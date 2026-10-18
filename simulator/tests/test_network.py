import re
from datetime import datetime

import pytest

from stormbench_sim.network import Controls, prepare_inp, read_layout

pytestmark = pytest.mark.model


def _options(text: str) -> dict[str, str]:
    block = re.search(r"(?ms)^\[OPTIONS\][^\n]*\n(.*?)^\[", text).group(1)
    return dict(line.split(None, 1) for line in block.splitlines() if line and not line.startswith(";"))


def _controls(text: str) -> str:
    return re.search(r"(?ms)^\[CONTROLS\][^\n]*\n(.*?)^\[", text).group(1).strip()


def test_prepare_points_rain_at_local_files(network, tmp_path):
    text = prepare_inp(network, tmp_path, Controls.BASE, "t").read_text(encoding="latin-1")
    files = re.findall(r'FILE\s+"([^"]+)"', re.search(r"(?ms)^\[TIMESERIES\].*?^\[", text).group(0))
    assert len(files) == 4
    assert all(network.model_dir.resolve().as_posix() in f for f in files)
    assert "C:\\scc" not in text.split("[BACKDROP]")[0]


def test_controls_variants(network, tmp_path):
    base = _controls(prepare_inp(network, tmp_path, Controls.BASE, "a").read_text(encoding="latin-1"))
    efd = _controls(prepare_inp(network, tmp_path, Controls.EFD_RULES, "b").read_text(encoding="latin-1"))
    external = _controls(prepare_inp(network, tmp_path, Controls.EXTERNAL, "c").read_text(encoding="latin-1"))
    assert base.startswith("RULE BC")
    assert efd.count("RULE ") == 9
    assert external == ""


def test_period_override_and_minimal_report(network, tmp_path):
    period = (datetime(2000, 5, 29, 0, 0), datetime(2000, 6, 1, 0, 0))
    text = prepare_inp(network, tmp_path, Controls.EXTERNAL, "p", period).read_text(encoding="latin-1")
    opts = _options(text)
    assert opts["START_DATE"].strip() == "05/29/2000"
    assert opts["END_DATE"].strip() == "06/01/2000"
    assert "NODES NONE" in text


def test_station_settings_match_the_model_files(network):
    """astlingen.toml must agree with the Base Case rule in the .inp and with EFD_Control.txt."""
    inp = network.inp_path.read_text(encoding="latin-1")
    efd = (network.model_dir / network.efd_rules).read_text(encoding="latin-1")
    for station in network.stations:
        assert re.search(
            rf"ORIFICE {station.valve} SETTING = {station.base_setting}\b", _controls(inp + "\n[")
        )
        assert re.search(rf"ORIFICE {station.valve} SETTING = {station.low_setting}\b", efd)
    assert f"NODE T2 DEPTH < {network.efd_threshold_m:g}" in efd


def test_layout_has_every_named_node(network):
    layout = read_layout(network)
    for node in (*network.tanks, *network.overflow_points, network.wwtp_outfall):
        assert node in layout.coords
    assert len(layout.links) == 23 + 6
