namespace LedgerAllocationLab.Data.Models;

[Dapper.Contrib.Extensions.Table("PaymentAllocations")]
public class PaymentAllocation : IEntity<long>
{
    public long Id { get; set; }

    public long PaymentId { get; set; }

    public int DistrictId { get; set; }

    public long AmountCents { get; set; } // negative for a reversal
}
