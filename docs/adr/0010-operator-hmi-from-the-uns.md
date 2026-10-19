# 0010. Operator HMI in Scada, drawn from the unified namespace

Status: accepted, 2026-09-28

## Context

Phase 3 needs an HMI a newcomer can use within minutes: a process display of the network with tanks, valves and overflow structures, an alarm banner, key figures, and a test console to pick a storm and a control algorithm. Architecture principles: the HMI controls nothing directly (commands go through the UNS and the PLC decides), the UNS is the single source of truth, and the network is data, not code (ADR 0007). Three facts the display needs were missing from the UNS: the positions of the network's elements, the overflow at the ten overflow points, and the rainfall. Only the simulator knew them, and it had no UNS connection.

## Decision

- **The simulator becomes Sparkplug edge node `Sim01`.** It stands in for the telemetry a real network has besides its PLCs (rain gauges, overflow measurement).
  - Its birth carries the network description (`Model/Network`: coordinates and topology read from the SWMM model at run time, so the unlicensed model is never stored in the repository) and the scenario catalog.
  - Every control step it publishes rain, overflow per point, the inflow to the treatment plant, the Base Case total of the same storm at the same moment (computed offline before the live run starts) and the shadow check's mismatch count.
  - It follows the same Sparkplug B 3.0 rules as the .NET edge node (ADR 0008), in Python on paho-mqtt with MQTT 3.1.1, because the broker's Sparkplug Aware plugin crashes on births carrying MQTT 5 properties.
- **The test console sends NCMD through the UNS.** Start carries storm, algorithm and pace in one command; Stop ends the run. Scada only checks what the simulator would refuse anyway; the simulator decides and reports `Scenario/State` and `Scenario/Message`. The console runs the simulated storm, never the plant.
- **The HMI is Razor components inside `StormBench.Scada`** (Blazor Web App, interactive server rendering, no separate service). It reads the modules' read models in process: UNS state, alarms, the trend of the current run, KPIs. A page rebuilds its view only when one of them reports a change, at most ten times a second.
- **The process display is drawn from `Model/Network`.** Map coordinates with the y axis turned down; tanks as vessels with the 1 m EFD threshold marked, tank outlets as valves (controlled or fixed), overflow structures without storage as weir chambers. Vessels are drawn far larger than to scale, so a structure that would touch a tank is moved clear and its pipes follow. Labels are placed automatically: each tries a few positions around its symbol and takes the one that crosses the fewest pipes and symbols. Another network needs no hand layout.
- **ISA-101 style.** Grey process, blue water, colour only for abnormal states. Red, orange and yellow belong to alarm priorities 1 to 3; each priority also has its own shape and number. Unacknowledged alarms blink (not under reduced motion). Stale values stay visible, greyed with a question mark. The Base Case reference in the trend is a dashed grey line, not a second colour. Light and dark theme, Atkinson Hyperlegible Next (palette and typeface later replaced, see ADR 0013).
- **ISA-18.2 alarm states** in a new `Alarms` module:
  - An alarm becomes unacknowledged when its condition turns true, and leaves the list only when the condition has cleared and it has been acknowledged; a condition that clears first leaves it returned-unacknowledged.
  - Conditions are evaluated only on good data, so stale values neither raise nor clear alarms. Communication alarms (broker, Edge01, PLC01, Sim01) report stale data. Losing the broker is an alarm; not yet having reached it right after a start is not.
  - Sources: overflow at each point, the PLC status word (fallback, implausible level, clamped setpoint, rejected proposal), communication.
  - Shelving and suppression come later; the persistent alarm journal followed in ADR 0015.
- **KPI tile in three states:** the overflow of the running storm against the Base Case at the same moment; within ±1 % it is the same, below better, above worse. Only "worse" is coloured.

## Consequences

- Measured on the Aspire stack:
  - the three-day storm in mode 3 at full speed gives the golden volume (12,069.95 m³), with 0 of 863 PLC answers differing from the reference and no sequence gap in Sim01's data messages;
  - the storm of 30 May at 10 steps per second takes 47 s for 431 steps and spills 11,967 m³ against 12,931 m³ in the Base Case (−7.5 %), the same numbers as the Python reference.
- The whole testbed is usable without a command line: start the AppHost (or `docker compose -f compose.uns.yaml up`), open the HMI, press Start.
- The Sim01 contract exists in C# and Python; a test compares the two. The Python Sparkplug node has its own unit tests, and the console's commands are tested against a real broker.
- The simulator still writes the mode (CmdMode) as part of the scenario. In a plant, a mode change would be an operator command to the PLC by DCMD; that belongs to the Phase 4 fault scenarios.
- The trend covers the current run only and lives in memory. The historian (VictoriaMetrics, ADR 0015) keeps every run beyond that.
