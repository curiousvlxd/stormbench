import threading
import time
from datetime import datetime, timedelta

import numpy as np
import pytest

from stormbench_sim.control import Mode
from stormbench_sim.network import Controls
from stormbench_sim.runner import PythonController, RunSpec, run
from stormbench_sim.service import control_steps

pytestmark = pytest.mark.model

WINDOW = (datetime(2000, 5, 30, 4, 0), datetime(2000, 5, 30, 6, 52))


def test_observer_sees_every_row_as_recorded(network, tmp_path):
    records = []
    spec = RunSpec(
        "observed",
        network,
        Controls.EXTERNAL,
        Mode.EFD_PUBLISHED,
        PythonController(network),
        period=WINDOW,
        observer=records.append,
    )
    result = run(spec, tmp_path, progress=None)
    assert len(records) == result.meta["steps"] == control_steps(*WINDOW, network.control_step_s) == 34
    assert "stopped" not in result.meta
    for k, rec in enumerate(records):
        assert rec.k == k
        assert rec.time == WINDOW[0] + timedelta(seconds=int(result.times_s[k]))
        assert rec.depths == tuple(result.depths[k])
        assert rec.flood_cum == tuple(result.flood_cum[k])
        assert rec.wwtp_cum == result.wwtp_cum[k]
        assert rec.rain_mmph == tuple(result.rain_mmph[k])
        assert rec.outputs.setpoints == tuple(result.setpoints[k])
        assert rec.outputs.mode_active == result.mode_active[k]
        assert rec.outputs.status == result.status[k]
        assert rec.mismatches == 0
    assert np.asarray([r.rain_mmph for r in records]).max() > 0


def test_stop_cuts_the_pace_wait_and_ends_the_run_after_the_current_step(network, tmp_path):
    stop = threading.Event()

    def observer(rec):
        if rec.k == 0:
            threading.Timer(0.2, stop.set).start()

    spec = RunSpec(
        "stopped", network, Controls.BASE, period=WINDOW, pace_s=30.0, observer=observer, stop=stop
    )
    t0 = time.perf_counter()
    result = run(spec, tmp_path, progress=None)
    assert time.perf_counter() - t0 < 20
    assert result.meta["steps"] == 1
    assert result.meta["stopped"] is True
    assert result.totals["flood_total_m3"] == pytest.approx(result.flood_cum[-1].sum())
