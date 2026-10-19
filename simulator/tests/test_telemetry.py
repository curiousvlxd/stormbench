import logging

from stormbench_sim.telemetry import export_logs


def test_logs_stay_local_without_victorialogs(monkeypatch):
    monkeypatch.delenv("VictoriaLogs__Url", raising=False)
    handlers = list(logging.getLogger().handlers)
    assert not export_logs()
    assert logging.getLogger().handlers == handlers
