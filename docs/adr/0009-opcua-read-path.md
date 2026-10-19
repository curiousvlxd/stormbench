# 0009. OPC UA as a read path next to Modbus

Status: accepted, 2026-09-28

## Context

German automation projects connect to PLCs over OPC UA far more often than over Modbus. OpenPLC Runtime v4 has an OPC UA server plugin (asyncua 1.1.8), but its configuration (`conf/opcua.json`) is normally produced by the OpenPLC Editor from STruC++'s debug map, and the testbed builds the PLC without the Editor (ADR 0002). The lockstep needs consistent snapshots and fast round trips (ADR 0003).

## Decision

- **Generate `conf/opcua.json` in the headless build** (`plc/build-bundle.mjs`), as the Editor 4.3.1 does: every located variable of the PLC program becomes a read-only Variable node `ns=urn:stormbench:plc;s=PLC.main.{Name}` (LINT as Int64, LREAL as Double), SecurityPolicy None, anonymous read-only, publishing cycle 100 ms. The generated file is byte-identical to the Editor's for the same program. A new located variable appears in OPC UA without a second edit.
- **Application URI = `urn:freeopcua:python:server`**, the URI the server actually announces: the plugin never applies the configured URI, and the OPC Foundation .NET client refuses a session whose certificate carries a different URI (`BadCertificateUriInvalid`), even with SecurityPolicy None.
- **The edge gateway can read over OPC UA** (`Plc:Driver = OpcUa`): one Read request for all 23 values per poll, with the handshake counters (`StepReq`, `StepAck`) requested first. OpenPLC applies a Modbus write in address order and commits outputs in declaration order with the counters last, so a new counter value implies that the values of its step are visible. The gateway rediscovers the endpoint for every session, because each new PLC container generates a new self-signed certificate.
- **Proposals are still written over Modbus.** Anonymous OPC UA sessions on OpenPLC are read-only by design here; OPC UA writes would need an operator role and land as soft writes that the next scan may overwrite.
- **Modbus stays the default for the lockstep**: one request returns the whole register image, and it is about three times faster.

## Consequences

- Measured, all in containers, three-day storm through the UNS with the gateway reading over OPC UA: 863 steps, 0 differences to the Python reference, volumes equal to the golden values; about 8.5 ms per step against about 3 ms with Modbus.
- The OPC UA and Modbus plugins share one Python interpreter in the runtime. In the spike, an OPC UA client polling every 10 ms slowed the Modbus lockstep by about a third, at 100 ms by about 8%. The server's publishing cycle stays at 100 ms.
- Refused OPC UA writes return `BadInternalError` for the whole request rather than an access-denied status per value (runtime behaviour, noted for Phase 3 HMI work).
- Clients should connect by IP: on Windows, `localhost` resolves to IPv6 first and added about two seconds to every session setup while the port was published on 127.0.0.1 only.
