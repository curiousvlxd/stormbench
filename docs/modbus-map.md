# Modbus register map

The simulator is the Modbus TCP client; the PLC (OpenPLC Runtime v4) is the server on port 502, unit id 1.
All values are 64-bit, in OpenPLC's `%ML` memory area:

- holding register address of `%MLn` = `4096 + 4 * n` (0-based Modbus addresses),
- four registers per value, big-endian, **high word first**,
- `LINT` for integers, `LREAL` (IEEE 754 double) for levels and setpoints.

Doubles on the wire: no scaling, no rounding; the PLC compares exactly the tank levels SWMM computed. Source of truth: `plc/src/main.st` and `simulator/stormbench_sim/registers.py`; `simulator/tests/test_registers.py` checks that they agree.

## Simulator to PLC (one FC16 write per control step)

| Variable | IEC address | Holding registers | Type | Unit / values |
|---|---|---|---|---|
| `SimTimeS` | `%ML0` | 4096-4099 | LINT | seconds since simulation start |
| `CmdMode` | `%ML1` | 4100-4103 | LINT | 0 local (Base Case), 1 EFD as published, 2 EFD as intended, 3 EFD proposed by the central optimiser |
| `CenterOk` | `%ML2` | 4104-4107 | LINT | 1 coordination available, 0 link to the center lost |
| `DepthT1` ... `DepthT6` | `%ML3` ... `%ML8` | 4108-4131 | LREAL | tank water depth, m |
| `StepReq` | `%ML9` | 4132-4135 | LINT | lockstep request number |

The block is written in one request, `StepReq` last.

## Central optimiser to PLC (mode 3, one FC16 write per step)

Written by the edge gateway when the central optimiser answers over the UNS (Phase 2), or by the simulator when it plays the center (`--center emulated`).

| Variable | IEC address | Holding registers | Type | Unit / values |
|---|---|---|---|---|
| `CenterSetpointV2` ... `CenterSetpointV6` | `%ML10` ... `%ML13` | 4136-4151 | LREAL | proposed relative openings for stations 1..4 |
| `CenterStep` | `%ML14` | 4152-4155 | LINT | the `StepReq` this proposal belongs to |

In mode 3 the PLC evaluates the step only once `CenterStep = StepReq`, unless `CenterOk = 0` (center link lost: the stations run their Base Case settings at once). The stations accept a proposal only if every value lies in 0..1; otherwise they fall back and set status bit 3.

## PLC to simulator (one FC3 read per poll)

| Variable | IEC address | Holding registers | Type | Unit / values |
|---|---|---|---|---|
| `SetpointV2` | `%ML16` | 4160-4163 | LREAL | relative opening 0..1 of the T2 outlet |
| `SetpointV3` | `%ML17` | 4164-4167 | LREAL | T3 outlet |
| `SetpointV4` | `%ML18` | 4168-4171 | LREAL | T4 outlet |
| `SetpointV6` | `%ML19` | 4172-4175 | LREAL | T6 outlet |
| `ModeActive` | `%ML20` | 4176-4179 | LINT | mode actually applied (0 when stations run locally) |
| `Status` | `%ML21` | 4180-4183 | LINT | bit 0 fallback to local, bit 1 implausible level, bit 2 setpoint clamped, bit 3 implausible proposal from the center |
| `ScanCount` | `%ML22` | 4184-4187 | LINT | diagnostic, increments every scan |
| `StepAck` | `%ML23` | 4188-4191 | LINT | equals `StepReq` once the outputs belong to that request |

## Lockstep handshake

1. The simulator advances SWMM by one control step (5 min of model time).
2. It writes the input block with `StepReq = n` in one FC16 request.
3. The PLC notices `StepReq <> StepAck`, evaluates the inputs once, writes the outputs and sets `StepAck := StepReq` in the same scan.
4. The simulator polls the output block with FC3 until `StepAck = n` (timeout 10 s), applies the setpoints to the SWMM orifices and continues.

At start the simulator reads the current `StepAck` and numbers its requests from there, so a leftover acknowledgement from an earlier run never matches a new request.

## Why `StepReq` and `StepAck` are declared last

OpenPLC v4 applies Modbus writes and commits program outputs through a journal. Inputs are copied in at the start of a scan. Changed outputs are committed after the scan **one by one, in declaration order**, without holding the image lock; a Modbus read in between sees only the outputs committed so far (`core/src/plc_app/image_tables.cpp` and `journal_buffer.c` in openplc-runtime v4.2.4).

With `StepAck` declared first, 3 of 105,406 steps in a year-long run returned the new acknowledgement with the previous step's setpoints. With `StepAck` declared after every other output, a read that sees the new acknowledgement has seen every output committed before it. The Modbus plugin writes the input block in ascending address order, which makes `StepReq` safe on the input side for the same reason. `test_step_ack_is_the_last_located_variable` guards the declaration order.

## The same variables over OPC UA

The PLC also serves every located variable above read-only over OPC UA at `opc.tcp://<plc>:4840/openplc/opcua` (SecurityPolicy None, anonymous): node `ns=urn:stormbench:plc;s=PLC.main.<Name>`, LINT as Int64, LREAL as Double. Resolve the namespace index at run time (currently 2). See ADR 0009.
