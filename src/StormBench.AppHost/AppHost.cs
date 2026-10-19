using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var repo = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

var mqttPort = builder.Configuration.GetValue("Ports:Mqtt", 21883);
var modbusPort = builder.Configuration.GetValue("Ports:Modbus", 21502);
var opcUaPort = builder.Configuration.GetValue("Ports:OpcUa", 24840);
var plcDriver = builder.Configuration.GetValue("Plc:Driver", "Modbus");
var opcUaEndpoint = $"opc.tcp://127.0.0.1:{opcUaPort}/openplc/opcua";

var mosquitto = builder.AddContainer("mosquitto", "eclipse-mosquitto", "2.1.2-alpine")
    .WithImageSHA256("38c0da4f2ef84284d47b3b3eeea1cb3bdeabe81ee10caf0cd5c5ff61ee3ea408")
    .WithBindMount(Path.Combine(repo, "deploy", "mosquitto", "mosquitto.conf"), "/mosquitto/config/mosquitto.conf", isReadOnly: true)
    .WithEndpoint(port: mqttPort, targetPort: 1883, scheme: "tcp", name: "mqtt", isProxied: false);

var plc = builder.AddDockerfile("plc", Path.Combine(repo, "plc"))
    .WithBuildArg("TASK_INTERVAL", "T#1ms")
    .WithContainerRuntimeArgs("--init", "--cap-add", "SYS_NICE", "--cap-add", "SYS_RESOURCE")
    .WithEndpoint(port: modbusPort, targetPort: 502, scheme: "tcp", name: "modbus", isProxied: false)
    .WithEndpoint(port: opcUaPort, targetPort: 4840, scheme: "tcp", name: "opcua", isProxied: false);

var mqtt = mosquitto.GetEndpoint("mqtt");
var modbus = plc.GetEndpoint("modbus");

var grafanaPort = builder.Configuration.GetValue("Ports:Grafana", 23000);
var victoriaMetricsPort = builder.Configuration.GetValue("Ports:VictoriaMetrics", 28428);
var victoriaLogsPort = builder.Configuration.GetValue("Ports:VictoriaLogs", 29428);
var postgresPort = builder.Configuration.GetValue("Ports:Postgres", 25432);

var victoriaMetrics = builder.AddContainer("victoriametrics", "victoriametrics/victoria-metrics", "v1.153.0")
    .WithImageSHA256("5eff7af5341e401471002f58d106d399e614a62f3d240f2dbc21901e49eed5dd")
    .WithArgs("-retentionPeriod=100y", "-search.disableCache", "-opentelemetry.usePrometheusNaming")
    .WithVolume("stormbench-victoriametrics", "/victoria-metrics-data")
    .WithHttpEndpoint(port: victoriaMetricsPort, targetPort: 8428, name: "http")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "VictoriaMetrics");

var victoriaLogs = builder.AddContainer("victorialogs", "victoriametrics/victoria-logs", "v1.53.0")
    .WithImageSHA256("251121fa882af99b95ba0c230a4a2f412ea602d2698c64a96c58dc9842bb755d")
    .WithVolume("stormbench-victorialogs", "/victoria-logs-data")
    .WithHttpEndpoint(port: victoriaLogsPort, targetPort: 9428, name: "http")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "VictoriaLogs");

var postgres = builder.AddPostgres("postgres", port: postgresPort)
    .WithImage("library/postgres", "17.11-alpine")
    .WithImageSHA256("b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24")
    .WithDataVolume("stormbench-postgres");
var runs = postgres.AddDatabase("runs");
var postgresTcp = postgres.GetEndpoint("tcp");

