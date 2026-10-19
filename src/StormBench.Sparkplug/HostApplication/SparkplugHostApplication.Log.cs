using Microsoft.Extensions.Logging;

namespace StormBench.Sparkplug.HostApplication;

public sealed partial class SparkplugHostApplication
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Primary host {HostId} online at {Host}:{Port}")]
    private partial void LogOnline(string hostId, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Primary host session ended")]
    private partial void LogSessionEnded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT session failed: {Reason}")]
    private partial void LogSessionFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Undecodable Sparkplug payload on {Topic}")]
    private partial void LogBadPayload(string topic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler failed for {Topic}: {Reason}")]
    private partial void LogHandlerFailed(string topic, string reason);
}
