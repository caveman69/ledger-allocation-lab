using System.Globalization;

namespace LedgerAllocationLab.Core.Tests;

public class BusinessDateClockTests
{
    private static readonly BusinessDateClock Clock = BusinessDateClock.Phoenix();

    // Phoenix is UTC-7 all year, so local midnight is 07:00 UTC.
    [Theory]
    [InlineData("2026-10-01T06:30:00", "2026-09-30")]     // 11:30 PM Sept 30 local
    [InlineData("2026-10-01T07:30:00", "2026-10-01")]     // 12:30 AM Oct 1 local
    [InlineData("2026-10-01T06:59:59.999", "2026-09-30")] // last millisecond of Sept 30
    [InlineData("2026-10-01T07:00:00", "2026-10-01")]     // local midnight exactly
    [InlineData("2028-03-01T06:30:00", "2028-02-29")]     // leap day, 11:30 PM local
    public void ToBusinessDate_UsesBusinessTimeZone(string utc, string expected)
    {
        var instant = DateTime.SpecifyKind(DateTime.Parse(utc, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), Clock.ToBusinessDate(instant));
    }

    [Fact]
    public void DstAssumingOffset_MovesLateEveningPaymentToNextDay()
    {
        var instant = new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc); // 11:30 PM Sept 30 in Phoenix
        var naive = DateOnly.FromDateTime(instant.AddHours(-6));            // the Report B mistake

        Assert.Equal(new DateOnly(2026, 9, 30), Clock.ToBusinessDate(instant));
        Assert.Equal(new DateOnly(2026, 10, 1), naive);
    }

    [Fact]
    public void DstAssumingOffset_LeavesEarlyMorningPaymentAlone()
    {
        var instant = new DateTime(2026, 10, 1, 7, 30, 0, DateTimeKind.Utc); // 12:30 AM Oct 1 in Phoenix
        var naive = DateOnly.FromDateTime(instant.AddHours(-6));

        Assert.Equal(Clock.ToBusinessDate(instant), naive);
    }

    [Fact]
    public void ToBusinessDate_RejectsLocalKind()
    {
        Assert.Throws<ArgumentException>(() => Clock.ToBusinessDate(DateTime.Now));
    }
}
