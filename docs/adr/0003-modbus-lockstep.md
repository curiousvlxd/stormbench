# 0003. Modbus TCP with a lockstep handshake between simulator and PLC

Status: accepted, 2026-09-28

## Context

The hydraulic model runs much faster than real time (a year in about 30 s), the PLC scans on a wall-clock tick. Results must not depend on how fast either side happens to run, and the loop must be deterministic so that a PLC run can be compared step by step with a Python reference.

## Decision

- Phase 1 uses Modbus TCP: every PLC runtime and SCADA stack speaks it, and it keeps the loop simple. OPC UA follows in Phase 2.
- Lockstep: at each 5-minute control step the simulator writes all inputs plus a request number (`StepReq`) in one FC16 request, then polls one FC3 block until the PLC echoes the number in `StepAck`. Only then are the new setpoints applied and the model advanced. See `docs/modbus-map.md`.
- All values are 64-bit (`%ML`): LREAL levels and setpoints, LINT counters. No integer scaling, so no rounding difference between the PLC and the reference.
- The simulator runs inside the compose network, not on the host: through Docker Desktop port forwarding each request costs about 0.25 ms more.
- Every PLC answer is shadowed by the Python reference; any difference is counted and the first one is stored with the run.

## Consequences

- Run time is independent of results. Measured: about 990 handshakes per second container to container at `T#1ms` (Phase 0 soak: 105,000 handshakes in 105 s, no errors). The full PLC-in-the-loop year (105,406 steps, including SWMM) took 139 s inside `docker compose` and 222 s with the simulator on the Windows host talking to the container through Docker Desktop.
- The shadow check found a real integration issue: OpenPLC v4 commits changed outputs one by one in declaration order without the image lock, so a read could see a new `StepAck` with the previous setpoints (3 of 105,406 steps). `StepAck` is now declared last. This is exactly the class of problem a software-in-the-loop bench exists to find before a real plant does.
- A `demo` pacing option slows the loop to a fixed wall time per step for live displays; results are unchanged.
