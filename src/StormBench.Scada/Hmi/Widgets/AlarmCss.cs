using StormBench.Domain.Alarms;

namespace StormBench.Scada.Hmi.Widgets;

public static class AlarmCss
{
    /// <summary>p1..p3 for the priority; returned (outlined) after the condition cleared; unack blinks.</summary>
    public static string Classes(AlarmPriority priority, Alarm? alarm)
    {
        var classes = $"p{(int)priority}";
        if (alarm is null)
        {
            return classes;
        }
        if (!alarm.Active)
        {
            classes += " returned";
        }
        if (alarm.NeedsAcknowledge)
        {
            classes += " unack";
        }
        return classes;
    }

    public static string StateText(AlarmState state) => state switch
    {
        AlarmState.Unacknowledged => "Active, unacknowledged",
        AlarmState.Acknowledged => "Active, acknowledged",
        AlarmState.ReturnedUnacknowledged => "Cleared, unacknowledged",
        _ => "Normal",
    };
}
