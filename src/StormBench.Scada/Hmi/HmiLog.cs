namespace StormBench.Scada.Hmi;

/// <summary>Log messages of the HMI (components cannot host LoggerMessage methods themselves).</summary>
public static partial class HmiLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not build the overview display")]
    public static partial void BuildFailed(this ILogger logger, Exception exception);
}
