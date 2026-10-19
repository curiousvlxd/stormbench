# 0007. Unified namespace over Sparkplug B, with the central optimiser inside the lockstep

Status: accepted, 2026-09-28

## Context

Phase 2 adds the industrial data path: PLC, edge gateway, a unified namespace (UNS) on MQTT, and central services that read the process and propose setpoints. Two architecture principles apply: the UNS is the single source of truth (services do not call each other), and protection stays in the PLC, so a central optimiser may only propose (ADR 0004). The Phase 1 guarantee must hold: results are deterministic and every PLC answer can be checked against the Python reference.

## Decision

- **Topics and names** follow Sparkplug B 3.0 (`spBv1.0/{group}/{type}/{edge node}[/{device}]`), read as an ISA-95 hierarchy: group `Astlingen` (site), edge node `Edge01` (the gateway next to the PLC), device `PLC01`. Metric names continue the hierarchy (`Storage/T2/Level`, `Outlets/V2/Setpoint`, `Lockstep/StepReq`). One definition in `StormBench.Contracts` serves publisher and consumers.
- **Edge gateway (`StormBench.Connectivity`)** reads the PLC's register image with one Modbus request per poll, so every published set of values comes from one consistent snapshot, and publishes only changed metrics (report by exception). Commands arrive as DCMD and are written to the PLC in one request.
- **Scada (`StormBench.Scada`)** is the Sparkplug Primary Host: it announces itself on `spBv1.0/STATE/Scada01`, keeps the UNS state with quality per metric, marks data stale on NDEATH (matching bdSeq), DDEATH or a lost broker connection, and requests a rebirth on sequence gaps. Its Optimization module computes Equal Filling Degree on every new lockstep step in mode 3 and sends the proposal as DCMD.
- **Mode 3 in the PLC**: the stations take the center's proposal instead of the local coordinator's, with the same limits and fallback. **In the testbed the center is part of the lockstep**: the PLC waits for the proposal tagged with the current step (`CenterStep = StepReq`), unless the simulator has marked the link as lost. The full chain (PLC, gateway, broker, Scada and back) fits inside one control step of model time, so runs stay deterministic.
- **Mosquitto 2.1** with the Sparkplug Aware plugin, `set_tcp_nodelay true`.

## Consequences

- The Phase 1 validation carries over unchanged: in mode 3 every PLC answer must equal the Python decision with published EFD, and the volumes must equal the Phase 1 values. Measured:
  - three-day storm of 29 May 2000, all in containers (`compose.uns.yaml`): 863 steps, 0 mismatches, volumes equal to the golden values;
  - the whole rain year through the UNS (Aspire, services on the host): 105,406 steps in 16.5 minutes, 0 mismatches, setpoints, tank levels and volumes bit for bit equal to the Python reference run on the same machine; total overflow 247,787 m³ (-6.40%), as in Phase 1.
- Latency of one step through the whole chain: about 3 ms container to container, about 6 to 7 ms with the services on the Windows host behind Docker Desktop's port forwarding. Without `set_tcp_nodelay`, Nagle's algorithm and delayed ACKs in Mosquitto added about 40 ms per step (45 ms in total); this one broker option made the loop seven times faster.
- A real station would not wait for the center: it would keep the last valid proposal and fall back to local control on a watchdog. The testbed's waiting is stated in the ST source and in ADR 0006 (no wall-clock timers). The watchdog followed in ADR 0011.
- If the center does not answer (stale data, Scada down), the simulator's step times out and the run fails loudly instead of silently producing different numbers. Superseded by ADR 0011: the stations now fall back and the run continues.
