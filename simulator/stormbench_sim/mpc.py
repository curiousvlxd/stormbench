from __future__ import annotations

import math
import time
from collections.abc import Sequence
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

import numpy as np
from pyswmm import Links, Nodes, Simulation
from scipy import sparse
from scipy.optimize import linprog

from .control import ControlInputs, ControlOutputs
from .network import Controls, Network, _section_rows, prepare_inp

GRAVITY = 32.2 * 0.3048

PAPER_OVERFLOW_WEIGHTS = {"T1": 1000.0, "T2": 5000.0, "T3": 5000.0, "T4": 5000.0, "T5": 5000.0, "T6": 10000.0}


@dataclass(frozen=True)
class Orifice:
    """A side orifice with a rectangular opening; `setting` scales the opening height, as in SWMM."""

    name: str
    height_m: float
    width_m: float
    discharge_coeff: float
    offset_m: float

    def flow(self, depth_m: float, setting: float) -> float:
        """Free outflow (m³/s) at this tank depth, after SWMM's orifice routine for side orifices:
        orifice flow on the head above the opening's midpoint, weir flow while the opening is not
        submerged. Backwater from the downstream node is ignored (the Astlingen outlets drop 2 m)."""
        opening = max(0.0, min(setting, 1.0)) * self.height_m
        head = depth_m - self.offset_m
        if opening <= 0.0 or head <= 0.0:
            return 0.0
        c_orif = self.discharge_coeff * opening * self.width_m * math.sqrt(2.0 * GRAVITY)
        if head < opening:
            return c_orif * math.sqrt(opening / 2.0) * (head / opening) ** 1.5
        return c_orif * math.sqrt(head - opening / 2.0)

    def setting_for(self, flow_m3s: float, depth_m: float) -> float:
        """Smallest setting in 0..1 that passes `flow_m3s` at this depth (the orifice equation
        inverted by bisection; flow does not decrease with the setting)."""
        if flow_m3s <= 0.0:
            return 0.0
        if flow_m3s >= self.flow(depth_m, 1.0):
            return 1.0
        lo, hi = 0.0, 1.0
        for _ in range(50):
            mid = 0.5 * (lo + hi)
            if self.flow(depth_m, mid) < flow_m3s:
                lo = mid
            else:
                hi = mid
        return hi


@dataclass(frozen=True)
class Tank:
    name: str
    area_m2: float
    max_depth_m: float
    outlet: Orifice
    controlled: bool

    @property
    def max_volume_m3(self) -> float:
        return self.area_m2 * self.max_depth_m


@dataclass(frozen=True)
class Probe:
    """An inflow the controller cannot change, measured in the pre-run: the flow of a link, or the
    lateral inflow of a node, that reaches `tank` after `delay_steps` control steps."""

    kind: str
    element: str
    tank: str
    delay_steps: int


@dataclass(frozen=True)
class ConceptualModel:
    """The internal model: the tanks a controlled outlet can influence, their routing and the
    inflows they receive from the rest of the network."""

    step_s: int
    tanks: tuple[Tank, ...]
    downstream: dict[str, tuple[str, int]]
    to_wwtp: tuple[str, ...]
    probes: tuple[Probe, ...]
    stations: tuple[str, ...]

    def tank(self, name: str) -> Tank:
        return next(t for t in self.tanks if t.name == name)


def _float(text: str) -> float:
    return float(text)


def _travel_time_s(row: list[str], xsect: list[str], invert: dict[str, float]) -> float:
    """Travel time through a conduit at its full-flow Manning velocity. Water at part-full flow
    moves slower, but the flood wave moves faster than the water (kinematic celerity), and the
    full-pipe velocity sits between the two; the delays end up rounded to 5 min steps anyway."""
    name, n1, n2, length, rough, off1, off2 = row[0], row[1], row[2], *map(_float, row[3:7])
    shape, geom1 = xsect[1], _float(xsect[2])
    if shape == "CIRCULAR":
        radius = geom1 / 4.0
    elif shape in ("RECT_CLOSED", "RECT_OPEN"):
        width = _float(xsect[3])
        radius = geom1 * width / (2.0 * geom1 + width)
    else:
        raise NotImplementedError(f"conduit {name}: cross section {shape} not supported by the MPC model")
    slope = max((invert[n1] + off1 - invert[n2] - off2) / length, 1e-4)
    velocity = radius ** (2.0 / 3.0) * math.sqrt(slope) / rough
    return length / velocity


