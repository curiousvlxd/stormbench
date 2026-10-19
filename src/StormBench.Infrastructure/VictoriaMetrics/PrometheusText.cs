using System.Globalization;
using System.Text;

namespace StormBench.Infrastructure.VictoriaMetrics;

/// <summary>One value of one series: Series is the metric name with its labels, as Prometheus writes it.</summary>
public readonly record struct Sample(string Series, double Value, long TimestampMs);

/// <summary>
/// The Prometheus text exposition format with timestamps, as VictoriaMetrics imports it on
/// <c>/api/v1/import/prometheus</c>: one line per sample, <c>name{label="value"} value timestamp_ms</c>.
/// https://prometheus.io/docs/instrumenting/exposition_formats/#text-based-format
/// </summary>
public static class PrometheusText
{
    /// <summary>
    /// A series name. Labels in the given order; values escaped (backslash, double quote, line
    /// feed). Names are the historian's own constants and are not checked.
    /// </summary>
    public static string Series(string metric, params ReadOnlySpan<(string Name, string Value)> labels)
    {
        if (labels.IsEmpty)
        {
            return metric;
        }
        var text = new StringBuilder(metric).Append('{');
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }
            text.Append(labels[i].Name).Append("=\"");
            AppendEscaped(text, labels[i].Value);
            text.Append('"');
        }
        return text.Append('}').ToString();
    }

    /// <summary>The lines of the samples; values that are not finite are left out.</summary>
    public static string Format(IEnumerable<Sample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var text = new StringBuilder();
        foreach (var sample in samples)
        {
            if (!double.IsFinite(sample.Value))
            {
                continue;
            }
            text.Append(sample.Series)
                .Append(' ')
                .Append(sample.Value.ToString("R", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(sample.TimestampMs.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
        return text.ToString();
    }

    private static void AppendEscaped(StringBuilder text, string value)
    {
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => text.Append(@"\\"),
                '"' => text.Append("\\\""),
                '\n' => text.Append(@"\n"),
                _ => text.Append(c)
            };
        }
    }
}
