from __future__ import annotations

import time
from dataclasses import dataclass, field
from enum import IntEnum

from . import sparkplug_b_pb2 as pb

_UINT64 = 1 << 64

Value = int | float | bool | str | None


class DataType(IntEnum):
    """Sparkplug B 3.0 data types used here (values as in sparkplug_b.proto)."""

    INT64 = 4
    UINT64 = 8
    DOUBLE = 10
    BOOLEAN = 11
    STRING = 12


@dataclass(frozen=True)
class Metric:
    name: str
    datatype: int
    value: Value


@dataclass
class Payload:
    timestamp: int
    seq: int | None
    metrics: list[Metric] = field(default_factory=list)

    def find(self, name: str) -> Metric | None:
        return next((m for m in self.metrics if m.name == name), None)


def now_ms() -> int:
    return time.time_ns() // 1_000_000


def encode(payload: Payload) -> bytes:
    message = pb.Payload(timestamp=payload.timestamp)
    if payload.seq is not None:
        message.seq = payload.seq
    for metric in payload.metrics:
        out = message.metrics.add(
            name=metric.name, datatype=int(metric.datatype), timestamp=payload.timestamp
        )
        value = metric.value
        if value is None:
            out.is_null = True
        elif metric.datatype == DataType.INT64:
            out.long_value = int(value) % _UINT64
        elif metric.datatype == DataType.UINT64:
            out.long_value = int(value)
        elif metric.datatype == DataType.DOUBLE:
            out.double_value = float(value)
        elif metric.datatype == DataType.BOOLEAN:
            out.boolean_value = bool(value)
        elif metric.datatype == DataType.STRING:
            out.string_value = str(value)
        else:
            raise ValueError(f"unsupported datatype {metric.datatype} for {metric.name}")
    return message.SerializeToString()


def decode(data: bytes) -> Payload:
    """Raises google.protobuf.message.DecodeError for bytes that are not a Sparkplug B payload."""
    message = pb.Payload.FromString(data)
    metrics = [Metric(m.name, _datatype(m.datatype), _value(m)) for m in message.metrics]
    return Payload(message.timestamp, message.seq if message.HasField("seq") else None, metrics)


def _datatype(value: int) -> int:
    try:
        return DataType(value)
    except ValueError:
        return value


def _value(metric: pb.Payload.Metric) -> Value:
    if metric.is_null:
        return None
    kind = metric.WhichOneof("value")
    if kind == "long_value":
        value = metric.long_value
        if metric.datatype == DataType.INT64 and value >= _UINT64 // 2:
            return value - _UINT64
        return value
    if kind in ("int_value", "float_value", "double_value", "boolean_value", "string_value"):
        return getattr(metric, kind)
    return None
