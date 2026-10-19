using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using StormBench.Contracts;

namespace StormBench.Connectivity.Plc;

/// <summary>Where the gateway reads the PLC's register image from.</summary>
public interface IPlcSnapshotSource
{
    string Name { get; }

    Task EnsureConnectedAsync(CancellationToken cancellationToken);

    Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);

    void Disconnect();
}

/// <summary>
/// Reads the PLC's located variables from the OpenPLC OPC UA server (SecurityPolicy None,
/// anonymous, read-only; nodes ns=urn:stormbench:plc;s=PLC.main.{Name}) with one Read
/// request. The handshake counters are read first: OpenPLC applies a Modbus write in address order
/// and commits outputs in declaration order, the counters last, so a new counter value implies
/// that the values of its step are visible as well. Proposals are still written over Modbus:
/// anonymous OPC UA sessions on OpenPLC are read-only.
/// </summary>
public sealed partial class OpcUaPlcReader(IOptions<PlcOptions> plcOptions, ILogger<OpcUaPlcReader> logger) : IPlcSnapshotSource, IDisposable
{
    public const string NamespaceUri = "urn:stormbench:plc";

    private static readonly string[] Order =
    [
        "StepReq", "StepAck", "SimTimeS", "CmdMode", "CenterOk",
        "DepthT1", "DepthT2", "DepthT3", "DepthT4", "DepthT5", "DepthT6",
        "CenterSetpointV2", "CenterSetpointV3", "CenterSetpointV4", "CenterSetpointV6", "CenterStep",
        "SetpointV2", "SetpointV3", "SetpointV4", "SetpointV6", "ModeActive", "Status", "ScanCount"
    ];

    private readonly PlcOptions _options = plcOptions.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ApplicationConfiguration? _configuration;
    private ITelemetryContext? _telemetry;
    private ISession? _session;
    private ReadValueIdCollection? _read;

    public string Name => "OPC UA";

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_session is { Connected: true })
        {
            return;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _session?.Dispose();
            _session = null;
            _telemetry ??= DefaultTelemetry.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            _configuration ??= await CreateConfigurationAsync(_telemetry, cancellationToken);

            var endpoint = await CoreClientUtils.SelectEndpointAsync(_configuration, _options.OpcUaEndpoint, false, _telemetry, cancellationToken);
            var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(_configuration));
            var session = await new DefaultSessionFactory(_telemetry)
                .CreateAsync(_configuration, configured, false, false, "StormBench.Connectivity", 60000, new UserIdentity(), null, cancellationToken);

            var ns = session.NamespaceUris.GetIndex(NamespaceUri);
            if (ns < 0)
            {
                session.Dispose();
                throw new InvalidOperationException($"namespace {NamespaceUri} not found on {_options.OpcUaEndpoint}");
            }
            _read = [.. Order.Select(name => new ReadValueId { NodeId = new NodeId($"PLC.main.{name}", (ushort)ns), AttributeId = Attributes.Value })];
            _session = session;
            LogConnected(_options.OpcUaEndpoint, ns);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_session is not { } session || _read is null)
        {
            throw new InvalidOperationException("OPC UA session not connected");
        }
        var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, _read, cancellationToken);
        var values = new Dictionary<string, object>(Order.Length);
        for (var i = 0; i < Order.Length; i++)
        {
            var result = response.Results[i];
            if (StatusCode.IsNotGood(result.StatusCode))
            {
                throw new InvalidOperationException($"OPC UA read of {Order[i]} returned {result.StatusCode}");
            }
            values[Order[i]] = result.Value;
        }

        long Int(string name) => Convert.ToInt64(values[name], System.Globalization.CultureInfo.InvariantCulture);
        double Real(string name) => Convert.ToDouble(values[name], System.Globalization.CultureInfo.InvariantCulture);
        return new PlcSnapshot(
            Int("SimTimeS"),
            (ControlMode)Int("CmdMode"),
            Int("CenterOk") is not 0,
            [.. PlcMetrics.Tanks.Select(t => Real($"Depth{t}"))],
            Int("StepReq"),
            [.. PlcMetrics.ControlledValves.Select(v => Real($"CenterSetpoint{v}"))],
            Int("CenterStep"),
            [.. PlcMetrics.ControlledValves.Select(v => Real($"Setpoint{v}"))],
            (ControlMode)Int("ModeActive"),
            Int("Status"),
            Int("ScanCount"),
            Int("StepAck"));
    }

    public void Disconnect()
    {
        _session?.Dispose();
        _session = null;
    }

    private static async Task<ApplicationConfiguration> CreateConfigurationAsync(ITelemetryContext telemetry, CancellationToken cancellationToken)
    {
        var pki = Path.Combine(Path.GetTempPath(), "stormbench-connectivity-pki");
        var configuration = new ApplicationConfiguration(telemetry)
        {
            ApplicationName = "StormBench.Connectivity",
            ApplicationUri = $"urn:{System.Net.Dns.GetHostName()}:StormBench.Connectivity",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "own"),
                    SubjectName = "CN=StormBench.Connectivity"
                },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "issuer") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "rejected") },
                AutoAcceptUntrustedCertificates = false
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
        };
        await configuration.ValidateAsync(ApplicationType.Client, cancellationToken);
        await new ApplicationInstance(configuration, telemetry).CheckApplicationInstanceCertificatesAsync(false, null, cancellationToken);
        return configuration;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _gate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OPC UA session to {Endpoint}, namespace index {Namespace}")]
    private partial void LogConnected(string endpoint, int @namespace);
}
