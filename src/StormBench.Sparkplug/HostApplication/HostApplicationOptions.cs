namespace StormBench.Sparkplug.HostApplication;

public sealed class HostApplicationOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string HostId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
}
