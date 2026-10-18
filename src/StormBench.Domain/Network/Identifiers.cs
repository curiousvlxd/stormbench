using System.Diagnostics.CodeAnalysis;

namespace StormBench.Domain.Network;

/// <summary>A storage tank of the network, e.g. T2.</summary>
public readonly record struct TankId(string Value) : IParsable<TankId>
{
    public override string ToString() => Value;

    public static TankId Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TankId result)
    {
        result = new(s ?? "");
        return s is not null;
    }
}

/// <summary>A gate (orifice) at a tank outlet, e.g. V2.</summary>
public readonly record struct GateId(string Value) : IParsable<GateId>
{
    public override string ToString() => Value;

    public static GateId Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out GateId result)
    {
        result = new(s ?? "");
        return s is not null;
    }
}

/// <summary>A place where the network spills into the river: a tank or a weir without storage, e.g. T1 or CSO7.</summary>
public readonly record struct OverflowPointId(string Value) : IParsable<OverflowPointId>
{
    /// <summary>The overflow of a tank carries the tank's id.</summary>
    public static OverflowPointId Of(TankId tank) => new(tank.Value);

    public override string ToString() => Value;

    public static OverflowPointId Parse(string s, IFormatProvider? provider) => new(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out OverflowPointId result)
    {
        result = new(s ?? "");
        return s is not null;
    }
}
