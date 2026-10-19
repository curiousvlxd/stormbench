# 0001. EPA SWMM via pyswmm for the hydraulic model

Status: accepted, 2026-09-28

## Context

The testbed needs a hydrodynamic sewer model that can be paused every control interval, read and given new throttle settings, and that runs a published RTC benchmark. The Astlingen benchmark exists as an EPA SWMM model with published results (Sun et al. 2020, Water 12(4), 1034).

## Decision

Use EPA SWMM 5.2.4 through pyswmm 2.1.0 and swmm-toolkit 0.17.0, pinned exactly (`pyswmm[swmm5.2.4]` would resolve an older toolkit).

- The simulation advances one control interval per stride (`step_advance(300)`). Between strides the runner reads tank depths and cumulative node flooding and writes orifice `target_setting`.
- The benchmark files are downloaded from the original repository at a pinned commit and checked by SHA-256. They are not redistributed: the repository has no licence.
- In a working copy, the simulator rewrites the model's absolute rain file paths and switches control rules (Base Case, SWMM EFD rules, or none for external control).

## Consequences

- One simulated year takes about 30-40 s on a laptop including the per-step reads, so the PLC handshake, not the hydraulics, dominates run time (ADR 0003).
- `swmm_start` re-initialises link settings, so the runner starts the simulation explicitly before applying the initial Base Case settings. Otherwise the first five minutes ran with fully open throttles.
- SWMM's rule engine and settings written through the API give the same settings at the same moments. Volumes agree to about 1e-12 relative (floating point noise), so rule-vs-API comparisons use a 1e-9 tolerance; PLC-vs-Python comparisons share the API path and are exact.

## Alternatives considered

- pystorms: a control sandbox on pyswmm with Astlingen as scenario "zeta", but only one storm (October 2005) and GPL-3.0. Kept as an optional comparison, not a dependency.
- Commercial simulators (SIMBA#, HYSTEM-EXTRAN, MIKE+, InfoWorks ICM): not reproducible for outsiders.
