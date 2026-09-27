namespace LedgerAllocationLab.Data.Models;

[Dapper.Contrib.Extensions.Table("Payments")]
public class Payment : IEntity<long>
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
