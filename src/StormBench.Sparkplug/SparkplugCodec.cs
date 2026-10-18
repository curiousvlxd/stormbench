using Google.Protobuf;
using Org.Eclipse.Tahu.Protobuf;

namespace StormBench.Sparkplug;

/// <summary>Encodes and decodes Sparkplug B payloads with the official protobuf schema.</summary>
public static class SparkplugCodec
{
    public static byte[] Encode(SparkplugPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var message = new Payload { Timestamp = payload.Timestamp };
        if (payload.Seq is { } seq)
        {
            message.Seq = seq;
        }
        foreach (var metric in payload.Metrics)
        {
            var encoded = new Payload.Types.Metric
            {
                Name = metric.Name,
                Datatype = (uint)metric.DataType,
                Timestamp = metric.Timestamp ?? payload.Timestamp,
            };
            switch (metric.Value)
            {
                case null:
                    encoded.IsNull = true;
                    break;
                case long v:
                    encoded.LongValue = unchecked((ulong)v);
                    break;
                case ulong v:
                    encoded.LongValue = v;
                    break;
                case double v:
                    encoded.DoubleValue = v;
                    break;
                case bool v:
                    encoded.BooleanValue = v;
                    break;
                case string v:
                    encoded.StringValue = v;
                    break;
                default:
                    throw new ArgumentException($"unsupported value type {metric.Value.GetType()} for {metric.Name}");
            }
            message.Metrics.Add(encoded);
        }
        return message.ToByteArray();
    }

    public static SparkplugPayload Decode(ReadOnlySpan<byte> bytes)
    {
        var message = Payload.Parser.ParseFrom(bytes);
        var metrics = new List<SparkplugMetric>(message.Metrics.Count);
        foreach (var metric in message.Metrics)
        {
            var type = (SparkplugDataType)metric.Datatype;
            object? value = metric.IsNull
                ? null
                : metric.ValueCase switch
                {
                    Payload.Types.Metric.ValueOneofCase.LongValue when type is SparkplugDataType.Int64 =>
                        unchecked((long)metric.LongValue),
                    Payload.Types.Metric.ValueOneofCase.LongValue => metric.LongValue,
                    Payload.Types.Metric.ValueOneofCase.DoubleValue => metric.DoubleValue,
                    Payload.Types.Metric.ValueOneofCase.BooleanValue => metric.BooleanValue,
                    Payload.Types.Metric.ValueOneofCase.StringValue => metric.StringValue,
                    Payload.Types.Metric.ValueOneofCase.IntValue => (long)metric.IntValue,
                    Payload.Types.Metric.ValueOneofCase.FloatValue => (double)metric.FloatValue,
                    _ => null,
                };
            metrics.Add(new SparkplugMetric(metric.Name, type, value, metric.HasTimestamp ? metric.Timestamp : null));
        }
        return new SparkplugPayload(message.Timestamp, message.HasSeq ? message.Seq : null, metrics);
    }
}
