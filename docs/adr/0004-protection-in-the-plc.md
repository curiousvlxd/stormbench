# 0004. Protection lives in the PLC, coordination only proposes setpoints

Status: accepted, 2026-09-28

## Context

In networks with real-time control, local stations must keep working safely when the central coordination fails, and a wrong setpoint from the center must never drive a throttle outside its limits. The design principle is that safety-relevant logic sits in the outstations and a global optimiser can only propose.

## Decision

The ST program is split like a distributed installation, even though Phase 1 runs it in one runtime:

- `FB_BasinStation`, one instance per controlled tank outlet: accepts a remote setpoint only while coordination is enabled, limits it to its own range, and otherwise runs its local Base Case setting.
- `FB_EfdCoordinator`: computes Equal Filling Degree setpoints for all four stations. It only proposes; the stations decide.
- Coordination is enabled only when a coordinated mode is selected, the center is reachable (`CenterOk`) and every controlled tank level is plausible (0 to 5.05 m; NaN is never plausible).
- The status word reports fallback, implausible input and clamping.

## Consequences

- Loss of the center or an implausible level degrades the network to Base Case, never below it. The Python reference implements the same rules, so fault scenarios can be validated the same way as normal operation.
- In normal operation the protection layer changes nothing (EFD setpoints are always within 0..1), which is why the PLC run matches the published EFD result exactly.
- Phase 4 can split the stations into separate PLC containers and cut links mid-storm (`--outage START END` already drives `CenterOk`).
