using System.Net;
using FluentModbus;
using Microsoft.Extensions.Options;
using StormBench.Contracts;

namespace StormBench.Connectivity.Plc;

public sealed class PlcOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 502;

    /// <summary>OpenPLC v4 answers unit id 1 only.</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>Pause between two reads of the register image; zero polls back to back.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.Zero;

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Modbus (default, the lockstep path) or OpcUa (read over OPC UA, write proposals over Modbus).</summary>
    public string Driver { get; set; } = "Modbus";

    public string OpcUaEndpoint { get; set; } = "opc.tcp://127.0.0.1:4840/openplc/opcua";

    /// <summary>Whether a driver name selects the OPC UA reader; anything else is Modbus.</summary>
    public static bool IsOpcUa(string? driver) => string.Equals(driver, "OpcUa", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the whole PLC register image (%ML0..%ML23) with one FC3 request, so every snapshot is
/// consistent, and writes the central optimiser's proposal with one FC16 request, step last.
/// One TCP connection shared by the poll loop and the command path, serialised by a gate.
/// </summary>
public sealed class PlcModbusClient(IOptions<PlcOptions> plcOptions) : IPlcSnapshotSource, IDisposable
{
    private readonly PlcOptions _options = plcOptions.Value;
    private readonly ModbusTcpClient _client = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Name => "Modbus";

    public bool IsConnected => _client.IsConnected;

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }
        var addresses = await Dns.GetHostAddressesAsync(_options.Host, cancellationToken);
        var address = addresses.FirstOrDefault(a => a.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork)
                      ?? addresses.First();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _client.Connect(new IPEndPoint(address, _options.Port), ModbusEndianness.BigEndian);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var count = (ushort)(RegisterMap.SnapshotValues * RegisterMap.RegistersPerValue);
            var bytes = await _client.ReadHoldingRegistersAsync(_options.UnitId, RegisterMap.Address(0), count, cancellationToken);
            return PlcSnapshot.Decode(ToRegisters(bytes.Span));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteCenterProposalAsync(long step, IReadOnlyList<double> proposal, CancellationToken cancellationToken)
    {
        var registers = RegisterMap.EncodeCenterProposal(step, proposal.ToArray());
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _client.WriteMultipleRegistersAsync(
                    _options.UnitId, RegisterMap.Address(RegisterMap.CenterSetpointFirst), RegisterMap.ToBigEndianBytes(registers), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Disconnect()
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }
    }

    private static ushort[] ToRegisters(ReadOnlySpan<byte> bytes)
    {
        var registers = new ushort[bytes.Length / 2];
        for (var i = 0; i < registers.Length; i++)
        {
            registers[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[(i * 2)..]);
        }
        return registers;
    }

    public void Dispose()
    {
        _client.Dispose();
        _gate.Dispose();
    }
}
