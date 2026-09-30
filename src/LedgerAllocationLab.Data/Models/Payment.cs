namespace LedgerAllocationLab.Data.Models;

public class Payment
{
    public long Id { get; set; }
    
    public Guid IdempotencyKey { get; set; }

    public int ParcelId { get; set; }

    public short TaxYear { get; set; }

    public long AmountCents { get; set; } // negative for a reversal

    public long? ReversesPaymentId { get; set; }

    public DateTime ReceivedOnUtc { get; set; }

    public DateOnly BusinessDate { get; set; }  
}
