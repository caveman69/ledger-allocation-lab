namespace LedgerAllocationLab.Data.Models;

public class ParcelDistrictRate 
{
    public long Id { get; set; }
    public int ParcelId { get; set; }
    public int DistrictId { get; set; }
    public short TaxYear { get; set; }
    public decimal Rate { get; set; }
}
