from __future__ import annotations

import os
import time
from datetime import datetime
from pathlib import Path

import pytest

from stormbench_sim.network import Network, load_network

REPO = Path(__file__).resolve().parents[2]
MODEL_DIR = Path(os.environ.get("STORMBENCH_MODEL_DIR", REPO / "models" / "astlingen"))
GOLDEN_DIR = Path(__file__).parent / "golden"

STORM = (datetime(2000, 5, 29, 0, 0), datetime(2000, 6, 1, 0, 0))


def model_available() -> bool:
    return (MODEL_DIR / "Astlingen_SWMM.inp").exists()


def wait_until(condition, timeout_s: float = 2.0) -> None:
    deadline = time.monotonic() + timeout_s
    while not condition():
        assert time.monotonic() < deadline, "timed out"
        time.sleep(0.005)


@pytest.fixture(scope="session")
def network() -> Network:
    return load_network("astlingen", MODEL_DIR)


def pytest_collection_modifyitems(config: pytest.Config, items: list[pytest.Item]) -> None:
    skip_model = pytest.mark.skip(reason="Astlingen model missing: python models/fetch_astlingen.py")
    skip_plc = pytest.mark.skip(reason="no PLC: set PLC_HOST (and PLC_PORT) to a running testbed PLC")
    for item in items:
        if "model" in item.keywords and not model_available():
            item.add_marker(skip_model)
        if "plc" in item.keywords and not os.environ.get("PLC_HOST"):
            item.add_marker(skip_plc)
