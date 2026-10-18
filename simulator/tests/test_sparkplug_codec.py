import pytest

from stormbench_sim.sparkplug import DataType, Metric, Payload, decode, encode
from stormbench_sim.sparkplug import sparkplug_b_pb2 as pb


def test_every_supported_type_round_trips():
    payload = Payload(
        1_700_000_000_123,
        7,
        [
            Metric("Scenario/Steps", DataType.INT64, 105_406),
            Metric("Counter", DataType.UINT64, 2**64 - 1),
            Metric("Overflow/Total", DataType.DOUBLE, 12069.953650939851),
            Metric("Overflow/T1/Active", DataType.BOOLEAN, True),
            Metric("Scenario/State", DataType.STRING, "running"),
        ],
    )
    assert decode(encode(payload)) == payload


@pytest.mark.parametrize("value", [-1, -300, -(2**63), 2**63 - 1])
def test_int64_travels_as_twos_complement(value):
    data = encode(Payload(0, 0, [Metric("x", DataType.INT64, value)]))
    assert pb.Payload.FromString(data).metrics[0].long_value == value % 2**64
    assert decode(data).metrics[0].value == value


def test_ndeath_has_no_seq():
    data = encode(Payload(1, None, [Metric("bdSeq", DataType.INT64, 3)]))
    assert not pb.Payload.FromString(data).HasField("seq")
    death = decode(data)
    assert death.seq is None
    assert death.find("bdSeq") == Metric("bdSeq", DataType.INT64, 3)


def test_seq_zero_is_present():
    data = encode(Payload(1, 0, []))
    assert pb.Payload.FromString(data).HasField("seq")
    assert decode(data).seq == 0


def test_metrics_carry_datatype_and_the_payload_timestamp():
    metric = pb.Payload.FromString(encode(Payload(42, 1, [Metric("x", DataType.DOUBLE, 1.5)]))).metrics[0]
    assert (metric.datatype, metric.timestamp, metric.double_value) == (10, 42, 1.5)


def test_null_value_round_trips():
    payload = Payload(1, 2, [Metric("x", DataType.DOUBLE, None)])
    assert decode(encode(payload)) == payload


def test_unsupported_datatype_is_rejected():
    with pytest.raises(ValueError):
        encode(Payload(0, 0, [Metric("x", 9, 1.0)]))


def test_foreign_types_decode_to_plain_values():
    message = pb.Payload(timestamp=5)
    message.metrics.add(name="f", datatype=9, float_value=0.5)
    message.metrics.add(name="i", datatype=3, int_value=7)
    decoded = decode(message.SerializeToString())
    assert decoded.metrics == [Metric("f", 9, 0.5), Metric("i", 3, 7)]
