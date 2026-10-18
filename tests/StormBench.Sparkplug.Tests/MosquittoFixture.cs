using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace StormBench.Sparkplug.Tests;

/// <summary>A real Mosquitto 2.1 broker with the Sparkplug Aware plugin, as in deploy/mosquitto.</summary>
public sealed class MosquittoFixture : IAsyncLifetime
{
    public const string Image =
        "eclipse-mosquitto:2.1.2-alpine@sha256:38c0da4f2ef84284d47b3b3eeea1cb3bdeabe81ee10caf0cd5c5ff61ee3ea408";

    private const string Config = """
        listener 1883
        allow_anonymous true
        global_plugin /usr/lib/mosquitto_sparkplug_aware.so
        persistence false
        log_dest stdout
        """;

    public IContainer Broker { get; } = new ContainerBuilder(Image)
        .WithPortBinding(1883, true)
        .WithResourceMapping(Encoding.UTF8.GetBytes(Config), "/mosquitto/config/mosquitto.conf")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Opening ipv4 listen socket"))
        .Build();

    public string Host => Broker.Hostname;

    public int Port => Broker.GetMappedPublicPort(1883);

    public async ValueTask InitializeAsync() => await Broker.StartAsync();

    public ValueTask DisposeAsync() => Broker.DisposeAsync();
}
