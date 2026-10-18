import json
import os

import numpy as np
import pytest
from conftest import GOLDEN_DIR, STORM

from stormbench_sim.control import Mode
from stormbench_sim.network import Controls
from stormbench_sim.plc import PlcController, PlcLink
from stormbench_sim.runner import RunSpec, run

pytestmark = [
    pytest.mark.uns,
    pytest.mark.model,
    pytest.mark.skipif(not os.environ.get("UNS_PLC_HOST"), reason="no Phase 2 stack: set UNS_PLC_HOST"),
]


def test_storm_through_the_uns_equals_phase_1(network, tmp_path):
    link = PlcLink(os.environ["UNS_PLC_HOST"], int(os.environ.get("UNS_PLC_PORT", "502")), timeout_s=60)
    link.connect(attempts=10)
    try:
        controller = PlcController(link, network, shadow=True, emulate_center=False)
        result = run(
            RunSpec("uns", network, Controls.EXTERNAL, Mode.EFD_CENTER, controller, period=STORM),
            tmp_path,
            progress=None,
        )
    finally:
        link.close()

    assert controller.shadow.mismatches == 0, controller.shadow.first
    assert np.all(result.mode_active[1:] == Mode.EFD_CENTER)
    golden = json.loads((GOLDEN_DIR / "astlingen_2000-05-29_3d.json").read_text(encoding="utf-8"))[
        "efd-python"
    ]
    assert {p: round(v, 3) for p, v in result.totals["flood_m3"].items()} == golden
