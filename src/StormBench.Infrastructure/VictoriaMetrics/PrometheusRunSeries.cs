using StormBench.Application.Historian;
using StormBench.Domain.Runs;

namespace StormBench.Infrastructure.VictoriaMetrics;

/// <summary>
/// Metric names the historian writes to VictoriaMetrics (ADR 0015). Every series carries the
/// labels run, storm and algorithm; per tank, outlet or overflow point also tank, valve or point.
/// Volumes are cumulative since the start of the run.
/// </summary>
public static class HistorianMetrics
{
    public const string Step = "stormbench_step";
    public const string Rain = "stormbench_rain_intensity_mm_per_hour";
    public const string OverflowTotal = "stormbench_overflow_total_cubic_meters";
    public const string BaseCaseTotal = "stormbench_basecase_overflow_total_cubic_meters";
    public const string Overflow = "stormbench_overflow_volume_cubic_meters";
    public const string WwtpInflow = "stormbench_wwtp_inflow_cubic_meters";
    public const string TankLevel = "stormbench_tank_level_meters";
    public const string Setpoint = "stormbench_valve_setpoint_ratio";
    public const string Mode = "stormbench_plc_mode";
    public const string Mismatches = "stormbench_shadow_mismatches";
    public const string AlarmsActive = "stormbench_alarms_active";
    public const string AlarmsUnacknowledged = "stormbench_alarms_unacknowledged";
}

/// <summary>Receives the samples of a control step. Must not block: it is called on the UNS host's path.</summary>
public interface ISampleSink
{
    void Write(IReadOnlyList<Sample> samples);
}

/// <summary>
/// The steps of a run as Prometheus samples: one series per value, named once per run with the
/// run's labels, every sample at the simulated time of its step.
/// </summary>
public sealed class PrometheusRunSeries(ISampleSink sink) : IRunSeriesWriter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Metric, string? Label, string? Value), string> _names = [];
    private RunId? _run;
    private (string Name, string Value)[] _labels = [];

    public void Write(RunStepSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        List<Sample> samples = [];
        lock (_gate)
        {
            if (_run != sample.Run.Id)
            {
                _run = sample.Run.Id;
                _labels = [("run", sample.Run.Id.Value), ("storm", sample.Run.Storm.Value), ("algorithm", sample.Run.Algorithm.Value)];
                _names.Clear();
            }
            void Add(string metric, double? value, string? label = null, string? labelValue = null)
            {
                if (value is { } v)
                {
                    samples.Add(new Sample(Name(metric, label, labelValue), v, sample.TimestampMs));
                }
            }

            Add(HistorianMetrics.Step, sample.Step);
            Add(HistorianMetrics.Rain, sample.Rain);
            Add(HistorianMetrics.OverflowTotal, sample.OverflowTotal?.CubicMeters);
            Add(HistorianMetrics.BaseCaseTotal, sample.BaseCaseTotal?.CubicMeters);
            Add(HistorianMetrics.WwtpInflow, sample.WwtpInflow?.CubicMeters);
            Add(HistorianMetrics.Mismatches, sample.Mismatches);
            foreach (var (point, volume) in sample.Overflow)
            {
                Add(HistorianMetrics.Overflow, volume?.CubicMeters, "point", point.Value);
            }
            foreach (var (tank, level) in sample.TankLevels)
            {
                Add(HistorianMetrics.TankLevel, level, "tank", tank.Value);
            }
            foreach (var (gate, setpoint) in sample.Setpoints)
            {
                Add(HistorianMetrics.Setpoint, setpoint?.Fraction, "valve", gate.Value);
            }
            Add(HistorianMetrics.Mode, sample.Mode);
            Add(HistorianMetrics.AlarmsActive, sample.AlarmsActive);
            Add(HistorianMetrics.AlarmsUnacknowledged, sample.AlarmsUnacknowledged);
        }
        sink.Write(samples);
    }

    private string Name(string metric, string? label, string? value)
    {
        if (!_names.TryGetValue((metric, label, value), out var name))
        {
            name = label is null || value is null
                ? PrometheusText.Series(metric, _labels)
                : PrometheusText.Series(metric, [.. _labels, (label, value)]);
            _names[(metric, label, value)] = name;
        }
        return name;
    }
}
