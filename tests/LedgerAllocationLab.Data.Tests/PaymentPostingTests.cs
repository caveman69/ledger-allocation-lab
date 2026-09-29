using System.Globalization;
using Dapper;
using LedgerAllocationLab.Core;
using LedgerAllocationLab.Data.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace LedgerAllocationLab.Data.Tests;

public class PaymentPostingTests(ITestOutputHelper output) : LedgerDataTestsBase(output), IDisposable
{
    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        GC.SuppressFinalize(this);
    }    

    [SqlFact]
    public void Configuration_ReachesDbOptions()
    {
        var options = _provider.GetRequiredService<IOptions<LedgerLabDbOptions>>().Value;

        Assert.False(string.IsNullOrWhiteSpace(options.ConnectionString));
    }

    // ===== Posting =====

    [SqlFact]
    public async Task Post_WritesPaymentAndAllocationsThatSumToAmount()
    {
        decimal[] rates = [1.5000m, 0.7500m, 4.2000m];
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, rates);
        var key = Guid.NewGuid();

        var paymentId = await PostAsync(CreateService(), key, parcelId, 100_000, new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc));

        await using var conn = await OpenAsync();
        var row = await conn.QuerySingleAsync<(long Id, int ParcelId, short TaxYear, long AmountCents)>(
            $"SELECT Id, ParcelId, TaxYear, AmountCents FROM {PaymentTable} WHERE IdempotencyKey = @key", new { key });
        Assert.Equal(paymentId, row.Id);
        Assert.Equal(parcelId, row.ParcelId);
        Assert.Equal(TaxYear, row.TaxYear);
        Assert.Equal(100_000, row.AmountCents);

        var allocations = await GetAllocationsAsync(paymentId);
        Assert.Equal(100_000, allocations.Values.Sum());

        var expected = Allocator.Allocate(100_000, rates);
        for (var i = 0; i < districtIds.Length; i++)
        {
            Assert.Equal(expected[i], allocations[districtIds[i]]);
        }
    }

    [SqlFact]
    public async Task Post_LeftoverCentGoesToLowestDistrictId()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 1m, 1m);

        var paymentId = await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 100, DateTime.UtcNow);

        var allocations = await GetAllocationsAsync(paymentId);
        Assert.Equal(34, allocations[districtIds[0]]);
        Assert.Equal(33, allocations[districtIds[1]]);
        Assert.Equal(33, allocations[districtIds[2]]);
    }

    [SqlFact]
    public async Task Post_UsesRatesForRequestedTaxYearOnly()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(2025, 1m, 0m);
        await AddRatesAsync(parcelId, districtIds, 2026, 0m, 1m);

        var paymentId = await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 100, DateTime.UtcNow, taxYear: 2026);

        var allocations = await GetAllocationsAsync(paymentId);
        Assert.Equal(0, allocations[districtIds[0]]);
        Assert.Equal(100, allocations[districtIds[1]]);
    }

    [SqlTheory]
    [InlineData("2026-10-01T06:59:59.9996", "2026-10-01T06:59:59.999", "2026-09-30")]
    [InlineData("2026-10-01T06:59:59.9999999", "2026-10-01T06:59:59.999", "2026-09-30")]
    [InlineData("2026-10-01T07:00:00.0004", "2026-10-01T07:00:00.000", "2026-10-01")]
    public async Task Post_SubMillisecondInput_IsTruncatedBeforeStoringAndDating(
    string input, string expectedStoredUtc, string expectedBusinessDate)
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m);
        var key = Guid.NewGuid();
        var received = DateTime.SpecifyKind(DateTime.Parse(input, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        await PostAsync(CreateService(), key, parcelId, 500, received);

        await using var conn = await OpenAsync();
        var row = await conn.QuerySingleAsync<(DateTime ReceivedOnUtc, DateTime BusinessDate)>(
            $"SELECT ReceivedOnUtc, BusinessDate FROM {PaymentTable} WHERE IdempotencyKey = @key", new { key });
        var storedUtc = DateTime.SpecifyKind(row.ReceivedOnUtc, DateTimeKind.Utc);
        var storedBusinessDate = DateOnly.FromDateTime(row.BusinessDate);

        Assert.Equal(
            DateTime.SpecifyKind(DateTime.Parse(expectedStoredUtc, CultureInfo.InvariantCulture), DateTimeKind.Utc),
            storedUtc);
        Assert.Equal(DateOnly.Parse(expectedBusinessDate, CultureInfo.InvariantCulture), storedBusinessDate);

        // The row must agree with itself: its date is the business date of its own timestamp.
        Assert.Equal(BusinessDateClock.Phoenix().ToBusinessDate(storedUtc), storedBusinessDate);
    }

    [SqlTheory]
    [InlineData("2026-10-01T06:30:00", "2026-09-30")]     // 11:30 PM Sept 30 Phoenix
    [InlineData("2026-10-01T06:59:59.999", "2026-09-30")] // last millisecond of Sept 30
    [InlineData("2026-10-01T07:00:00", "2026-10-01")]     // local midnight
    [InlineData("2026-10-01T07:30:00", "2026-10-01")]     // 12:30 AM Oct 1 Phoenix
    public async Task Post_StoresUtcAndBusinessDate(string utc, string expectedBusinessDate)
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m);
        var key = Guid.NewGuid();
        var receivedOnUtc = DateTime.SpecifyKind(DateTime.Parse(utc, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        await PostAsync(CreateService(), key, parcelId, 500, receivedOnUtc);

        await using var conn = await OpenAsync();
        var row = await conn.QuerySingleAsync<(DateTime ReceivedOnUtc, DateTime BusinessDate)>(
            $"SELECT ReceivedOnUtc, BusinessDate FROM {PaymentTable} WHERE IdempotencyKey = @key", new { key });
        Assert.Equal(receivedOnUtc, DateTime.SpecifyKind(row.ReceivedOnUtc, DateTimeKind.Utc));
        Assert.Equal(DateOnly.Parse(expectedBusinessDate, CultureInfo.InvariantCulture), DateOnly.FromDateTime(row.BusinessDate));
    }

    [SqlFact]
    public async Task Post_StoredReceivedUtcAndBusinessDateAgree_AtSubMillisecondBoundary()
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m);
        var key = Guid.NewGuid();
        var received = new DateTime(2026, 10, 1, 6, 59, 59, DateTimeKind.Utc).AddTicks(9_996_000); // .9996

        await PostAsync(CreateService(), key, parcelId, 500, received);

        await using var conn = await OpenAsync();
        var row = await conn.QuerySingleAsync<(DateTime ReceivedOnUtc, DateTime BusinessDate)>(
            $"SELECT ReceivedOnUtc, BusinessDate FROM {PaymentTable} WHERE IdempotencyKey = @key", new { key });

        var clock = BusinessDateClock.Phoenix();
        Assert.Equal(
            clock.ToBusinessDate(DateTime.SpecifyKind(row.ReceivedOnUtc, DateTimeKind.Utc)),
            DateOnly.FromDateTime(row.BusinessDate));
        Assert.Equal(new DateOnly(2026, 9, 30), DateOnly.FromDateTime(row.BusinessDate));
    }

    // ===== Lookup =====

    [SqlFact]
    public async Task GetByIdempotencyKey_ReturnsPostedPayment()
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m);
        var key = Guid.NewGuid();
        var paymentId = await PostAsync(CreateService(), key, parcelId, 1_234, DateTime.UtcNow);

        var found = await CreateService().GetPaymentByIdempotencyKeyAsync(key);

        Assert.NotNull(found);
        Assert.Equal(paymentId, found.Id);
    }

    [SqlFact]
    public async Task GetByIdempotencyKey_UnknownKey_ReturnsNull()
    {
        var found = await CreateService().GetPaymentByIdempotencyKeyAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    // ===== Idempotency =====

    [SqlFact]
    public async Task Post_SameKeyTwice_WritesOnePaymentAndReturnsSameId()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 2m, 3m);
        var key = Guid.NewGuid();

        var first = await PostAsync(CreateService(), key, parcelId, 10_000, DateTime.UtcNow);
        var second = await PostAsync(CreateService(), key, parcelId, 10_000, DateTime.UtcNow);

        Assert.Equal(first, second);
        Assert.Equal(1, await CountPaymentsAsync(key));
        Assert.Equal(districtIds.Length, (await GetAllocationsAsync(first)).Count);
    }

    [SqlFact]
    public async Task Post_SameKeyConcurrently_WritesOnePaymentAndEveryCallerGetsIt()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 2m, 3m);
        const int rounds = 25;
        const int callers = 4;

        for (var round = 0; round < rounds; round++)
        {
            var key = Guid.NewGuid();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var posts = Enumerable.Range(0, callers).Select(async _ =>
            {
                var service = CreateService();
                await gate.Task;
                return await PostAsync(service, key, parcelId, 10_000, DateTime.UtcNow);
            }).ToArray();

            gate.SetResult();
            var ids = await Task.WhenAll(posts);

            Assert.All(ids, id => Assert.Equal(ids[0], id));
            Assert.Equal(1, await CountPaymentsAsync(key));
            var allocations = await GetAllocationsAsync(ids[0]);
            Assert.Equal(districtIds.Length, allocations.Count);
            Assert.Equal(10_000, allocations.Values.Sum());
        }
    }

    // ===== Atomicity =====

    [SqlFact]
    public async Task Post_WhenParcelHasNoRatesForYear_ThrowsAndLeavesNothing()
    {
        var (parcelId, _) = await SeedParcelAsync(2025, 1m, 1m);
        var key = Guid.NewGuid();

        await Assert.ThrowsAnyAsync<Exception>(() => PostAsync(CreateService(), key, parcelId, 1_000, DateTime.UtcNow, taxYear: 2026));

        Assert.Equal(0, await CountPaymentsAsync(key));
    }

    [SqlFact]
    public async Task Post_WhenAllRatesAreZero_ThrowsAndLeavesNothing()
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 0m, 0m);
        var key = Guid.NewGuid();

        await Assert.ThrowsAnyAsync<Exception>(() => PostAsync(CreateService(), key, parcelId, 1_000, DateTime.UtcNow));

        Assert.Equal(0, await CountPaymentsAsync(key));
    }

    [SqlFact]
    public async Task Post_NegativeAmount_ThrowsAndLeavesNothing()
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m, 1m);
        var key = Guid.NewGuid();

        await Assert.ThrowsAnyAsync<Exception>(() => PostAsync(CreateService(), key, parcelId, -1_000, DateTime.UtcNow));

        Assert.Equal(0, await CountPaymentsAsync(key));
    }

    // ===== Append-only =====

    [SqlFact]
    public async Task Payment_UpdateIsRefused()
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m);
        var paymentId = await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 700, DateTime.UtcNow);

        await using var conn = await OpenAsync();
        await Assert.ThrowsAsync<SqlException>(() =>
            conn.ExecuteAsync($"UPDATE {PaymentTable} SET AmountCents = 1 WHERE Id = @paymentId", new { paymentId }));

        Assert.Equal(700, await conn.ExecuteScalarAsync<long>(
            $"SELECT AmountCents FROM {PaymentTable} WHERE Id = @paymentId", new { paymentId }));
    }

    [SqlFact]
    public async Task Payment_DeleteIsRefused_ByTheTriggerNotAForeignKey()
    {
        // Inserted directly with no allocation rows, so a foreign key can't be what blocks the delete.
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1m);
        var key = Guid.NewGuid();
        await using var conn = await OpenAsync();
        var paymentId = await conn.ExecuteScalarAsync<long>(
            $"""
            INSERT INTO {PaymentTable} (IdempotencyKey, ParcelId, TaxYear, AmountCents, ReceivedOnUtc, BusinessDate)
            VALUES (@key, @parcelId, @TaxYear, 100, SYSUTCDATETIME(), CAST(SYSUTCDATETIME() AS DATE));
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """, new { key, parcelId, TaxYear });

        await Assert.ThrowsAsync<SqlException>(() =>
            conn.ExecuteAsync($"DELETE FROM {PaymentTable} WHERE Id = @paymentId", new { paymentId }));

        Assert.Equal(1, await CountPaymentsAsync(key));
    }

    [SqlFact]
    public async Task PaymentAllocation_UpdateIsRefused()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 1m);
        var paymentId = await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 1_000, DateTime.UtcNow);

        await using var conn = await OpenAsync();
        await Assert.ThrowsAsync<SqlException>(() =>
            conn.ExecuteAsync($"UPDATE {AllocationTable} SET AmountCents = AmountCents + 1 WHERE PaymentId = @paymentId", new { paymentId }));

        var allocations = await GetAllocationsAsync(paymentId);
        Assert.Equal(districtIds.Length, allocations.Count);
        Assert.Equal(1_000, allocations.Values.Sum());
    }

    [SqlFact]
    public async Task PaymentAllocation_DeleteIsRefused()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 1m);
        var paymentId = await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 1_000, DateTime.UtcNow);

        await using var conn = await OpenAsync();
        await Assert.ThrowsAsync<SqlException>(() =>
            conn.ExecuteAsync($"DELETE FROM {AllocationTable} WHERE PaymentId = @paymentId", new { paymentId }));

        Assert.Equal(districtIds.Length, (await GetAllocationsAsync(paymentId)).Count);
    }

    // ===== Uniqueness the old composite keys used to enforce =====

    [SqlFact]
    public async Task PaymentAllocation_SecondRowForSameDistrictIsRefused()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 1m);
        var paymentId = await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 1_000, DateTime.UtcNow);

        await using var conn = await OpenAsync();
        await Assert.ThrowsAsync<SqlException>(() =>
            conn.ExecuteAsync($"INSERT INTO {AllocationTable} (PaymentId, DistrictId, AmountCents) VALUES (@paymentId, @districtId, 0)",
                new { paymentId, districtId = districtIds[0] }));
    }

    [SqlFact]
    public async Task ParcelDistrictRate_SecondRateForSameDistrictAndYearIsRefused()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m);

        await Assert.ThrowsAsync<SqlException>(() => AddRatesAsync(parcelId, districtIds, TaxYear, 2m));
    }

    private static async Task<int> CountPaymentsAsync(Guid key)
    {
        await using var conn = await OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {PaymentTable} WHERE IdempotencyKey = @key", new { key });
    }

    private static async Task<Dictionary<int, long>> GetAllocationsAsync(long paymentId)
    {
        await using var conn = await OpenAsync();
        var rows = await conn.QueryAsync<(int DistrictId, long AmountCents)>(
            $"SELECT DistrictId, AmountCents FROM {AllocationTable} WHERE PaymentId = @paymentId", new { paymentId });
        return rows.ToDictionary(r => r.DistrictId, r => r.AmountCents);
    }
}
