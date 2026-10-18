using System.Globalization;
using StormBench.Domain.Control;
using StormBench.Domain.Scenarios;
using DomainVolume = StormBench.Domain.Network.Volume;

namespace StormBench.Scada.Hmi;

/// <summary>Number and time formats of the HMI: one style everywhere, invariant culture.</summary>
public static class Fmt
{
    /// <summary>Shown where there is no value yet (no run, no data).</summary>
    public const string NoValue = "--";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Volume(double? m3) => m3 is { } v ? $"{v.ToString("N0", Invariant)} m³" : NoValue;

    public static string Volume(DomainVolume? volume) => Volume(volume?.CubicMeters);

    public static string SignedVolume(double m3) => $"{(m3 > 0 ? "+" : m3 < 0 ? "−" : "±")}{Math.Abs(m3).ToString("N0", Invariant)} m³";

    public static string Percent(double? fraction) => fraction is { } f ? $"{Math.Round(f * 100).ToString("0", Invariant)}%" : NoValue;

    public static string SignedPercent(double fraction)
    {
        var rounded = Math.Round(fraction * 100, 1);
        var sign = rounded > 0 ? "+" : rounded < 0 ? "−" : "±";
        return $"{sign}{Math.Abs(rounded).ToString("0.0", Invariant)}%";
    }

    public static string Metres(double? m) => m is { } v ? $"{v.ToString("0.00", Invariant)} m" : NoValue;

    public static string Rain(double? mmPerHour) => mmPerHour is { } v ? $"{v.ToString("0.0", Invariant)} mm/h" : NoValue;

    public static string Clock(DateTime? time) => time is { } t ? t.ToString("d MMM yyyy  HH:mm", Invariant) : NoValue;

    public static string Time(DateTime? time) => time is { } t ? t.ToString("d MMM HH:mm", Invariant) : NoValue;

    public static string WallTime(DateTimeOffset time) => time.ToLocalTime().ToString("HH:mm:ss", Invariant);

    public static string Count(long value) => value.ToString("N0", Invariant);

    public static string Pace(double seconds) => seconds <= 0
        ? "max"
        : $"{(1.0 / seconds).ToString("0.#", Invariant)}/s";

    /// <summary>Simulated minutes per wall second at a pace, for a 5-minute control step.</summary>
    public static string PaceHint(double seconds, int stepSeconds) => seconds <= 0
        ? "Max: as fast as the PLC answers"
        : $"One wall second is {(stepSeconds / 60.0 / seconds).ToString("0", Invariant)} simulated minutes";

    /// <summary>The PLC's active mode; in mode 3 the console's algorithm names which center proposes.</summary>
    public static string Mode(ControlMode? mode, ControlAlgorithm? algorithm) =>
        mode is ControlMode.EfdCenter && algorithm is { Mode: ControlMode.EfdCenter, Center: not null }
            ? algorithm.Label
            : Mode(mode);

    public static string Mode(ControlMode? mode) => mode switch
    {
        ControlMode.Local => "Base Case (local)",
        ControlMode.EfdPublished => "EFD in the PLC",
        ControlMode.EfdIntended => "EFD in the PLC, as intended",
        ControlMode.EfdCenter => "EFD from the center",
        null => NoValue,
        _ => $"mode {(long)mode}",
    };
}
