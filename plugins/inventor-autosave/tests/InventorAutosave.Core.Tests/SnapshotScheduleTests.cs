using System;
using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class SnapshotScheduleTests
{
    [Fact]
    public void GetInterval_DefaultsToFiveMinutesWhenInvalid()
    {
        var interval = SnapshotSchedule.GetInterval(0);

        Assert.Equal(TimeSpan.FromMinutes(5), interval);
    }

    [Fact]
    public void ScheduleNextRunUtc_UsesConfiguredMinutes()
    {
        var now = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc);

        var due = SnapshotSchedule.ScheduleNextRunUtc(now, 7);

        Assert.Equal(new DateTime(2026, 3, 11, 12, 7, 0, DateTimeKind.Utc), due);
    }

    [Fact]
    public void FormatCountdown_ReturnsStoppedWhenTimerIsOff()
    {
        var text = SnapshotSchedule.FormatCountdown(false, null, DateTime.UtcNow);

        Assert.Equal("Stopped", text);
    }
}
