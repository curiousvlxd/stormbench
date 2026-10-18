import json
import os

import numpy as np
import pytest
from conftest import GOLDEN_DIR, STORM

from stormbench_sim.control import ControlInputs, Mode, decide
from stormbench_sim.network import Controls
from stormbench_sim.plc import PlcController, PlcLink
from stormbench_sim.runner import PythonController, RunSpec, run

pytestmark = pytest.mark.plc


@pytest.fixture(scope="module")
def link():
    plc = PlcLink(os.environ["PLC_HOST"], int(os.environ.get("PLC_PORT", "502")))
    plc.connect(attempts=5)
    yield plc
    plc.close()


def test_edge_cases_match_reference(link, network):
    cases = [
        (Mode.EFD_PUBLISHED, True, (0.0,) * 6),
        (Mode.EFD_PUBLISHED, True, (1.0, 2.0, 1.5, 0.3, 0.0, 4.9)),
        (Mode.EFD_INTENDED, True, (1.0, 2.0, 1.5, 0.3, 0.0, 4.9)),
        (Mode.EFD_INTENDED, True, (0.0, 0.5, 0.9, 0.2, 0.0, 0.1)),
        (Mode.EFD_INTENDED, True, (0.0, 2.0, 2.0, 1.0, 0.0, 0.5)),
        (Mode.EFD_PUBLISHED, False, (1.0, 2.0, 1.5, 0.3, 0.0, 4.9)),
        (Mode.EFD_PUBLISHED, True, (1.0, float("nan"), 1.5, 0.3, 0.0, 4.9)),
        (Mode.EFD_PUBLISHED, True, (1.0, 5.2, 1.5, 0.3, 0.0, 4.9)),
        (Mode.LOCAL, True, (1.0, 2.0, 1.5, 0.3, 0.0, 4.9)),
    ]
    for k, (mode, center_ok, depths) in enumerate(cases):
        inputs = ControlInputs(300 * k, mode, center_ok, depths)
        assert link.exchange(inputs)[0] == decide(inputs, network), inputs


def test_center_proposals_are_checked_by_the_stations(link, network):
    levels = (1.0, 2.0, 1.5, 0.3, 0.0, 4.9)
    proposals = [
        (0.2366, 0.6508, 0.3523, 1.0),
        (0.5, 0.5, 0.5, 0.5),
        (float("nan"), 0.5, 0.5, 0.5),
        (1.3, 0.5, 0.5, 0.5),
        (0.5, -0.1, 0.5, 0.5),
    ]
    for k, proposal in enumerate(proposals):
        for center_ok in (True, False):
            inputs = ControlInputs(300 * k, Mode.EFD_CENTER, center_ok, levels)
            expected = decide(inputs, network, proposal)
            assert link.exchange(inputs, proposal)[0] == expected, (inputs, proposal)


@pytest.mark.model
@pytest.mark.parametrize("mode", [Mode.EFD_PUBLISHED, Mode.EFD_INTENDED, Mode.EFD_CENTER])
def test_storm_plc_equals_python(link, network, tmp_path, mode):
    controller = PlcController(link, network, shadow=True, emulate_center=True)
    plc = run(
        RunSpec("plc", network, Controls.EXTERNAL, mode, controller, period=STORM), tmp_path, progress=None
    )
    reference_mode = Mode.EFD_PUBLISHED if mode == Mode.EFD_CENTER else mode
    python = run(
        RunSpec("py", network, Controls.EXTERNAL, reference_mode, PythonController(network), period=STORM),
        tmp_path,
        progress=None,
    )
    assert controller.shadow.mismatches == 0, controller.shadow.first
    assert np.array_equal(plc.setpoints, python.setpoints)
    assert plc.totals["flood_m3"] == python.totals["flood_m3"]
    golden = GOLDEN_DIR / "astlingen_2000-05-29_3d.json"
    if golden.exists():
        key = "efd-intended" if mode == Mode.EFD_INTENDED else "efd-python"
        expected = json.loads(golden.read_text(encoding="utf-8"))[key]
        assert {p: round(v, 3) for p, v in plc.totals["flood_m3"].items()} == expected


@pytest.mark.model
def test_storm_plc_with_the_mpc_center_equals_python(link, network, tmp_path):
    from stormbench_sim.mpc import MpcCenter
    from stormbench_sim.runner import CenterController

    center = MpcCenter.for_run(network, tmp_path, STORM)
    controller = PlcController(link, network, shadow=True, center=center)
    plc = run(
        RunSpec("plc-mpc", network, Controls.EXTERNAL, Mode.EFD_CENTER, controller, period=STORM),
        tmp_path,
        progress=None,
    )
    offline = CenterController(MpcCenter.for_run(network, tmp_path, STORM), network)
    python = run(
        RunSpec("py-mpc", network, Controls.EXTERNAL, Mode.EFD_CENTER, offline, period=STORM),
        tmp_path,
        progress=None,
    )
    assert controller.shadow.mismatches == 0, controller.shadow.first
    assert np.array_equal(plc.setpoints, python.setpoints)
    assert plc.totals["flood_m3"] == python.totals["flood_m3"]