def build_model(network: Network) -> ConceptualModel:
    """Read the conceptual model from the network's .inp: storage curves, outlet orifices, routing
    between tanks and the travel times of the conduits on the way."""
    text = network.inp_path.read_text(encoding="latin-1")
    step = network.control_step_s
    invert = {}
    for section in ("JUNCTIONS", "OUTFALLS", "STORAGE"):
        invert.update({row[0]: _float(row[1]) for row in _section_rows(text, section)})
    storage = {row[0]: row for row in _section_rows(text, "STORAGE")}
    curves: dict[str, list[tuple[float, float]]] = {}
    for row in _section_rows(text, "CURVES"):
        values = row[2:] if len(row) % 2 == 0 else row[1:]
        points = curves.setdefault(row[0], [])
        points += [(float(values[i]), float(values[i + 1])) for i in range(0, len(values) - 1, 2)]
    xsections = {row[0]: row for row in _section_rows(text, "XSECTIONS")}
    conduits = {row[0]: row for row in _section_rows(text, "CONDUITS")}
    orifices = {row[0]: row for row in _section_rows(text, "ORIFICES")}
    links: dict[str, tuple[str, str]] = {}
    for section in ("CONDUITS", "ORIFICES", "WEIRS", "PUMPS", "OUTLETS"):
        links.update({row[0]: (row[1], row[2]) for row in _section_rows(text, section)})
    out_links: dict[str, list[str]] = {}
    in_links: dict[str, list[str]] = {}
    for link, (a, b) in links.items():
        out_links.setdefault(a, []).append(link)
        in_links.setdefault(b, []).append(link)

    def link_time(link: str) -> float:
        return _travel_time_s(conduits[link], xsections[link], invert) if link in conduits else 0.0

    def route(node: str) -> tuple[list[tuple[str, float]], str]:
        """Nodes downstream of `node` up to the next storage or outfall, each with the travel time
        from it to that end, and the end itself."""
        path: list[tuple[str, str]] = []
        while True:
            outs = out_links.get(node, [])
            if len(outs) != 1:
                raise NotImplementedError(
                    f"node {node}: {len(outs)} outgoing links, the MPC model needs a tree"
                )
            path.append((node, outs[0]))
            node = links[outs[0]][1]
            if node in storage or node not in out_links:
                break
        times, remaining = [], sum(link_time(link) for _, link in path)
        for n, link in path:
            times.append((n, remaining))
            remaining -= link_time(link)
        return times, node

    station_tanks = [s.tank for s in network.stations]
    outlet_of = {row[1]: row[0] for row in orifices.values() if row[1] in storage}
    end_of: dict[str, tuple[str, list[tuple[str, float]]]] = {}
    for tank in storage:
        if tank not in outlet_of:
            raise NotImplementedError(f"storage {tank}: no outlet orifice")
        nodes, end = route(tank)
        end_of[tank] = (end, nodes[1:])
    in_lp = list(station_tanks)
    for tank in station_tanks:
        end = end_of[tank][0]
        while end in storage and end not in in_lp:
            in_lp.append(end)
            end = end_of[end][0]

    def make_tank(name: str) -> Tank:
        row = storage[name]
        max_depth, shape = float(row[2]), row[4].upper()
        if shape != "TABULAR":
            raise NotImplementedError(f"storage {name}: only TABULAR storage curves are supported")
        pts = curves[row[5]]
        depths = np.array([p[0] for p in pts])
        areas = np.array([p[1] for p in pts])
        grid = np.linspace(0.0, max_depth, 101)
        volume = float(np.trapezoid(np.interp(grid, depths, areas), grid))
        orow = orifices[outlet_of[name]]
        xs = xsections[orow[0]]
        if orow[3].upper() != "SIDE" or xs[1] != "RECT_CLOSED":
            raise NotImplementedError(f"orifice {orow[0]}: only rectangular side orifices are supported")
        outlet = Orifice(orow[0], float(xs[2]), float(xs[3]), float(orow[5]), float(orow[4]))
        return Tank(name, volume / max_depth, max_depth, outlet, name in station_tanks)

    tanks = tuple(make_tank(t) for t in in_lp)
    downstream, to_wwtp = {}, []
    for tank in in_lp:
        end, nodes = end_of[tank]
        total = nodes[0][1] if nodes else 0.0
        if end in in_lp:
            downstream[tank] = (end, round(total / step))
        elif end == network.wwtp_outfall:
            to_wwtp.append(tank)

    probes = []
    for tank in in_lp:
        path_nodes = {tank: 0.0}
        for up in in_lp:
            if end_of[up][0] == tank:
                path_nodes.update(dict(end_of[up][1]))
        lp_outlets = {outlet_of[up] for up in in_lp if end_of[up][0] == tank}
        for node, t_node in path_nodes.items():
            probes.append(Probe("node", node, tank, round(t_node / step)))
            for link in in_links.get(node, []):
                if link in lp_outlets or links[link][0] in path_nodes:
                    continue
                t = t_node + link_time(link) / 2.0
                probes.append(Probe("link", link, tank, round(t / step)))
    return ConceptualModel(step, tanks, downstream, tuple(to_wwtp), tuple(probes), tuple(station_tanks))