var grafanaDir = Path.Combine(repo, "deploy", "grafana");
var grafana = builder.AddContainer("grafana", "grafana/grafana", "13.2.4")
    .WithImageSHA256("dbbc38ea21103cefb50396340cd88afec779c7e070b0a7bf5a75b4ce46651d3f")
    .WithBindMount(Path.Combine(grafanaDir, "provisioning", "datasources"), "/etc/grafana/provisioning/datasources", isReadOnly: true)
    .WithBindMount(Path.Combine(grafanaDir, "provisioning", "dashboards"), "/etc/grafana/provisioning/dashboards", isReadOnly: true)
    .WithBindMount(Path.Combine(grafanaDir, "dashboards"), "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithEnvironment("VM_URL", victoriaMetrics.GetEndpoint("http"))
    .WithEnvironment("PG_HOST", postgresTcp.Property(EndpointProperty.HostAndPort))
    .WithEnvironment("PG_USER", postgres.Resource.UserNameReference)
    .WithEnvironment("PG_PASSWORD", ReferenceExpression.Create($"{postgres.Resource.PasswordParameter}"))
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
    .WithEnvironment("GF_DASHBOARDS_DEFAULT_HOME_DASHBOARD_PATH", "/var/lib/grafana/dashboards/algorithm-comparison.json")
    .WithEnvironment("GF_ANALYTICS_REPORTING_ENABLED", "false")
    .WithEnvironment("GF_ANALYTICS_CHECK_FOR_UPDATES", "false")
    .WithEnvironment("GF_ANALYTICS_CHECK_FOR_PLUGIN_UPDATES", "false")
    .WithEnvironment("GF_NEWS_NEWS_FEED_ENABLED", "false")
    .WithHttpEndpoint(port: grafanaPort, targetPort: 3000, name: "http")
    .WithHttpHealthCheck("/api/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Grafana")
    .WaitFor(victoriaMetrics)
    .WaitFor(runs);
var algorithmComparison = ReferenceExpression.Create($"{grafana.GetEndpoint("http")}/d/stormbench-algorithm-comparison");
grafana.WithUrl(algorithmComparison, "Algorithm comparison");

var scada = builder.AddProject<Projects.StormBench_Scada>("scada")
    .WithEnvironment("Mqtt__Host", mqtt.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqtt.Property(EndpointProperty.Port))
    .WithEnvironment("VictoriaMetrics__Url", victoriaMetrics.GetEndpoint("http"))
    .WithEnvironment("VictoriaLogs__Url", victoriaLogs.GetEndpoint("http"))
    .WithReference(runs)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "HMI")
    .WithUrl(algorithmComparison, "Trends (Grafana)")
    .WaitFor(mosquitto);

var connectivity = builder.AddProject<Projects.StormBench_Connectivity>("connectivity")
    .WithEnvironment("Mqtt__Host", mqtt.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqtt.Property(EndpointProperty.Port))
    .WithEnvironment("VictoriaMetrics__Url", victoriaMetrics.GetEndpoint("http"))
    .WithEnvironment("VictoriaLogs__Url", victoriaLogs.GetEndpoint("http"))
    .WithEnvironment("Plc__Host", modbus.Property(EndpointProperty.Host))
    .WithEnvironment("Plc__Port", modbus.Property(EndpointProperty.Port))
    .WithEnvironment("Plc__Driver", plcDriver)
    .WithEnvironment("Plc__OpcUaEndpoint", opcUaEndpoint)
    .WaitFor(mosquitto)
    .WaitFor(plc)
    .WaitFor(scada);

builder.AddPythonModule("simulator", Path.Combine(repo, "simulator"), "stormbench_sim")
    .WithUv()
    .WithEnvironment("VictoriaLogs__Url", victoriaLogs.GetEndpoint("http"))
    .WithArgs(
        "--model-dir", Path.Combine(repo, "models", "astlingen"),
        "--results-dir", Path.Combine(repo, "results"),
        "--work-dir", Path.Combine(repo, "work"),
        "serve",
        "--mqtt-host", mqtt.Property(EndpointProperty.Host),
        "--mqtt-port", mqtt.Property(EndpointProperty.Port),
        "--plc-host", modbus.Property(EndpointProperty.Host),
        "--plc-port", modbus.Property(EndpointProperty.Port),
        "--plc-timeout", "60")
    .WaitFor(connectivity);

builder.Build().Run();
