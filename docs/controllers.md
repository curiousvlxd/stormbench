# Plugging in a control algorithm

The testbed has two places where an algorithm can sit, as in a real installation (ADR 0004, ADR 0007):

| Plug point | What it decides | Who checks it | Examples |
|---|---|---|---|
| **Center** (`stormbench_sim.runner.Center`) | proposes one setting per controlled outlet, every control step | the PLC stations (mode 3): limits, plausibility, fallback to the Base Case | EFD in Scada, the MPC (`stormbench_sim/mpc.py`, ADR 0017) |
| **Controller** (`stormbench_sim.runner.Controller`) | the whole field side: the setpoints, mode and status the stations apply | nothing, it replaces the stations | the PLC (`plc.PlcController`), its Python reference (`runner.PythonController`) |

Write a **center** to test an algorithm. It then runs behind the same station checks as everything else, offline against the Python reference of the PLC program, or with the real PLC in the loop, and the per-step shadow check stays exact.

## The Center protocol

```python
class Center(Protocol):
    name: str

    def propose(self, inputs: ControlInputs) -> Sequence[float]: ...

    def observe(self, inputs: ControlInputs, outputs: ControlOutputs) -> None: ...
```

- `propose` is called once per 5 min control step, after the model has advanced one interval. `inputs.depths` are the tank levels the field reports, in `Network.tanks` order (a frozen sensor shows up here). `inputs.sim_time_s` is model time since the start of the run. Return one setting per station in `Network.stations` order (V2, V3, V4, V6 for Astlingen); they act during the next interval.
- A setting must be within 0..1 and finite. Anything else is an implausible proposal: the stations run their Base Case setting and the status word reports it (bit 8).
- `observe` reports what the stations applied: the proposal, or their local setting when they did not take it (link to the center cut, implausible level or proposal).
- `propose` is not called when the link to the center is down; the stations run locally.
- Keep it deterministic and free of wall-clock time (ADR 0006): the same inputs must give the same proposal, or runs stop being reproducible.

## A minimal center

[`simulator/examples/fill_proportional.py`](../simulator/examples/fill_proportional.py) opens each outlet in proportion to its tank's filling degree, never below the Base Case:

```python
class FillProportional:
    name = "fill-proportional"

    def __init__(self, network: Network):
        self.network = network

    def propose(self, inputs: ControlInputs) -> Sequence[float]:
        settings = []
        for station in self.network.stations:
            depth = inputs.depths[self.network.tanks.index(station.tank)]
            fill = depth / self.network.tank_max_depth_m
            settings.append(min(1.0, max(station.base_setting, fill)))
        return settings

    def observe(self, inputs: ControlInputs, outputs: ControlOutputs) -> None:
        pass


def make_center(network: Network, work_dir: Path, period: tuple[datetime, datetime] | None):
    return FillProportional(network)
```

The factory gets the network, a working directory and the simulated period (None: the model's own), so a center can prepare itself, for example with a pre-run as the MPC does for its forecast.

## Running it

Offline, behind the Python reference of the PLC stations (no containers needed):

```bash
cd simulator
PYTHONPATH=examples uv run python -m stormbench_sim run fill --control python --mode efd-center \
    --center fill_proportional:make_center --start 2000-05-30T00:00 --end 2000-05-31T12:00
```

With the PLC in the loop: start only the PLC (`docker compose up --build --detach --wait plc`, Modbus on `localhost:1502`). This process then plays the center: it writes each proposal into the PLC's center registers together with the step, the PLC stations check it, and the shadow check recomputes the stations' decision from exactly the proposal written. The exit code is 1 if any PLC answer differed.

```bash
PYTHONPATH=examples uv run python -m stormbench_sim run fill --control plc --mode efd-center \
    --center fill_proportional:make_center --plc-host localhost --plc-port 1502 \
    --start 2000-05-30T00:00 --end 2000-05-31T12:00
```

Do not point this at the PLC of a running UNS stack (`docker-compose.uns.yml`): Scada's EFD would answer the same steps. In that stack, run centers through the test console, where Scada stands aside for an algorithm whose catalog entry names another center (ADR 0017).

In Python, the same in a few lines:

```python
from stormbench_sim.control import Mode
from stormbench_sim.network import Controls, load_network
from stormbench_sim.runner import CenterController, RunSpec, run

net = load_network("astlingen", Path("models/astlingen"))
result = run(
    RunSpec("fill", net, Controls.EXTERNAL, Mode.EFD_CENTER, CenterController(FillProportional(net), net)),
    Path("work"),
)
print(result.totals["flood_total_m3"])
```

`python -m stormbench_sim compare` runs Base Case, EFD and the MPC on the console storms (`--year` adds the rain year) and writes `results/controllers.md`; it is the place to add a new center for a side-by-side table.

## Writing a controller instead

A `Controller` has one method, `decide(inputs) -> ControlOutputs`, and replaces the field side: the runner applies its setpoints as they are. Use it to model a different field (another PLC, other station logic); for a new algorithm, a center is the better fit, because the station checks then stay in the loop.
