using System.Data.Common;
using Dapper;
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

public class PaymentsDbService(LedgerLabDapperDbContext context, ILogger<PaymentsDbService> logger, BusinessDateClock businessDateClock) : DbServiceBase(context, logger), IPaymentsDbService
{
    private static bool IsIdempotencyKeyViolation(SqlException ex) =>
    (ex.Number == 2627 || ex.Number == 2601)
    && ex.Message.Contains("UQ_Payment_IdempotencyKey", StringComparison.Ordinal);

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
        using var conn = context.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        try
        {
            //insert payment
            const string insertPayment = """
                INSERT INTO dbo.Payments (IdempotencyKey, ParcelId, TaxYear, AmountCents, ReceivedOnUtc, BusinessDate) 
                OUTPUT INSERTED.Id
                VALUES (@IdempotencyKey, @ParcelId, @TaxYear, @AmountCents, @ReceivedOnUtc, @BusinessDate); 
                """;
            payment.Id = await conn.ExecuteScalarAsync<long>(insertPayment, payment, tx);

            var rates = await LoadRatesAsync(conn, tx, parcelId, taxYear);
            var split = Allocator.Allocate(amountCents, rates.Select(r => (r.DistrictId, r.Rate)).ToList());

            const string insertAllocation = """
                INSERT INTO dbo.PaymentAllocations (PaymentId, DistrictId, AmountCents)
                VALUES (@PaymentId, @DistrictId, @AmountCents); 
                """;
            await conn.ExecuteAsync(insertAllocation,
                split.Select(s => new { PaymentId = payment.Id, DistrictId = s.Key, AmountCents = s.Cents }), tx);

            //commit transaction
            await tx.CommitAsync();
        } catch (SqlException sqlEx) when (IsIdempotencyKeyViolation(sqlEx))
        {
            // Handle idempotency violation
            await tx.RollbackAsync();

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
            await tx.RollbackAsync();
            logger.LogError(ex, "Error occurred while posting payment");
            throw;
        }
        return payment;
    }

    private static async Task<IReadOnlyList<(int DistrictId, decimal Rate)>> LoadRatesAsync(
    DbConnection conn, DbTransaction tx, int parcelId, short taxYear)
    {
        const string sql = """
        SELECT DistrictId, Rate
        FROM dbo.ParcelDistrictRates
        WHERE ParcelId = @parcelId AND TaxYear = @taxYear
        ORDER BY DistrictId;
        """;
        var rows = await conn.QueryAsync<(int DistrictId, decimal Rate)>(sql, new { parcelId, taxYear }, tx);
        return rows.ToList();
    }
}
