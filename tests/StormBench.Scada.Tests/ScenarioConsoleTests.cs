using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MQTTnet;
using Microsoft.Extensions.Logging.Abstractions;
using StormBench.Application.Scenarios;
using StormBench.Contracts;
using StormBench.Domain.Network;
using StormBench.Domain.Scenarios;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

/// <summary>
/// The test console only asks: its commands reach Sim01 as NCMD over a real broker, and it
/// refuses what the simulator would refuse anyway.
/// </summary>
public sealed class ScenarioConsoleTests : IAsyncLifetime, IDisposable
{
    private const string Image =
        "eclipse-mosquitto:2.1.2-alpine@sha256:38c0da4f2ef84284d47b3b3eeea1cb3bdeabe81ee10caf0cd5c5ff61ee3ea408";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly int _port = FreePort();
    private readonly IContainer _broker;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SiteFixture _site = new();
    private readonly ConcurrentQueue<SparkplugPayload> _commands = new();
    private SparkplugHostApplication _host = null!;
    private IMqttClient _listener = null!;
    private StartStormHandler _start = null!;
    private StopStormHandler _stopStorm = null!;
    private SetCenterLinkHandler _link = null!;
    private JamGateHandler _jam = null!;
    private FreezeSensorHandler _freeze = null!;

    public ScenarioConsoleTests()
    {
        _broker = new ContainerBuilder(Image)
            .WithPortBinding(_port, 1883)
            .WithResourceMapping(
                Encoding.UTF8.GetBytes("listener 1883\nallow_anonymous true\npersistence false\n"),
                "/mosquitto/config/mosquitto.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Opening ipv4 listen socket"))
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _broker.StartAsync();
        _host = new SparkplugHostApplication(
            new HostApplicationOptions { Host = "127.0.0.1", Port = _port, HostId = Uns.PrimaryHostId, GroupId = Uns.GroupId },
            TimeProvider.System,
            NullLogger<SparkplugHostApplication>.Instance);
        _ = _host.RunAsync(_shutdown.Token);