@dataclass(frozen=True)
class InflowForecast:
    """Mean inflow per control interval j (from start + j*step_s) into each LP tank, from a pre-run.
    The inflows are those no control changes, so the pre-run's control does not matter (it runs
    the Base Case); small backwater effects are the only error, which makes this a perfect forecast
    in the sense of Sun et al. (2020), section 3."""

    start: datetime
    step_s: int
    tanks: tuple[str, ...]
    inflow_m3s: np.ndarray

    def window(self, sim_time_s: int, steps: int) -> np.ndarray:
        """Inflows for the `steps` intervals that begin at sim_time_s (zero past the end)."""
        j0 = sim_time_s // self.step_s
        out = np.zeros((steps, len(self.tanks)))
        part = self.inflow_m3s[j0 : j0 + steps]
        out[: len(part)] = part
        return out

    def persistence(self, sim_time_s: int, steps: int) -> np.ndarray:
        """No foresight: the inflow of the interval just ended, held for the whole horizon (what a
        controller that measures the tank inflows but has no forecast would assume)."""
        j = sim_time_s // self.step_s - 1
        row = self.inflow_m3s[j] if 0 <= j < len(self.inflow_m3s) else np.zeros(len(self.tanks))
        return np.tile(row, (steps, 1))


def record_forecast(
    network: Network,
    model: ConceptualModel,
    work_dir: Path,
    period: tuple[datetime, datetime] | None = None,
    sample_s: int = 60,
    tag: str = "forecast",
) -> InflowForecast:
    """Pre-run of the window with the Base Case rules: averages each probe over every control
    interval (sampled every `sample_s`) and shifts it by its delay to the tank."""
    inp = prepare_inp(network, work_dir, Controls.BASE, tag, period)
    step = model.step_s
    lp_tanks = tuple(t.name for t in model.tanks)
    samples: list[list[float]] = []
    with Simulation(str(inp)) as sim:
        nodes, links = Nodes(sim), Links(sim)
        readers = []
        for probe in model.probes:
            obj = links[probe.element] if probe.kind == "link" else nodes[probe.element]
            readers.append((obj, probe.kind == "link"))
        sim.step_advance(sample_s)
        start = sim.start_time
        for _ in sim:
            samples.append([obj.flow if is_link else obj.lateral_inflow for obj, is_link in readers])
    per_interval = step // sample_s
    data = np.asarray(samples)
    n = len(data) // per_interval
    means = data[: n * per_interval].reshape(n, per_interval, len(model.probes)).mean(axis=1)
    if len(data) > n * per_interval:
        means = np.vstack([means, data[n * per_interval :].mean(axis=0)])
    inflow = np.zeros((len(means), len(lp_tanks)))
    for p, probe in enumerate(model.probes):
        col = lp_tanks.index(probe.tank)
        d = probe.delay_steps
        inflow[d:, col] += means[: len(means) - d, p] if d else means[:, p]
    return InflowForecast(start, step, lp_tanks, inflow)


