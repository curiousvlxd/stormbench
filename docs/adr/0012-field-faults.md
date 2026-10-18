# 0012. Field faults the console can inject, and the checks that find them

Status: accepted, 2026-10-15

## Context

The testbed should show how the system copes when the field misbehaves, not only when the link to the center fails (ADR 0011). Two classic field faults are not visible to the PLC's own plausibility check (ADR 0004), which only rejects levels outside the tank or NaN: a gate that no longer moves, and a level sensor that freezes at a plausible value.

## Decision

- **Faults live in the simulated field.** Sim01 takes `Fault/StuckGate` (a controlled outlet, or empty) and `Fault/FrozenSensor` (a tank, or empty) by NCMD from the test console. A stuck gate keeps its opening whatever the PLC asks; a frozen sensor reports the level it had when the fault began. Both take effect at the next control step and stay until cleared.
- **The field reports what a real one could.** Per step Sim01 publishes the opening each controlled outlet really has (position feedback, `Outlets/{V}/Opening`) and, as a testbed extra, the real tank level (`Storage/{T}/TrueLevel`), which the HMI shows only while a sensor is frozen.
- **Scada finds them, the PLC does not have to.** A new check in the Alarms module raises `Gate {V} does not follow its setpoint` when position feedback and PLC setpoint differ by more than 5 % for three steps, and `Level reading of {T} frozen` when a level has not changed for six steps (30 min) while the tank is neither empty nor full. Both are priority 2.
- **Reports are paired by step.** The field's report of a step can reach Scada before the gateway's; comparing them as they arrive raised false gate alarms while EFD switched setpoints every step. Sim01 now publishes the PLC step number of each report (`Lockstep/PlcStep`), and the check waits for the PLC's values of that same step.

## Consequences

- Measured on the Aspire stack, storm of 30 May in mode 3: two healthy runs (full speed and 10 steps per second) raised no field alarm; with V2 stuck and T4 frozen from 21:00, both alarms came up in every run. What the faults cost depends on where the gate sticks: stuck fully open, the storm spilled 3 % more than the Base Case (healthy EFD: 7.5 % less); stuck at its base opening, 8.3 % less.
- A frozen sensor on an empty tank is not found: an empty tank legitimately stays at zero. A gate stuck at a value the controller keeps returning to is found only while the controller asks for something else.
- The shadow check stays exact: the reference gets the same, possibly frozen, levels as the PLC.
