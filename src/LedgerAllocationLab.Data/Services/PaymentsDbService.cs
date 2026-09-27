using Dapper;
using Dapper.Contrib.Extensions;
using LedgerAllocationLab.Core;
using LedgerAllocationLab.Data.Extensions;
using LedgerAllocationLab.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace LedgerAllocationLab.Data.Services;

public interface IPaymentsDbService : IDbReadService<Models.Payment, long>
{
    public Task<Payment?> GetPaymentByIdempotencyKeyAsync(Guid idempotencyKey);
    public Task<Payment> PostPaymentAsync(Guid idempotencyKey, int parcelId, short taxYear, long amountCents, DateTime receivedUtc);

}

public class PaymentsDbService(LedgerLabDapperDbContext context, IDbReadService<Payment, long> baseDbService, IParcelDistrictRateService parcelDistrictRateService, ILogger<PaymentsDbService> logger) : IPaymentsDbService
{
    public Task<IEnumerable<Payment>> GetAllAsync() => baseDbService.GetAllAsync();
    public Task<Payment?> GetByIdAsync(long id) => baseDbService.GetByIdAsync(id);

    public async Task<Payment?> GetPaymentByIdempotencyKeyAsync(Guid idempotencyKey)
    {
        logger.LogInformation("Fetching payment with idempotencyKey: {IdempotencyKey}", idempotencyKey);
        using var connection = context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<Payment>("SELECT * FROM Payments WHERE IdempotencyKey = @IdempotencyKey", new { IdempotencyKey = idempotencyKey });

        return result;
    }
    public async Task<Payment> PostPaymentAsync(Guid idempotencyKey, int parcelId, short taxYear, long amountCents, DateTime receivedOnUtc)
    {
        logger.LogInformation("Posting payment with idempotencyKey: {IdempotencyKey}, parcelId: {ParcelId}, taxYear: {TaxYear}, amountCents: {AmountCents}, receivedOnUtc: {ReceivedOnUtc}", idempotencyKey, parcelId, taxYear, amountCents, receivedOnUtc);

        //get business date from receivedOnUtc
        var businessDateClock = BusinessDateClock.Phoenix(); //load from configuration
        var received = receivedOnUtc.TruncateToMilliseconds();
        var localBusinessDate = businessDateClock.ToBusinessDate(received);

        //create a new payment object
        var payment = new Payment
        {
            IdempotencyKey = idempotencyKey,
            ParcelId = parcelId,
            TaxYear = taxYear,
            AmountCents = amountCents,
            ReceivedOnUtc = received,
            BusinessDate = localBusinessDate
        };

        //open transaction
        using var insertConn = context.CreateConnection();
        await insertConn.OpenAsync();
        using var transaction = await insertConn.BeginTransactionAsync();

        try
        { 
            //insert payment
            var result = await insertConn.InsertAsync(payment, transaction);

            //create the paymentallocations
            var allocations = await AllocatePayment(payment);

            //insert payment allocations
            //Dapper does these one at a time... it's okay for this example,
            //but for production, consider a bulk insert library like Dapper Plus or EF Core BulkExtensions
            var allocs = await insertConn.InsertAsync(allocations, transaction);

            //commit transaction
            transaction.Commit();
        } catch (SqlException sqlEx)
        {
            if (new[] { 2627, 2601 }.Contains(sqlEx.Number))
            {
                // Handle idempotency violation
                logger.LogWarning("Idempotency violation occurred while posting payment with idempotencyKey: {IdempotencyKey} returning existing payment.", idempotencyKey);
                transaction.Rollback();
                var existingPayment = await GetPaymentByIdempotencyKeyAsync(idempotencyKey);
                if (existingPayment != null)
                {
                    return existingPayment;
                }
                else {
                    logger.LogError("Idempotency violation occurred but no existing payment found for idempotencyKey: {IdempotencyKey}", idempotencyKey);
                }
            }
            transaction.Rollback();
            logger.LogError(sqlEx, "SQL error occurred while posting payment");
            throw;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            logger.LogError(ex, "Error occurred while posting payment");
            throw;
        }
        return payment;
    }

    private async Task<List<PaymentAllocation>> AllocatePayment(Payment payment)
    {
        // Implementation for allocating payment to districts
        //get all parceldistrictrates for the parcel and tax year
        var parcelDistrictRates = await parcelDistrictRateService.GetParcelDistrictRatesForTaxYear(payment.ParcelId, payment.TaxYear);
        //allocate
        var districtAllocations = Allocator.Allocate(payment.AmountCents, parcelDistrictRates.Select(r=>(r.DistrictId, r.Rate)).ToList());

        var allocations = new List<PaymentAllocation>();
        foreach (var alloc in districtAllocations)
        {
            allocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                DistrictId = alloc.Key,
                AmountCents = alloc.Cents
            });
        }
        return allocations;
    }   
}
