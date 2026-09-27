namespace LedgerAllocationLab.Core;

public sealed class BusinessDateClock
{
    private readonly TimeZoneInfo _zone;

    public BusinessDateClock(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zone = zone;
    }

    public static BusinessDateClock Phoenix()
    {
        return new BusinessDateClock(TimeZoneInfo.FindSystemTimeZoneById("America/Phoenix"));
    }

    public DateOnly ToBusinessDate(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Expected a UTC timestamp.", nameof(utc));
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
        return DateOnly.FromDateTime(local);
    }
}
