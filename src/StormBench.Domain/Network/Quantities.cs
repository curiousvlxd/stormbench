namespace StormBench.Domain.Network;

/// <summary>A volume of water in cubic metres, e.g. what spilled at an overflow point since the run started.</summary>
public readonly record struct Volume(double CubicMeters) : IComparable<Volume>
{
    public static Volume Zero => default;

    public static Volume operator +(Volume left, Volume right) => new(left.CubicMeters + right.CubicMeters);

    public static Volume operator -(Volume left, Volume right) => new(left.CubicMeters - right.CubicMeters);

    public static Volume operator -(Volume value) => new(-value.CubicMeters);

    public static bool operator <(Volume left, Volume right) => left.CubicMeters < right.CubicMeters;

    public static bool operator >(Volume left, Volume right) => left.CubicMeters > right.CubicMeters;

    public static bool operator <=(Volume left, Volume right) => left.CubicMeters <= right.CubicMeters;

    public static bool operator >=(Volume left, Volume right) => left.CubicMeters >= right.CubicMeters;

    /// <summary>This volume against a reference as a fraction: -0.1 is ten percent less.</summary>
    public double RelativeTo(Volume reference) => (CubicMeters - reference.CubicMeters) / reference.CubicMeters;

    public int CompareTo(Volume other) => CubicMeters.CompareTo(other.CubicMeters);
}

/// <summary>How full a tank is: its level as a fraction of the tank depth, 0 (empty) to 1 (full).</summary>
public readonly record struct FillFraction
{
    private FillFraction(double value) => Value = value;

    public double Value { get; }

    public static FillFraction Of(double level, double depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        return new(Math.Clamp(level / depth, 0.0, 1.0));
    }
}

/// <summary>Opening of a gate as a fraction: 0 closed, 1 fully open.</summary>
public readonly record struct GateOpening(double Fraction)
{
    public static GateOpening FullyOpen { get; } = new(1.0);

    /// <summary>Whether two openings differ by more than a tolerance, e.g. a setpoint and the position feedback.</summary>
    public bool DiffersFrom(GateOpening other, double tolerance) => Math.Abs(Fraction - other.Fraction) > tolerance;
}
