# 0006. No wall-clock timers in the control logic

Status: accepted, 2026-09-28

## Context

PLC timers (`TON`, `TOF`, `TIME()`) advance with the runtime's scan clock, which is wall time. In the testbed the model runs faster than real time and is paused while it waits for the PLC, so wall time and process time have no fixed relation. A timer-based rule (a delay, a ramp, a debounce, a communication watchdog) would therefore behave differently at every simulation speed, and results would stop being reproducible.

## Decision

- The control logic contains no timers and no calls to the system clock.
- The simulator sends simulated time (`SimTimeS`, seconds since start) with every step. Logic that needs time uses it, or counts lockstep steps.
- Loss of communication is modelled by the simulator (`CenterOk`), not by a wall-clock watchdog in the PLC.

## Consequences

- Results are identical at any speed: the fast run, the paced demo run and a run on a slower machine give the same numbers.
- On a real installation a watchdog on the center link would be a wall-clock timer. Where the testbed needs one (Phase 4, communication loss scenarios), it will be written against `SimTimeS`, and the difference to a plant deployment will be stated in the code.