@dataclass(frozen=True)
class MpcConfig:
    """Defaults from Sun et al. (2020), section 3.2: 100 min horizon at the network's 5 min step,
    and the cost of eq. (14) with its weights: Table 3 on the accumulated overflow volume (an
    overflow at step k counts once for every remaining step of the horizon, eq. 16), 2 on the
    overflow flow, -1 on the flow to the WWTP, 0.01 on the change of the control flow (squared
    there, absolute here). Flows are in m³/s, volumes in m³, as in the paper.

    `time_weighting` "flat" counts every m³ of overflow the same, wherever in the horizon it falls
    (the accumulated-volume term then costs W per m³). `forecast` "perfect" uses the pre-run's
    inflows over the horizon, "persistence" only the inflow just measured (a sensitivity check:
    how much of the gain is foresight)."""

    horizon_steps: int = 20
    forecast: str = "perfect"
    overflow_weights: dict[str, float] = field(default_factory=lambda: dict(PAPER_OVERFLOW_WEIGHTS))
    time_weighting: str = "accumulated"
    overflow_flow_weight: float = 2.0
    wwtp_weight: float = 1.0
    change_weight: float = 0.01
    tangent_depths_m: tuple[float, ...] = (0.15, 0.3, 0.6, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0)


@dataclass
class SolveStats:
    solves: int = 0
    failures: int = 0
    total_s: float = 0.0
    max_s: float = 0.0
    times_s: list[float] = field(default_factory=list)


