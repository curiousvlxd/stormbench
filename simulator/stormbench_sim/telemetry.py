"""Ships the simulator's logs to VictoriaLogs over OpenTelemetry when VictoriaLogs__Url is set."""

from __future__ import annotations

import logging
import os


def export_logs(service: str = "simulator") -> bool:
    url = os.environ.get("VictoriaLogs__Url")  # noqa: SIM112
    if not url:
        return False
    from opentelemetry._logs import set_logger_provider
    from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter
    from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
    from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
    from opentelemetry.sdk.resources import Resource

    provider = LoggerProvider(resource=Resource.create({"service.name": service}))
    endpoint = url.rstrip("/") + "/insert/opentelemetry/v1/logs"
    provider.add_log_record_processor(BatchLogRecordProcessor(OTLPLogExporter(endpoint=endpoint)))
    set_logger_provider(provider)
    logging.getLogger().addHandler(LoggingHandler(level=logging.INFO, logger_provider=provider))
    return True
