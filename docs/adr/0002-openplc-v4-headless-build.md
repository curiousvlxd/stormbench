# 0002. OpenPLC Runtime v4 with the ST program compiled at image build time

Status: accepted, 2026-09-28

## Context

The control algorithm must run as real IEC 61131-3 Structured Text on a PLC runtime, and the whole loop must start with one `docker compose up`, without a graphical editor.

OpenPLC Runtime v4 (MIT) does not compile ST itself. The OpenPLC Editor compiles ST to C++ with STruC++ and uploads a `program.zip`; the runtime compiles that C++ into a shared library. OpenPLC v3 accepted ST directly but reached end of life and was archived on 2026-04-04.

## Decision

- Base image `ghcr.io/autonomy-logic/openplc-runtime:v4.2.4`, pinned by digest.
- A multi-stage Dockerfile compiles `plc/src/*.st` with STruC++ 0.6.9 (the version the Editor 4.3.1 pins), with the Editor's options and file layout, then runs the runtime's own compile and plugin activation steps. The container boots straight into RUNNING with the Modbus TCP server on port 502.
- Task interval `T#1ms`. One lockstep handshake costs one scan: at the Editor default `T#20ms` a year of 105,406 control steps takes about 35 minutes, at 1 ms about 2 minutes.
- The REST API on port 8443 is not published: a fresh runtime lets anyone create the first administrator and upload code that runs as root.

## Consequences

- No user accounts, no REST upload, no GUI in the default path. The official Editor CLI (`openplc-cli` 4.3.1) produced byte-identical C++ in the Phase 0 spike and remains the reference if the runtime's internal file contract changes.
- The build depends on the Editor-to-runtime file contract, which changes quickly (runtime 4.2.0 to 4.2.4 in three weeks). Runtime and STruC++ versions are pinned together.
- STruC++ is GPL-3.0 with a Runtime Library Exception. It is a build tool only; the generated code and runtime headers may be distributed under any licence.
- STruC++ 0.6.9 generates `NOT(int)` for `NOT (a AND b)` with two comparisons, which does not compile. `main.st` assigns the AND to a BOOL first.

## Alternatives considered

- OpenPLC v3: works and accepts `.st`, same throughput, but unmaintained.
- CODESYS Control SL: programs are built with a Windows IDE, the demo stops after two hours, and the Linux SL runtime is not released for containers. Planned later as a second target for the same ST code, not as the default.
- Beremiz: viable plan B, but no official image and one to two days of setup.
- 4diac FORTE (IEC 61499) and RuSTy (compiler only): wrong standard or no runtime.
