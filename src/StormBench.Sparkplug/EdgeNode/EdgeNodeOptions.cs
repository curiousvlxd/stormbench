namespace StormBench.Sparkplug.EdgeNode;

public sealed class EdgeNodeOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string GroupId { get; set; } = "";
    public string EdgeNodeId { get; set; } = "";

    /// <summary>Primary Host whose STATE gates the births (Sparkplug B 3.0); null publishes without waiting.</summary>
    public string? PrimaryHostId { get; set; }

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
}
