# 0005. SWMM 5.2.4 instead of 5.1.013, reference values and validation tolerance

Status: accepted, 2026-09-28

## Context

Sun et al. (2020) ran the Astlingen SWMM model with EPA SWMM 5.1.013 and report per-point overflow volumes for year 2000 (Tables 4 to 6). swmm-toolkit 0.17.0 bundles SWMM 5.2.4. The acceptance criterion was a PLC-vs-Python tolerance of 0.5 percentage points of total overflow volume.

## Decision

1. Compare with the paper using SWMM's own Node Flooding Summary from the `.rpt` file, which is what the paper tabulates. pyswmm's cumulative node statistics read through the API come out up to about 0.01% higher; they are used for per-interval volumes and event counts, not for the comparison with the paper.
2. Implement EFD twice, as two modes of the same ST program and Python reference:
   - **EFD as published**: `EFD_Control.txt` exactly as SWMM executes it. SWMM replaces a queued action only if the new rule has a strictly higher priority (`updateActionList` in `src/solver/controls.c`), and rules are evaluated in file order. Every `EFDO2Tx` rule always queues an action, so the `EFDO3Tx` rules (throttle the emptiest tank) and `EFDO1` (Base Case below 1 m) never take effect. This diagnosis was first published in SWMM-Astlingen pull request #1 (2025-11-29, https://github.com/open-toolbox/SWMM-Astlingen/pull/1).
   - **EFD as intended**: the rule priorities fixed as in that pull request.
3. PLC vs Python: no tolerance. Setpoint series, level series and volumes must be identical, and every step is cross-checked by the shadow reference.

## Results (year 2000)

| | Paper, SWMM 5.1.013 | This testbed, SWMM 5.2.4 |
|---|---:|---:|
| Base Case | 264,731 m³ | 264,724 m³ |
| EFD (SWMM rules) | 247,788 m³ (-6.4%) | 247,787 m³ (-6.40%) |
| EFD on the PLC | | 247,787 m³ (-6.40%) |
| EFD as intended | | 247,537 m³ (-6.49%) |

Per point the differences to the paper are a few m³. The paper's CSO10 value under EFD (4,751) is a typo for 4,741: CSO10 is upstream of every controlled outlet, and the paper's EFD total only adds up with 4,741.

## Consequences

- The SWMM version change is measured, not assumed: its effect on the benchmark is below 0.01%.
- A 0.5 pp tolerance would have hidden logic errors: the whole EFD effect is 6.4 pp. Exact equality caught a real integration issue (ADR 0003).
- Exact equality holds on one platform. The swmm-toolkit wheels for Windows and Linux are built with different compilers, and the same run differs between them by at most 3e-14 m in tank level and 3e-10 m³ in overflow volume, with identical control decisions. Golden values are therefore rounded to 0.001 m³, and bit-for-bit comparisons are made between runs on the same machine.
- The paper's Simba# figure (-8.3%) is not reproduced by either EFD mode. Dropping the 1 m threshold as well gives -8.30% (242,765 m³), but the per-point pattern differs from Simba#; we do not claim to explain the gap between the two simulators.