        _listener = new MqttClientFactory().CreateMqttClient();
        _listener.ApplicationMessageReceivedAsync += e =>
        {
            _commands.Enqueue(SparkplugCodec.Decode(e.ApplicationMessage.Payload.ToArray()));
            return Task.CompletedTask;
        };
        await _listener.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _port).Build());
        await _listener.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(new SparkplugTopic(Uns.GroupId, MessageTypes.NCmd, Uns.SimNodeId, null).ToString())
            .Build());

        var broker = new PrimaryHostConnection(_host);
        var simulator = new UnsSimulatorCommands(_host);
        _start = new StartStormHandler(_site.Reader, broker, simulator, NullLogger<StartStormHandler>.Instance);
        _stopStorm = new StopStormHandler(_site.Reader, broker, simulator, NullLogger<StopStormHandler>.Instance);
        _link = new SetCenterLinkHandler(_site.Reader, broker, simulator, NullLogger<SetCenterLinkHandler>.Instance);
        _jam = new JamGateHandler(_site.Reader, broker, simulator, NullLogger<JamGateHandler>.Instance);
        _freeze = new FreezeSensorHandler(_site.Reader, broker, simulator, NullLogger<FreezeSensorHandler>.Instance);
        _site.SimBirth();
        await Eventually(() => _host.IsConnected, "host connected");
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        await _listener.DisconnectAsync();
        await _host.DisposeAsync();
        await _broker.DisposeAsync();
    }

    public void Dispose()
    {
        _shutdown.Dispose();
        _listener?.Dispose();
    }

    [Fact]
    public async Task Start_sends_storm_algorithm_and_pace_in_one_command()
    {
        var result = await Start("s2", "base", 0.2);

        Assert.True(result.Accepted, result.Reason);
        var command = await NextCommand();
        Assert.Equal("s2", command.Find(SimMetrics.Storm)?.Value);
        Assert.Equal("base", command.Find(SimMetrics.Algorithm)?.Value);
        Assert.Equal(0.2, command.Find(SimMetrics.Pace)?.Value);
        Assert.Equal(true, command.Find(SimMetrics.Start)?.Value);
        Assert.Null(command.Seq);
    }

    [Theory]
    [InlineData("nope", "base", 0.1, "Unknown storm")]
    [InlineData("s1", "nope", 0.1, "Unknown algorithm")]
    [InlineData("s1", "base", -1.0, "Pace must be")]
    [InlineData("s1", "base", double.NaN, "Pace must be")]
    [InlineData("s1", "base", 9.0, "Pace must be")]
    public async Task Refuses_what_the_simulator_cannot_run(string storm, string algorithm, double pace, string reason)
    {
        var result = await Start(storm, algorithm, pace);

        Assert.False(result.Accepted);
        Assert.StartsWith(reason, result.Reason, StringComparison.Ordinal);
        await NoCommand();
    }

    [Fact]
    public async Task Refuses_a_second_storm_while_one_runs_but_can_stop_it()
    {
        _site.SimData(SparkplugMetric.String(SimMetrics.State, ScenarioStates.Running));

        var start = await Start("s1", "base", 0.1);
        var stop = await _stopStorm.HandleAsync(CancellationToken.None);

        Assert.False(start.Accepted);
        Assert.True(stop.Accepted);
        Assert.Equal(true, (await NextCommand()).Find(SimMetrics.Stop)?.Value);
    }

    [Fact]
    public async Task Link_to_the_center_is_cut_and_restored_by_command()
    {
        Assert.True((await _link.HandleAsync(new SetCenterLink(true), CancellationToken.None)).Accepted);
        Assert.Equal(true, (await NextCommand()).Find(SimMetrics.CenterLinkCut)?.Value);

        _site.SimData(SparkplugMetric.Boolean(SimMetrics.CenterLinkCut, true), SparkplugMetric.Boolean(SimMetrics.CenterLinkUp, false));
        var status = ScenarioStatus.From(_site.Read());
        Assert.True(status.CenterLinkCut);
        Assert.False(status.CenterLinkUp);

        Assert.True((await _link.HandleAsync(new SetCenterLink(false), CancellationToken.None)).Accepted);
        Assert.Equal(false, (await NextCommand()).Find(SimMetrics.CenterLinkCut)?.Value);
    }

    [Fact]
    public async Task A_gate_of_the_network_is_jammed_and_freed_by_command()
    {
        Assert.True((await _jam.HandleAsync(new JamGate(new GateId("V2")), CancellationToken.None)).Accepted);
        Assert.Equal("V2", (await NextCommand()).Find(SimMetrics.StuckGate)?.Value);

        Assert.True((await _jam.HandleAsync(new JamGate(null), CancellationToken.None)).Accepted);
        Assert.Equal("", (await NextCommand()).Find(SimMetrics.StuckGate)?.Value);
    }

    [Fact]
    public async Task A_sensor_of_the_network_is_frozen_and_targets_outside_it_are_refused()
    {
        Assert.True((await _freeze.HandleAsync(new FreezeSensor(new TankId("T1")), CancellationToken.None)).Accepted);
        Assert.Equal("T1", (await NextCommand()).Find(SimMetrics.FrozenSensor)?.Value);

        var gate = await _jam.HandleAsync(new JamGate(new GateId("V9")), CancellationToken.None);
        var tank = await _freeze.HandleAsync(new FreezeSensor(new TankId("V2")), CancellationToken.None);

        Assert.Equal(("Unknown target 'V9'.", "Unknown target 'V2'."), (gate.Reason, tank.Reason));
        await NoCommand();
    }

    [Fact]
    public async Task Nothing_to_stop_when_idle_and_nothing_to_ask_an_offline_simulator()
    {
        Assert.False((await _stopStorm.HandleAsync(CancellationToken.None)).Accepted);

        _site.SimDeath();
        var result = await Start("s1", "base", 0.1);

        Assert.Equal("The simulator is offline.", result.Reason);
        await NoCommand();
    }

    [Fact]
    public void Status_reads_the_console_state_from_the_uns()
    {
        _site.SimData(
            SparkplugMetric.String(SimMetrics.State, ScenarioStates.Running),
            SparkplugMetric.Int64(SimMetrics.Run, 3),
            SparkplugMetric.String(SimMetrics.Storm, "s1"),
            SparkplugMetric.Int64(SimMetrics.Step, 100),
            SparkplugMetric.Int64(SimMetrics.Steps, 400),
            SparkplugMetric.Int64(SimMetrics.Clock, SiteFixture.ClockMs(new DateTime(2000, 5, 30, 8, 20, 0))));

        var status = ScenarioStatus.From(_site.Read());

        Assert.True(status.Busy);
        Assert.True(status.CanStop);
        Assert.False(status.CanStart);
        Assert.Equal(0.25, status.Progress);
        Assert.Equal(new DateTime(2000, 5, 30, 8, 20, 0), status.Clock);
        Assert.Equal("Storm one", status.Catalog!.Storm(status.Storm)!.Label);
    }

    private Task<CommandResult> Start(string storm, string algorithm, double pace) =>
        _start.HandleAsync(new StartStorm(new StormId(storm), new AlgorithmId(algorithm), pace), CancellationToken.None);

    private async Task<SparkplugPayload> NextCommand()
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (_commands.TryDequeue(out var command))
            {
                return command;
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("no NCMD reached Sim01");
            }
            await Task.Delay(20);
        }
    }

    private async Task NoCommand()
    {
        await Task.Delay(300);
        Assert.Empty(_commands);
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"timed out waiting for: {what}");
            }
            await Task.Delay(25);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
