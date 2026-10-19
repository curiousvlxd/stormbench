using System.Buffers.Binary;

namespace StormBench.Contracts;

/// <summary>
/// Modbus register map of the PLC, see docs/modbus-map.md. Every value is 64-bit in OpenPLC's
/// %ML area: holding register 4096 + 4*n is %MLn, big-endian, high word first.
/// </summary>
public static class RegisterMap
{
    public const ushort MlBase = 4096;
    public const int RegistersPerValue = 4;

    public const int SimTimeS = 0;
    public const int CmdMode = 1;
    public const int CenterOk = 2;
    public const int DepthFirst = 3;
    public const int StepReq = 9;
    public const int CenterSetpointFirst = 10;
    public const int CenterStep = 14;
    public const int SetpointFirst = 16;
    public const int ModeActive = 20;
    public const int Status = 21;
    public const int ScanCount = 22;
    public const int StepAck = 23;

    /// <summary>Values %ML0..%ML23, read with one FC3 request (96 registers).</summary>
    public const int SnapshotValues = StepAck + 1;

    public const int Stations = 4;
    public const int Tanks = 6;

    public static ushort Address(int mlIndex) => (ushort)(MlBase + (RegistersPerValue * mlIndex));

    public static long ReadInt64(ReadOnlySpan<ushort> registers, int mlIndex)
    {
        var offset = mlIndex * RegistersPerValue;
        return (long)(((ulong)registers[offset] << 48)
                      | ((ulong)registers[offset + 1] << 32)
                      | ((ulong)registers[offset + 2] << 16)
                      | registers[offset + 3]);
    }

    public static double ReadDouble(ReadOnlySpan<ushort> registers, int mlIndex) =>
        BitConverter.Int64BitsToDouble(ReadInt64(registers, mlIndex));

    public static void WriteInt64(Span<ushort> registers, int valueIndex, long value)
    {
        var offset = valueIndex * RegistersPerValue;
        var bits = (ulong)value;
        registers[offset] = (ushort)(bits >> 48);
        registers[offset + 1] = (ushort)(bits >> 32);
        registers[offset + 2] = (ushort)(bits >> 16);
        registers[offset + 3] = (ushort)bits;
    }

    public static void WriteDouble(Span<ushort> registers, int valueIndex, double value) =>
        WriteInt64(registers, valueIndex, BitConverter.DoubleToInt64Bits(value));

    /// <summary>Center block %ML10..%ML14: four proposals, then the step, in one FC16 request.</summary>
    public static ushort[] EncodeCenterProposal(long step, ReadOnlySpan<double> proposal)
    {
        if (proposal.Length is not Stations)
        {
            throw new ArgumentException($"expected {Stations} proposals", nameof(proposal));
        }
        var registers = new ushort[(Stations + 1) * RegistersPerValue];
        for (var i = 0; i < Stations; i++)
        {
            WriteDouble(registers, i, proposal[i]);
        }
        WriteInt64(registers, Stations, step);
        return registers;
    }

    /// <summary>Registers as bytes on the wire (big-endian per register), e.g. for FC16 clients that take bytes.</summary>
    public static byte[] ToBigEndianBytes(ReadOnlySpan<ushort> registers)
    {
        var bytes = new byte[registers.Length * 2];
        for (var i = 0; i < registers.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), registers[i]);
        }
        return bytes;
    }
}

/// <summary>One consistent read of %ML0..%ML23.</summary>
public sealed record PlcSnapshot(
    long SimTimeS,
    ControlMode CmdMode,
    bool CenterOk,
    IReadOnlyList<double> Depths,
    long StepReq,
    IReadOnlyList<double> CenterSetpoints,
    long CenterStep,
    IReadOnlyList<double> Setpoints,
    ControlMode ModeActive,
    long Status,
    long ScanCount,
    long StepAck)
{
    public static PlcSnapshot Decode(ReadOnlySpan<ushort> registers)
    {
        if (registers.Length is not RegisterMap.SnapshotValues * RegisterMap.RegistersPerValue)
        {
            throw new ArgumentException("unexpected register count", nameof(registers));
        }
        var depths = new double[RegisterMap.Tanks];
        for (var i = 0; i < depths.Length; i++)
        {
            depths[i] = RegisterMap.ReadDouble(registers, RegisterMap.DepthFirst + i);
        }
        var center = new double[RegisterMap.Stations];
        var setpoints = new double[RegisterMap.Stations];
        for (var i = 0; i < RegisterMap.Stations; i++)
        {
            center[i] = RegisterMap.ReadDouble(registers, RegisterMap.CenterSetpointFirst + i);
            setpoints[i] = RegisterMap.ReadDouble(registers, RegisterMap.SetpointFirst + i);
        }
        return new PlcSnapshot(
            RegisterMap.ReadInt64(registers, RegisterMap.SimTimeS),
            (ControlMode)RegisterMap.ReadInt64(registers, RegisterMap.CmdMode),
            RegisterMap.ReadInt64(registers, RegisterMap.CenterOk) is not 0,
            depths,
            RegisterMap.ReadInt64(registers, RegisterMap.StepReq),
            center,
            RegisterMap.ReadInt64(registers, RegisterMap.CenterStep),
            setpoints,
            (ControlMode)RegisterMap.ReadInt64(registers, RegisterMap.ModeActive),
            RegisterMap.ReadInt64(registers, RegisterMap.Status),
            RegisterMap.ReadInt64(registers, RegisterMap.ScanCount),
            RegisterMap.ReadInt64(registers, RegisterMap.StepAck));
    }
}
