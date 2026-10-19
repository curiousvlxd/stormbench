using System.Net;
using System.Net.Sockets;
using FluentModbus;
using Microsoft.Extensions.Options;
using StormBench.Connectivity.Plc;
using StormBench.Contracts;

namespace StormBench.Connectivity.Tests;

/// <summary>The gateway's Modbus path over a real Modbus TCP stack (in-process server with unit id 1).</summary>
public sealed class PlcModbusClientTests : IDisposable
{
    private readonly ModbusTcpServer _server = new();
    private readonly PlcModbusClient _client;

    public PlcModbusClientTests()
    {
        var port = FreePort();
        _server.AddUnit(1);
        _server.Start(new IPEndPoint(IPAddress.Loopback, port));
        _client = new PlcModbusClient(Options.Create(new PlcOptions { Host = "127.0.0.1", Port = port }));
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task Center_proposal_written_by_the_gateway_is_read_back_in_the_snapshot()
    {
        await _client.EnsureConnectedAsync(CancellationToken.None);
        double[] proposal = [1.0, 0.6508, 0.3523, 0.4303];

        await _client.WriteCenterProposalAsync(4711, proposal, CancellationToken.None);
        var snapshot = await _client.ReadSnapshotAsync(CancellationToken.None);

        Assert.Equal(4711, snapshot.CenterStep);
        Assert.Equal(proposal, snapshot.CenterSetpoints);
        Assert.Equal(0, snapshot.StepReq);
    }

    [Fact]
    public async Task Proposal_lands_exactly_in_the_center_block()
    {
        await _client.EnsureConnectedAsync(CancellationToken.None);
        await _client.WriteCenterProposalAsync(-1, [0.1, 0.2, 0.3, 0.4], CancellationToken.None);

        var raw = _server.GetHoldingRegisterBuffer(1);
        var first = RegisterMap.Address(RegisterMap.CenterSetpointFirst) * 2;
        var last = (RegisterMap.Address(RegisterMap.CenterStep) + RegisterMap.RegistersPerValue) * 2;
        Assert.Contains(raw[first..last].ToArray(), b => b is not 0);
        Assert.All(raw[..first].ToArray(), b => Assert.Equal(0, b));
        Assert.All(raw[last..(last + 64)].ToArray(), b => Assert.Equal(0, b));
    }
}
