namespace LedgerAllocationLab.Data.Models;

public class PaymentAllocation 
{
    public long Id { get; set; }

    public long PaymentId { get; set; }

    public int DistrictId { get; set; }

    public long AmountCents { get; set; } // negative for a reversal
}
