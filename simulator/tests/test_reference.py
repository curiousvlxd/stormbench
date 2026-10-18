import json
import os

import numpy as np
import pytest
from conftest import GOLDEN_DIR, STORM

from stormbench_sim.control import Mode
from stormbench_sim.network import Controls
from stormbench_sim.runner import PythonController, RunSpec, run

pytestmark = pytest.mark.model
GOLDEN = GOLDEN_DIR / "astlingen_2000-05-29_3d.json"


@pytest.fixture(scope="module")
def storm_runs(network, tmp_path_factory):
    work = tmp_path_factory.mktemp("work")
    specs = {
        "bc": RunSpec("bc", network, Controls.BASE, period=STORM),
        "efd-swmm": RunSpec("efd-swmm", network, Controls.EFD_RULES, period=STORM),
        "efd-python": RunSpec(
            "efd-python",
            network,
            Controls.EXTERNAL,
            Mode.EFD_PUBLISHED,
            PythonController(network),
            period=STORM,
        ),
        "efd-intended": RunSpec(
            "efd-intended",
            network,
            Controls.EXTERNAL,
            Mode.EFD_INTENDED,
            PythonController(network),
            period=STORM,
        ),
        "bc-python": RunSpec(
            "bc-python", network, Controls.EXTERNAL, Mode.LOCAL, PythonController(network), period=STORM
        ),
    }
    return {name: run(spec, work, progress=None) for name, spec in specs.items()}


def test_python_efd_equals_swmm_rules(storm_runs):
    rules, python = storm_runs["efd-swmm"], storm_runs["efd-python"]
    assert np.array_equal(rules.setpoints, python.setpoints)
    assert np.allclose(rules.depths, python.depths, rtol=1e-9, atol=1e-9)
    assert python.totals["flood_m3"] == pytest.approx(rules.totals["flood_m3"], rel=1e-9)


def test_local_mode_equals_swmm_base_case(storm_runs):
    bc, python = storm_runs["bc"], storm_runs["bc-python"]
    assert np.array_equal(bc.setpoints, python.setpoints)
    assert python.totals["flood_m3"] == pytest.approx(bc.totals["flood_m3"], rel=1e-9)


def test_efd_reduces_overflow_in_this_storm(storm_runs):
    bc = storm_runs["bc"].totals["flood_total_m3"]
    assert storm_runs["efd-python"].totals["flood_total_m3"] < bc
    assert storm_runs["efd-intended"].totals["flood_total_m3"] < bc


def test_golden_values(storm_runs):
    current = {
        name: {p: round(v, 3) for p, v in r.totals["flood_m3"].items()} for name, r in storm_runs.items()
    }
    if os.environ.get("UPDATE_GOLDEN") or not GOLDEN.exists():
        GOLDEN.parent.mkdir(parents=True, exist_ok=True)
        GOLDEN.write_text(json.dumps(current, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        pytest.skip("golden values written")
    assert current == json.loads(GOLDEN.read_text(encoding="utf-8"))
