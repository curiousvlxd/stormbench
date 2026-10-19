using Microsoft.Extensions.Options;
using StormBench.Connectivity.EdgeNode;
using StormBench.Connectivity.Plc;
using StormBench.Sparkplug.EdgeNode;

namespace StormBench.Connectivity;

/// <summary>Composition root of the edge gateway: the PLC driver, the Sparkplug edge node and the gateway between them.</summary>
public static class ConnectivityHostExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder AddConnectivity()
        {
            builder.AddServiceDefaults();
            builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(GatewayMetrics.MeterName));
            builder.Services
                .ConfigureOptions<PlcOptionsSetup>()
                .ConfigureOptions<EdgeNodeOptionsSetup>()
                .AddSingleton(TimeProvider.System)
                .AddSingleton<GatewayMetrics>()
                .AddSingleton<PlcModbusClient>()
                .AddKeyedSingleton<IPlcSnapshotSource>(PlcDriver.Modbus, (sp, _) => sp.GetRequiredService<PlcModbusClient>())
                .AddKeyedSingleton<IPlcSnapshotSource, OpcUaPlcReader>(PlcDriver.OpcUa)
                .AddSingleton(sp => sp.GetRequiredKeyedService<IPlcSnapshotSource>(sp.GetRequiredService<IOptions<PlcOptions>>().Value.Driver))
                .AddOptionsValue<EdgeNodeOptions>()
                .AddSingleton<SparkplugEdgeNode>()
                .AddHostedService<EdgeGateway>();
            return builder;
        }
    }
}
