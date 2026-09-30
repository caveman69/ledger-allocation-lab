namespace LedgerAllocationLab.Core;

public class BusinessDateClockOptions
{
    public const string SectionName = "BusinessDateClock";
    public string IanaTimeZone { get; set; } = string.Empty;
}
public sealed class BusinessDateClock
{
    private readonly TimeZoneInfo _zone;

    public BusinessDateClock(BusinessDateClockOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _zone = TimeZoneInfo.FindSystemTimeZoneById(options.IanaTimeZone);
    }

    public static BusinessDateClock GetBusinessDateClock(string ianaTimeZone)
    {
        return new BusinessDateClock(new BusinessDateClockOptions { IanaTimeZone = ianaTimeZone });
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
