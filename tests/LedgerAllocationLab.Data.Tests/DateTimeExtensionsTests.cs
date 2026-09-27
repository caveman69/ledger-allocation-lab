using System.Globalization;
using LedgerAllocationLab.Data.Extensions;

namespace LedgerAllocationLab.Core.Tests;

public class DateTimeExtensionsTests
{
    [Theory]
    [InlineData("2026-10-01T06:59:59.9996", "2026-10-01T06:59:59.999")]    // would round up to midnight
    [InlineData("2026-10-01T06:59:59.9999999", "2026-10-01T06:59:59.999")] // last tick before midnight
    [InlineData("2026-10-01T07:00:00.0004", "2026-10-01T07:00:00.000")]    // just after midnight stays after
    [InlineData("2026-10-01T07:00:00", "2026-10-01T07:00:00")]             // whole millisecond is unchanged
    public void TruncateToMilliseconds_DropsSubMillisecondTicks(string input, string expected)
    {
        var value = ParseUtc(input);

        var result = value.TruncateToMilliseconds();

        Assert.Equal(ParseUtc(expected), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Utc);
    }
}
