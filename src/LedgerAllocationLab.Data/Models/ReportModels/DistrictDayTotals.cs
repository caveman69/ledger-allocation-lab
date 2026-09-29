namespace LedgerAllocationLab.Data.Models.ReportModels;

public sealed class DistrictDayTotal
{
    public DateOnly BusinessDate { get; init; }
    public int DistrictId { get; init; }
    public long AmountCents { get; init; }
    public int PaymentCount { get; init; }
}