class LinearMpc:
    """The LP of one step, built once; each step only changes the right-hand sides and bounds.

    Variables per step k = 0..H-1 and LP tank i: outflow q[k,i] (m³/s), overflow w[k,i] (m³/s),
    volume V[k+1,i] (m³); per controlled tank, the change d[k,c] >= |q[k,c] - q[k-1,c]|.
    Mass balance: V[k+1,i] = V[k,i] + dT (inflow[k,i] + sum_u q[k - delay_ui, u] - q[k,i] - w[k,i]).
    Outflow: q[k,i] <= Qmax_i(V[k,i]) with Qmax the orifice equation at full opening (tangents of
    the concave curve, exact at k = 0); 0 <= V <= Vmax, q, w >= 0.
    Objective: weighted overflow volume + change penalty - reward for flow to the WWTP.
    """

    def __init__(self, model: ConceptualModel, config: MpcConfig):
        self.model, self.config = model, config
        H, n = config.horizon_steps, len(model.tanks)
        self.H, self.n = H, n
        names = [t.name for t in model.tanks]
        self.controlled = [i for i, t in enumerate(model.tanks) if t.controlled]
        nc = len(self.controlled)
        dt = float(model.step_s)
        self.dt = dt
        self.iq = lambda k, i: k * n + i
        self.iw = lambda k, i: H * n + k * n + i
        self.iv = lambda k, i: 2 * H * n + k * n + i
        self.id = lambda k, c: 3 * H * n + k * nc + c
        nvar = 3 * H * n + H * nc
        self.nvar = nvar

        self.upstream: dict[int, list[tuple[int, int]]] = {i: [] for i in range(n)}
        for up, (down, delay) in model.downstream.items():
            self.upstream[names.index(down)].append((names.index(up), delay))
        self.max_delay = max([d for ups in self.upstream.values() for _, d in ups], default=0)

        rows, cols, vals = [], [], []
        for k in range(H):
            for i in range(n):
                r = k * n + i
                rows += [r, r, r]
                cols += [self.iv(k, i), self.iq(k, i), self.iw(k, i)]
                vals += [1.0, dt, dt]
                if k > 0:
                    rows.append(r)
                    cols.append(self.iv(k - 1, i))
                    vals.append(-1.0)
                for u, delay in self.upstream[i]:
                    if k - delay >= 0:
                        rows.append(r)
                        cols.append(self.iq(k - delay, u))
                        vals.append(-dt)
        self.A_eq = sparse.csr_array((vals, (rows, cols)), shape=(H * n, nvar))

        rows, cols, vals, b = [], [], [], []
        r = 0
        self.tangents: list[list[tuple[float, float]]] = []
        for i, tank in enumerate(model.tanks):
            lines = []
            for h in config.tangent_depths_m:
                if h > tank.max_depth_m or h < 1.2 * tank.outlet.height_m:
                    continue
                eps = 1e-4
                q0 = tank.outlet.flow(h, 1.0)
                slope_h = (tank.outlet.flow(h + eps, 1.0) - tank.outlet.flow(h - eps, 1.0)) / (2 * eps)
                slope_v = slope_h / tank.area_m2
                lines.append((q0 - slope_v * h * tank.area_m2, slope_v))
            self.tangents.append(lines)
            for k in range(1, H):
                for a, slope in lines:
                    rows += [r, r]
                    cols += [self.iq(k, i), self.iv(k - 1, i)]
                    vals += [1.0, -slope]
                    b.append(a)
                    r += 1
        self.change_rows = r
        for c, i in enumerate(self.controlled):
            for k in range(H):
                for sign in (1.0, -1.0):
                    rows += [r, r]
                    cols += [self.iq(k, i), self.id(k, c)]
                    vals += [sign, -1.0]
                    if k > 0:
                        rows.append(r)
                        cols.append(self.iq(k - 1, i))
                        vals.append(-sign)
                    b.append(0.0)
                    r += 1
        self.A_ub = sparse.csr_array((vals, (rows, cols)), shape=(r, nvar))
        self.b_ub = np.asarray(b, dtype=float)

        cost = np.zeros(nvar)
        for k in range(H):
            remaining = H - k if config.time_weighting == "accumulated" else 1
            for i, tank in enumerate(model.tanks):
                volume_weight = config.overflow_weights.get(tank.name, 1.0) * remaining
                cost[self.iw(k, i)] = config.overflow_flow_weight + dt * volume_weight
                if tank.name in model.to_wwtp:
                    cost[self.iq(k, i)] = -config.wwtp_weight
            for c in range(nc):
                cost[self.id(k, c)] = config.change_weight
        self.cost = cost

        self.last_x: np.ndarray | None = None
        self.lower = np.zeros(nvar)
        self.upper = np.full(nvar, np.inf)
        for k in range(H):
            for i, tank in enumerate(model.tanks):
                self.upper[self.iv(k, i)] = tank.max_volume_m3

    def solve(
        self,
        volumes: np.ndarray,
        q_max_now: np.ndarray,
        q_prev: np.ndarray,
        inflow: np.ndarray,
        history: np.ndarray,
    ) -> np.ndarray | None:
        """Planned outflows q[k, i], or None if the solver fails.

        volumes, q_max_now: per LP tank now; q_prev: per LP tank, outflow in the interval just ended;
        inflow: (H, n) uncontrollable inflows; history: (max_delay, n) outflows of the past
        intervals, row 0 the most recent."""
        H, n, dt = self.H, self.n, self.dt
        b_eq = dt * inflow.reshape(-1).copy()
        b_eq[:n] += volumes
        for i in range(n):
            for u, delay in self.upstream[i]:
                for k in range(min(delay, H)):
                    b_eq[k * n + i] += dt * history[delay - k - 1, u]
        b_ub = self.b_ub.copy()
        r = self.change_rows
        for i in self.controlled:
            for k in range(H):
                for sign in (1.0, -1.0):
                    if k == 0:
                        b_ub[r] = sign * q_prev[i]
                    r += 1
        upper = self.upper.copy()
        for i in range(n):
            upper[self.iq(0, i)] = q_max_now[i]
        res = linprog(
            self.cost,
            A_ub=self.A_ub,
            b_ub=b_ub,
            A_eq=self.A_eq,
            b_eq=b_eq,
            bounds=np.column_stack([self.lower, upper]),
            method="highs",
        )
        self.last_x = res.x if res.status == 0 else None
        if res.status != 0:
            return None
        return res.x[: H * n].reshape(H, n)


