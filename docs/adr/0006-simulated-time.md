# 0006. No wall-clock timers in the control logic

Status: accepted, 2026-09-28

## Context

PLC timers (`TON`, `TOF`, `TIME()`) advance with the runtime's scan clock, which is wall time. In the testbed the model runs faster than real time and pauses while it waits for the PLC, so wall time and process time have no fixed relation. A timer-based rule (delay, ramp, debounce, communication watchdog) would behave differently at every simulation speed, and results would not be reproducible.

## Decision

- The control logic contains no timers and no calls to the system clock.
- The simulator sends simulated time (`SimTimeS`, seconds since start) with every step. Logic that needs time uses it, or counts lockstep steps.
- The simulator models loss of communication (`CenterOk`); the PLC has no wall-clock watchdog.

## Consequences

- Results are identical at any speed: the fast run, the paced demo run and a run on a slower machine give the same numbers.
- On a real installation a watchdog on the center link would be a wall-clock timer. The plan was to write the testbed's against `SimTimeS`; ADR 0011 instead puts the center watchdog in the simulator, and the PLC stays free of timers.
