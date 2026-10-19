# Models

## Astlingen (SWMM implementation)

The testbed uses the SWMM implementation of the Astlingen real-time control benchmark:

- Repository: https://github.com/open-toolbox/SWMM-Astlingen, pinned to commit `e226b03` (2020-01-08).
- Developed by Congcong Sun (IRI, CSIC-UPC) under the supervision of Manfred Schütze (ifak), Morten Borup and Luca Vezzaro (DTU).
- Reference publication: Sun, C.; Svensen, J.L.; Borup, M.; Puig, V.; Cembrano, G.; Vezzaro, L. "An MPC-Enabled SWMM Implementation of the Astlingen RTC Benchmarking Network." Water 2020, 12(4), 1034. https://doi.org/10.3390/w12041034
- Original benchmark: Schütze, M. et al. "Astlingen: a benchmark for real time control (RTC)." Water Science & Technology, 2017.

The repository has no licence file, and its description asks users to cite the authors when the model is used or disseminated. The files are therefore **not** redistributed: `fetch_astlingen.py` downloads them from the pinned commit and verifies their SHA-256 hashes. `docker compose up` runs the same script.

```bash
python models/fetch_astlingen.py          # into models/astlingen/ (gitignored)
```

### Files

| File | Content |
|---|---|
| `Astlingen_SWMM.inp` | SWMM 5 model, Base Case control rule, year 2000 simulation period |
| `EFD_Control.txt` | Equal Filling Degree control as SWMM control rules |
| `1..4Astlingen_Erft*.txt` | Rain series for 4 gauges, 5 min, 2000-01-01 to 2009-12-31 (29 February is absent in all leap years) |

From the `.inp` (see also `docs/adr/0005-swmm-version-and-validation.md`):

- 6 storage tanks T1 to T6 with constant area and 5 m depth, 5,900 m³ in total.
- 10 overflow points: overflow from the 6 tanks and from 4 CSO structures without storage (CSO7 to CSO10), modelled as node flooding.
- 6 tank outlet orifices; V2, V3, V4 and V6 are controlled, V1 and V5 are fixed.
- The `[TIMESERIES]` section points to the rain files with an absolute path on the authors' machine. The simulator rewrites these paths at run time; the downloaded files stay unmodified.
