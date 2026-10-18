# 0011. The PLC does not wait for a silent center

Status: accepted, 2026-10-07

## Context

In mode 3 the PLC waits, step by step, for the center's proposal (ADR 0007): this keeps every run deterministic. The link to the center was taken as up unless a run declared an outage window. When the center really fell silent (broker, edge gateway or Scada down), the PLC waited until the simulator gave up after 60 s and the run failed. A real station would not wait: it falls back to local control.

## Decision

- **Deadline per step.** The simulator gives the center 2 s per step. Without an acknowledgement by then it sends the same step again with CenterOk = 0; the PLC's stations run locally (status bit 1, alarm `PLC01.Fallback`) and the run goes on.
- **No waiting while the center is gone.** After a miss, the following steps go to the PLC with CenterOk = 0 at once; every 10 s of wall time one step asks the center again, and the first answer brings mode 3 back.
- **Cut on purpose.** The test console can cut the link (`Fault/CenterLinkCut`, NCMD to Sim01, button on the operator screen) and restore it. Sim01 reports `Center/LinkUp`.
- **The check stays exact.** The shadow check compares each PLC answer with the reference computed from the inputs the PLC actually took; when the center answers in the very moment the step is sent again, the PLC's status word tells which inputs it used.

## Consequences

- Measured on the Aspire stack, storm of 30 May in mode 3: with the broker container stopped for about 10 s the PLC kept acknowledging steps on local control, went back to mode 3 after the broker returned, and the run finished with 0 of 431 answers differing from the reference. Cutting the link from the console gives the same picture.
- In normal operation nothing changes: the center answers in milliseconds, the deadline never fires, and the golden volumes are unchanged.
- Which steps run locally during a real outage depends on timing, so such runs are not reproducible bit for bit; runs without faults still are.
