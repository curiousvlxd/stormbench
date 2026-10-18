from __future__ import annotations

import threading
import time
from collections.abc import Sequence
from dataclasses import dataclass, field, replace
from typing import TYPE_CHECKING

from pymodbus.client import ModbusTcpClient

from . import registers as reg
from .control import STATUS_FALLBACK, ControlInputs, ControlOutputs, Mode, center_proposal, decide
from .network import Network

if TYPE_CHECKING:
    from .runner import Center


class PlcError(RuntimeError):
    pass


@dataclass
class LinkStats:
    exchanges: int = 0
    polls: int = 0
    wait_s: float = 0.0


class PlcLink:
    def __init__(self, host: str, port: int = 502, timeout_s: float = 10.0):
        self.host, self.port, self.timeout_s = host, port, timeout_s
        self.client = ModbusTcpClient(host, port=port, timeout=3, retries=1)
        self.stats = LinkStats()
        self._seq: int | None = None

    def connect(self, attempts: int = 30, delay_s: float = 1.0) -> None:
        for _ in range(attempts):
            if self.client.connect():
                break
            time.sleep(delay_s)
        else:
            raise PlcError(f"cannot connect to PLC at {self.host}:{self.port}")
        self._seq = self._read_outputs()[0]

    @property
    def seq(self) -> int | None:
        """Number of the last step sent to the PLC (its StepReq)."""
        return self._seq

    def close(self) -> None:
        self.client.close()

    def __enter__(self) -> PlcLink:
        self.connect()
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def _read_outputs(self) -> tuple[int, tuple[float, ...], int, int, int]:
        rr = self.client.read_holding_registers(
            reg.ml(reg.OUT_FIRST), count=reg.OUT_COUNT * reg.REGS_PER_VALUE
        )
        if rr.isError():
            raise PlcError(f"read failed: {rr}")
        return reg.decode_outputs(rr.registers)

    def exchange(
        self,
        inputs: ControlInputs,
        proposal: Sequence[float] | None = None,
        center_deadline_s: float | None = None,
    ) -> tuple[ControlOutputs, bool]:
        """One lockstep step. `proposal`: act as the central optimiser and write its setpoints
        for this step; without it, in EFD_CENTER mode the edge gateway has to deliver them.
        `center_deadline_s`: if the PLC has not answered by then (it waits for the center's
        proposal), the step is sent again with CenterOk = 0 and the stations run locally.
        Returns the outputs and whether the step was given up on the center."""
        if self._seq is None:
            raise PlcError("not connected")
        self._seq += 1
        self._write_inputs(inputs)
        if proposal is not None:
            wr = self.client.write_registers(
                reg.ml(reg.CENTER_SETPOINT_FIRST), reg.encode_center(self._seq, proposal)
            )
            if wr.isError():
                raise PlcError(f"center write failed: {wr}")
        t0 = time.perf_counter()
        gave_up = False
        while True:
            self.stats.polls += 1
            ack, setpoints, mode_active, status, _ = self._read_outputs()
            if ack == self._seq:
                break
            waited = time.perf_counter() - t0
            if center_deadline_s is not None and not gave_up and waited > center_deadline_s:
                self._write_inputs(replace(inputs, center_ok=False))
                gave_up = True
            if waited > self.timeout_s:
                raise PlcError(f"no ACK for step {self._seq} within {self.timeout_s} s (last ACK {ack})")
        self.stats.exchanges += 1
        self.stats.wait_s += time.perf_counter() - t0
        return ControlOutputs(setpoints, Mode(mode_active), status), gave_up

    def _write_inputs(self, inputs: ControlInputs) -> None:
        payload = reg.encode_inputs(
            self._seq, inputs.sim_time_s, inputs.mode, inputs.center_ok, inputs.depths
        )
        wr = self.client.write_registers(reg.ml(0), payload)
        if wr.isError():
            raise PlcError(f"write failed: {wr}")


@dataclass
class ShadowCheck:
    """Counts steps where the PLC answer differs from the Python reference."""

    mismatches: int = 0
    first: dict | None = field(default=None)

    def check(self, step: int, inputs: ControlInputs, plc: ControlOutputs, ref: ControlOutputs) -> None:
        if plc != ref:
            self.mismatches += 1
            if self.first is None:
                self.first = {"step": step, "inputs": inputs, "plc": plc, "python": ref}


class PlcController:
    """Controller that asks the PLC, optionally shadowed by the Python reference.

    In EFD_CENTER mode the proposals come either from the central optimiser over the UNS
    (`emulate_center=False`, Phase 2 stack running) or from this process, which then plays
    the center itself: published EFD (`emulate_center=True`, tests the PLC path without the
    .NET services) or any `center` (runner.Center, e.g. the MPC). A center played here writes
    its proposal into the PLC's center registers itself, and the shadow check uses exactly the
    proposal written.

    The link to the center can fail, or be cut on purpose (`link_cut`). The PLC waits for
    the center only up to `center_deadline_s`; then the step goes to the PLC with CenterOk = 0
    and its stations run locally. While the center stays silent, steps go with CenterOk = 0
    right away, and every `center_retry_s` one step tries the center again.
    """

    def __init__(
        self,
        link: PlcLink,
        network: Network,
        shadow: bool = True,
        emulate_center: bool = False,
        center_variant: Mode = Mode.EFD_PUBLISHED,
        center_deadline_s: float = 2.0,
        center_retry_s: float = 10.0,
        link_cut: threading.Event | None = None,
        center: Center | None = None,
    ):
        self.link, self.network = link, network
        self.shadow = ShadowCheck() if shadow else None
        self.emulate_center = emulate_center or center is not None
        self.center = center
        self.center_variant = center_variant
        self.center_deadline_s = center_deadline_s
        self.center_retry_s = center_retry_s
        self.link_cut = link_cut
        self.center_lost_at: float | None = None
        self.center_fallbacks = 0
        self._step = 0
        self.name = "plc"

    @property
    def center_link_up(self) -> bool:
        return self.center_lost_at is None and not self._link_is_cut

    @property
    def _link_is_cut(self) -> bool:
        return self.link_cut is not None and self.link_cut.is_set()

    @property
    def _center_recently_lost(self) -> bool:
        return (
            self.center_lost_at is not None
            and time.perf_counter() - self.center_lost_at < self.center_retry_s
        )

    def decide(self, inputs: ControlInputs) -> ControlOutputs:
        self._step += 1
        proposal = None
        deadline = None
        if inputs.mode == Mode.EFD_CENTER and inputs.center_ok:
            if self._link_is_cut or (not self.emulate_center and self._center_recently_lost):
                inputs = replace(inputs, center_ok=False)
            elif self.center is not None:
                proposal = list(self.center.propose(inputs))
            elif self.emulate_center:
                proposal = center_proposal(inputs, self.network, self.center_variant)
            else:
                deadline = self.center_deadline_s
        out, gave_up = self.link.exchange(inputs, proposal, deadline)
        if deadline is not None:
            self.center_lost_at = time.perf_counter() if gave_up else None
        if gave_up:
            self.center_fallbacks += 1
            if out.status & STATUS_FALLBACK:
                inputs = replace(inputs, center_ok=False)
        if self.shadow is not None:
            expected = decide(inputs, self.network, proposal, self.center_variant)
            self.shadow.check(self._step, inputs, out, expected)
        if self.center is not None:
            self.center.observe(inputs, out)
        return out
