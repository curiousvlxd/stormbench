# 0008. A small own Sparkplug B 3.0 library instead of SparkplugNet

Status: accepted, 2026-09-28

## Context

The Phase 2 criterion is about exactly the parts of Sparkplug B 3.0 that are easy to get subtly wrong: bdSeq in the will and the birth, sequence numbers, rebirth, the Primary Host STATE handshake, and marking data stale. SparkplugNet (MIT, 1.4.1 of 2026-08-18, one maintainer) is the established .NET library. Reviewing its 1.4.1 source and running it against Mosquitto 2.1.2 showed deviations from 3.0 in these areas: its rebirth publishes an NDEATH and a non-zero seq, it subscribes to the pre-3.0 STATE topic and does not wait for the primary host before the birth, its host uses different timestamps for birth and will, and the host side does not reject stale NDEATHs or detect sequence gaps. Fixes for the STATE topic and for waiting on the host have been open as pull requests since 2024.

## Decision

`StormBench.Sparkplug`: about 900 lines on MQTTnet 5.2 and Google.Protobuf, using the unmodified `sparkplug_b.proto` from Eclipse Tahu (EPL-2.0, kept as a separately licensed file).

- Edge node: one MQTT session per bdSeq (Int64 metric, as in the specification and Tahu), NDEATH registered as the will at connect, births only after the primary host's STATE is online, seq 0 on NBIRTH and +1 mod 256 on every following message, rebirth on NCMD with the same bdSeq, NDEATH and a new session when the host goes offline or the connection drops, report by exception, DDEATH when the PLC becomes unreachable.
- Primary host: subscribes before announcing itself, STATE online and will offline with the same timestamp, ordered processing of all group messages, rebirth requests, and a callback for a lost connection.
- MQTT 5 without PUBLISH properties on births: Mosquitto 2.1.2's Sparkplug Aware plugin crashes the broker on a second NBIRTH that carries properties (double free; found during the stack check, not yet reported upstream). The rebirth test publishes two births through the plugin and guards this.
- MQTTnet 5 reports a refused connection in the connect result instead of throwing; both clients check it.

## Consequences

- The behaviour the criterion depends on is ours to test: eight session tests against a real broker (births wait for the host, sequence, will with matching bdSeq, next session with the next bdSeq, rebirth, DCMD delivery, host offline) plus the broker-loss tests in Scada.
- The generated types also decode SparkplugNet's messages, so interoperability is at the wire level; a TCK run (Eclipse Sparkplug TCK) is the next step before calling it compliant.
- Maintenance is ours. The library is deliberately small and only supports the data types the testbed uses (Int64, UInt64, Double, Boolean, String); templates, datasets and aliases are out of scope.
