using Dapper;
using Dapper.Contrib.Extensions;
using LedgerAllocationLab.Core;
using LedgerAllocationLab.Data.Exceptions;
using LedgerAllocationLab.Data.Extensions;
using LedgerAllocationLab.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace LedgerAllocationLab.Data.Services;

public interface IPaymentsDbService
{
    public Task<Payment?> GetPaymentByIdempotencyKeyAsync(Guid idempotencyKey);
    public Task<Payment> PostPaymentAsync(Guid idempotencyKey, int parcelId, short taxYear, long amountCents, DateTime receivedUtc);

}

public class PaymentsDbService(LedgerLabDapperDbContext context, IDbReadService<Payment, long> baseDbService, IParcelDistrictRateService parcelDistrictRateService, ILogger<PaymentsDbService> logger) : DbServiceBase(context, logger), IPaymentsDbService
{
    private static bool IsIdempotencyKeyViolation(SqlException ex) =>
    (ex.Number == 2627 || ex.Number == 2601)
    && ex.Message.Contains("UQ_Payment_IdempotencyKey", StringComparison.Ordinal);

    public Task<IEnumerable<Payment>> GetAllAsync() => baseDbService.GetAllAsync();
    public Task<Payment?> GetByIdAsync(long id) => baseDbService.GetByIdAsync(id);

    public async Task<Payment?> GetPaymentByIdempotencyKeyAsync(Guid idempotencyKey)
    {
        logger.LogInformation("Fetching payment with idempotencyKey: {IdempotencyKey}", idempotencyKey);
        using var connection = context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<Payment>("SELECT * FROM Payments WHERE IdempotencyKey = @IdempotencyKey", new { IdempotencyKey = idempotencyKey });

        return result;
    }

    /// <summary>
    /// Posts a payment to the database, allocating it to districts based on parcel district rates. This method is idempotent and will return the existing payment if a payment with the same idempotency key already exists.
    /// </summary>
    /// <param name="idempotencyKey"></param>
    /// <param name="parcelId"></param>
    /// <param name="taxYear"></param>
    /// <param name="amountCents"></param>
    /// <param name="receivedOnUtc">Date the payment was received in UTC - this comes from the caller (a processor or a lockbox timestamp). BusinessDate is derived from it</param>
    /// <returns></returns>
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
            await transaction.CommitAsync();
        } catch (SqlException sqlEx) when (IsIdempotencyKeyViolation(sqlEx))
        {
            // Handle idempotency violation
            await transaction.RollbackAsync();

            var existing = await GetPaymentByIdempotencyKeyAsync(idempotencyKey);
            if (existing is null)
            {
                // The key collided but the row isn't there. Don't guess; surface it.
                logger.LogError(sqlEx, "Idempotency violation but no payment found for {IdempotencyKey}", idempotencyKey);
                throw;   // rethrows the original SqlException, stack intact
            }
            if (existing.ParcelId != parcelId || existing.TaxYear != taxYear || existing.AmountCents != amountCents)
            {
                logger.LogError(sqlEx, LogTemplates.IdempotencyKeyConflictExceptionTemplate, idempotencyKey, existing.ParcelId, parcelId, existing.TaxYear, taxYear, existing.AmountCents, amountCents);
                throw new IdempotencyKeyConflictException(sqlEx, LogTemplates.IdempotencyKeyConflictExceptionTemplate, idempotencyKey, existing.ParcelId, parcelId, existing.TaxYear, taxYear, existing.AmountCents, amountCents);
            }
            logger.LogWarning("Replay of {IdempotencyKey}; returning existing payment {PaymentId}", idempotencyKey, existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
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
