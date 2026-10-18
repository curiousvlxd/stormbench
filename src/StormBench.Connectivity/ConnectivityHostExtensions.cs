using StormBench.Connectivity.Plc;
using StormBench.Contracts;
using StormBench.Sparkplug;

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

            var plc = builder.Configuration.GetSection("Plc");
            builder.Services.Configure<PlcOptions>(plc);
            builder.Services.Configure<EdgeNodeOptions>(builder.Configuration.GetSection("Mqtt"));
            builder.Services.PostConfigure<EdgeNodeOptions>(o =>
            {
                o.GroupId = string.IsNullOrEmpty(o.GroupId) ? Uns.GroupId : o.GroupId;
                o.EdgeNodeId = string.IsNullOrEmpty(o.EdgeNodeId) ? Uns.EdgeNodeId : o.EdgeNodeId;
                o.PrimaryHostId ??= Uns.PrimaryHostId;
            });

            builder.Services
                .AddSingleton(TimeProvider.System)
                .AddSingleton<GatewayMetrics>()
                .AddSingleton<PlcModbusClient>()
                .AddOptionsValue<EdgeNodeOptions>()
                .AddSingleton<SparkplugEdgeNode>()
                .AddHostedService<EdgeGateway>();

            if (PlcOptions.IsOpcUa(plc.Get<PlcOptions>()?.Driver))
            {
                builder.Services.AddSingleton<IPlcSnapshotSource, OpcUaPlcReader>();
            }
            else
            {
                builder.Services.AddSingleton<IPlcSnapshotSource>(sp => sp.GetRequiredService<PlcModbusClient>());
            }
            return builder;
        }
    }
}
