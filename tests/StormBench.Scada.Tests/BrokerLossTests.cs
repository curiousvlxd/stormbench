using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StormBench.Application.Alarms;
using StormBench.Application.Historian;
using StormBench.Application.Optimization;
using StormBench.Application.Runs;
using StormBench.Application.Site;
using StormBench.Contracts;
using StormBench.Domain.Alarms;
using StormBench.Domain.Control;
using StormBench.Infrastructure.Postgres;
using StormBench.Infrastructure.PrimaryHost;
using StormBench.Infrastructure.VictoriaMetrics;
using StormBench.Sparkplug;

namespace StormBench.Scada.Tests;

/// <summary>
/// Phase 2 criterion: when the edge node or the broker goes away, the primary host marks the PLC's
/// data stale (NDEATH or lost connection) and returns to good quality after the rebirth.
/// Real Mosquitto 2.1 broker, real edge node, real Scada host wiring.
/// </summary>
public sealed class BrokerLossTests : IAsyncLifetime, IDisposable
{
    private const string Image =
        "eclipse-mosquitto:2.1.2-alpine@sha256:38c0da4f2ef84284d47b3b3eeea1cb3bdeabe81ee10caf0cd5c5ff61ee3ea408";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly string LevelT2 = PlcMetrics.Level("T2");

    private readonly int _port = FreePort();
    private readonly IContainer _broker;
    private readonly UnsState _state = new(TimeProvider.System);
    private readonly CancellationTokenSource _stop = new();
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private SparkplugHostApplication _host = null!;
    private SparkplugEdgeNode _edge = null!;
    private UnsHostWorker _worker = null!;
    private AlarmEngine _alarms = null!;

    public BrokerLossTests()
    {
        _broker = new ContainerBuilder(Image)
            .WithPortBinding(_port, 1883)
            .WithResourceMapping(
                Encoding.UTF8.GetBytes("listener 1883\nallow_anonymous true\npersistence false\nset_tcp_nodelay true\n"),
                "/mosquitto/config/mosquitto.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Opening ipv4 listen socket"))
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _broker.StartAsync();
        var fast = TimeSpan.FromMilliseconds(200);
        _host = new SparkplugHostApplication(
            new HostApplicationOptions
            {
                Host = "127.0.0.1", Port = _port, HostId = Uns.PrimaryHostId, GroupId = Uns.GroupId, ReconnectDelay = fast,
            },
            TimeProvider.System,
            NullLogger<SparkplugHostApplication>.Instance);
        _edge = new SparkplugEdgeNode(
            new EdgeNodeOptions
            {
                Host = "127.0.0.1", Port = _port, GroupId = Uns.GroupId, EdgeNodeId = Uns.EdgeNodeId,
                PrimaryHostId = Uns.PrimaryHostId, ReconnectDelay = fast,
            },
            TimeProvider.System,
            NullLogger<SparkplugEdgeNode>.Instance);
        var site = new UnsSiteReader(_state, new PrimaryHostConnection(_host));
        var optimizer = new CenterOptimizer(
            site, new UnsPlcCommands(_host), Options.Create(new EfdParameters()),
            new OptimizationMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<CenterOptimizer>.Instance);
        _alarms = new AlarmEngine(TimeProvider.System);
        var runs = new RunRecorder(new NoRunStore(), TimeProvider.System);
        var monitor = new SiteMonitor(
            site, runs, _alarms, new FieldChecks(), new RunTrend(), new RunSampler(runs, _alarms, new PrometheusRunSeries(new NoSampleSink())));
        _worker = new UnsHostWorker(_host, _state, optimizer, monitor, TimeProvider.System, NullLogger<UnsHostWorker>.Instance);

        await _edge.PublishDeviceDataAsync(
            Uns.PlcDeviceId, [SparkplugMetric.Double(LevelT2, 1.5), SparkplugMetric.Int64(PlcMetrics.StepReq, 1)], CancellationToken.None);
        await _worker.StartAsync(_stop.Token);
        _ = _edge.RunAsync(_stop.Token);
        await Eventually(() => Quality() is Infrastructure.PrimaryHost.Quality.Good, "initial birth");
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _worker.StopAsync(CancellationToken.None);
        await _edge.DisposeAsync();
        await _host.DisposeAsync();
        await _broker.DisposeAsync();
        await _services.DisposeAsync();
    }

    public void Dispose()
    {
        _stop.Dispose();
        _worker?.Dispose();
    }

    private Infrastructure.PrimaryHost.Quality? Quality() =>
        _state.TryGetDeviceMetric(Uns.GroupId, Uns.EdgeNodeId, Uns.PlcDeviceId, LevelT2, out var m) ? m.Quality : null;

    private NodeSnapshot Node() => _state.Snapshot().Single(n => n.EdgeNodeId == Uns.EdgeNodeId);

    private Alarm? Alarm(string id) => _alarms.Current().FirstOrDefault(a => a.Definition.Id == id);

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

    [Fact]
    public async Task Lost_edge_connection_marks_plc_data_stale_until_the_next_birth()
    {
        var bdSeq = Node().BdSeq;

        await _edge.DropConnectionAsync(CancellationToken.None);

        await Eventually(() => Quality() is Infrastructure.PrimaryHost.Quality.Stale, "stale after NDEATH");
        await Eventually(() => Alarm(AlarmDefinitions.EdgeOffline.Id) is not null, "gateway alarm");
        await Eventually(() => Quality() is Infrastructure.PrimaryHost.Quality.Good && Node().BdSeq == bdSeq + 1, "rebirth with next bdSeq");
        await Eventually(() => Alarm(AlarmDefinitions.EdgeOffline.Id)?.State is AlarmState.ReturnedUnacknowledged, "gateway alarm cleared, waiting for acknowledgement");
    }

    [Fact]
    public async Task Broker_outage_marks_everything_stale_and_recovers_after_restart()
    {
        await _broker.StopAsync();

        await Eventually(() => Quality() is Infrastructure.PrimaryHost.Quality.Stale, "stale after losing the broker");
        Assert.False(Node().Online);
        await Eventually(() => Alarm(AlarmDefinitions.Broker.Id)?.Active == true, "broker alarm");

        await _broker.StartAsync();

        await Eventually(() => Quality() is Infrastructure.PrimaryHost.Quality.Good && Node().Online, "good again after the broker returns");
        await Eventually(() => Alarm(AlarmDefinitions.Broker.Id)?.State is AlarmState.ReturnedUnacknowledged, "broker alarm cleared");
    }
}
