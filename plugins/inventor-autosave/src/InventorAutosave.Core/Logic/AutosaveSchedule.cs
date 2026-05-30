using System;

namespace InventorAutosave.Core.Logic;

internal static class AutosaveSchedule
{
    public static TimeSpan GetInterval(int intervalMinutes)
    {
        var sanitizedMinutes = intervalMinutes >= 1
            ? intervalMinutes
            : AutosaveDefaults.DefaultIntervalMinutes;
        return TimeSpan.FromMinutes(sanitizedMinutes);
    }

    public static DateTime ScheduleNextRunUtc(DateTime nowUtc, int intervalMinutes)
    {
        return nowUtc.Add(GetInterval(intervalMinutes));
    }

    public static string FormatCountdown(bool isRunning, DateTime? nextRunUtc, DateTime nowUtc)
    {
        if (!isRunning || !nextRunUtc.HasValue)
        {
            return "Stopped";
        }

        var remaining = nextRunUtc.Value - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return "Due";
        }

        return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }
}
