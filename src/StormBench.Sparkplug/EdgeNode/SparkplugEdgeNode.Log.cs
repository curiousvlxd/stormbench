using Microsoft.Extensions.Logging;

namespace StormBench.Sparkplug.EdgeNode;

public sealed partial class SparkplugEdgeNode
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to {Host}:{Port}, bdSeq {BdSeq}")]
    private partial void LogConnected(string host, int port, ulong bdSeq);

    [LoggerMessage(Level = LogLevel.Information, Message = "NBIRTH published, bdSeq {BdSeq}, {Devices} device(s)")]
    private partial void LogBorn(ulong bdSeq, int devices);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session with bdSeq {BdSeq} ended")]
    private partial void LogSessionEnded(ulong bdSeq);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT session failed: {Reason}")]
    private partial void LogSessionFailed(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebirth requested by NCMD")]
    private partial void LogRebirthRequested();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Primary host went offline: NDEATH and new session")]
    private partial void LogHostOffline();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Undecodable Sparkplug payload on {Topic}")]
    private partial void LogBadPayload(string topic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Edge node work item failed: {Reason}")]
    private partial void LogWorkFailed(string reason);
}
