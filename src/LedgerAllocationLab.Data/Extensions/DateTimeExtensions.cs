namespace LedgerAllocationLab.Data.Extensions;

public static class DateTimeExtensions
{
    public static DateTime TruncateToMilliseconds(this DateTime dateTime)
    {
        return dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerMillisecond));
    }   
}