class MpcCenter:
    """Central optimiser with MPC: proposes one setting per station for each step (Center protocol,
    see docs/controllers.md). `observe` tells it what the stations applied, which it needs for the
    outflows still travelling between tanks."""

    name = "mpc"

    def __init__(self, network: Network, model: ConceptualModel, forecast: InflowForecast, config: MpcConfig):
        self.network, self.model, self.forecast, self.config = network, model, forecast, config
        self.lp = LinearMpc(model, config)
        self.stats = SolveStats()
        self.tank_index = [network.tanks.index(t.name) for t in model.tanks]
        self.station_lp = [
            next(i for i, t in enumerate(model.tanks) if t.name == s.tank) for s in network.stations
        ]
        n = len(model.tanks)
        self._history = np.zeros((max(self.lp.max_delay, 1), n))
        self._pending: tuple[np.ndarray, np.ndarray] | None = None
        self._settings = np.array(
            [1.0 if not t.controlled else self._base(t.name) for t in model.tanks], dtype=float
        )

    @classmethod
    def for_run(
        cls,
        network: Network,
        work_dir: Path,
        period: tuple[datetime, datetime] | None,
        config: MpcConfig | None = None,
        tag: str = "forecast",
    ) -> MpcCenter:
        model = build_model(network)
        forecast = record_forecast(network, model, work_dir, period, tag=tag)
        return cls(network, model, forecast, config or MpcConfig())

    def _base(self, tank: str) -> float:
        return next(s.base_setting for s in self.network.stations if s.tank == tank)

    def _depths(self, depths: Sequence[float]) -> np.ndarray:
        out = []
        for t, idx in zip(self.model.tanks, self.tank_index, strict=True):
            d = depths[idx]
            out.append(min(max(d, 0.0), t.max_depth_m) if math.isfinite(d) else 0.0)
        return np.asarray(out)

    def _flows(self, depths: np.ndarray, settings: np.ndarray) -> np.ndarray:
        return np.array(
            [t.outlet.flow(d, s) for t, d, s in zip(self.model.tanks, depths, settings, strict=True)]
        )

    def propose(self, inputs: ControlInputs) -> list[float]:
        depths = self._depths(inputs.depths)
        if self._pending is not None:
            start_depths, settings = self._pending
            flows = 0.5 * (self._flows(start_depths, settings) + self._flows(depths, settings))
            self._history = np.vstack([flows, self._history[:-1]])
        q_prev = self._history[0]
        volumes = np.array([d * t.area_m2 for d, t in zip(depths, self.model.tanks, strict=True)])
        q_max = self._flows(depths, np.ones(len(depths)))
        if self.config.forecast == "persistence":
            inflow = self.forecast.persistence(inputs.sim_time_s, self.lp.H)
        else:
            inflow = self.forecast.window(inputs.sim_time_s, self.lp.H)
        t0 = time.perf_counter()
        plan = self.lp.solve(volumes, q_max, q_prev, inflow, self._history)
        elapsed = time.perf_counter() - t0
        st = self.stats
        st.solves += 1
        st.total_s += elapsed
        st.max_s = max(st.max_s, elapsed)
        st.times_s.append(elapsed)
        if plan is None:
            st.failures += 1
            return [s.base_setting for s in self.network.stations]
        proposal = []
        for i in self.station_lp:
            tank = self.model.tanks[i]
            proposal.append(tank.outlet.setting_for(plan[0, i], depths[i]))
        return proposal

    def observe(self, inputs: ControlInputs, outputs: ControlOutputs) -> None:
        """The settings the stations applied for the coming interval (the proposal, or the local
        setting on fallback)."""
        depths = self._depths(inputs.depths)
        settings = self._settings.copy()
        for i, sp in zip(self.station_lp, outputs.setpoints, strict=True):
            settings[i] = sp
        self._pending = (depths, settings)

    def summary(self) -> dict:
        st = self.stats
        times = np.asarray(st.times_s) if st.times_s else np.zeros(1)
        return {
            "solves": st.solves,
            "failures": st.failures,
            "mean_ms": round(1000 * float(times.mean()), 2),
            "p99_ms": round(1000 * float(np.percentile(times, 99)), 2),
            "max_ms": round(1000 * st.max_s, 2),
            "horizon_steps": self.config.horizon_steps,
            "time_weighting": self.config.time_weighting,
            "forecast": self.config.forecast,
            "overflow_weights": self.config.overflow_weights,
        }
