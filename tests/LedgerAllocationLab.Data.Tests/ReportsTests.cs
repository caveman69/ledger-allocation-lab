using System.Globalization;
using LedgerAllocationLab.Data.Models.ReportModels;
using LedgerAllocationLab.Data.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LedgerAllocationLab.Data.Tests;

[Collection(LedgerDatabaseCollection.Name)]
public class ReportsTests(LedgerDatabaseMigratorFixture db, ITestOutputHelper output) : LedgerDataTestsBase(db, output)
{
    [SqlFact]
    public async Task ReportA_SumsStoredAllocations_ByBusinessDateAndDistrict()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 1m, 1m);
        var service = CreateService();
        await PostAsync(service, Guid.NewGuid(), parcelId, 100, Utc("2026-09-30T18:00:00")); // Sept 30 local
        await PostAsync(service, Guid.NewGuid(), parcelId, 200, Utc("2026-10-01T06:30:00")); // 11:30 PM Sept 30 local
        await PostAsync(service, Guid.NewGuid(), parcelId, 300, Utc("2026-10-01T18:00:00")); // Oct 1 local

        var rows = Mine(await CreateReports().CollectionsByDistrictAsync(new(2026, 9, 30), new(2026, 10, 2)), districtIds);

        var sept30 = rows.Where(r => r.BusinessDate == new DateOnly(2026, 9, 30)).ToList();
        var oct1 = rows.Where(r => r.BusinessDate == new DateOnly(2026, 10, 1)).ToList();
        Assert.Equal(300, sept30.Sum(r => r.AmountCents));
        Assert.All(sept30, r => Assert.Equal(2, r.PaymentCount));
        Assert.Equal(300, oct1.Sum(r => r.AmountCents));
        Assert.All(oct1, r => Assert.Equal(1, r.PaymentCount));
    }

    [SqlFact]
    public async Task ReportB_PutsLateEveningPaymentOnNextDay()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m);
        await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 500, Utc("2026-10-01T06:30:00")); // 11:30 PM Sept 30 local

        var a = Mine(await CreateReports().CollectionsByDistrictAsync(new(2026, 9, 30), new(2026, 10, 2)), districtIds);
        var b = Mine(await CreateReports().CollectionsByDistrictRecomputedAsync(new(2026, 9, 30), new(2026, 10, 2)), districtIds);

        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(a).BusinessDate);
        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(b).BusinessDate);
    }

    [SqlFact]
    public async Task ReportB_RoundsEachLine_AndLosesTheLeftoverCent()
    {
        var (parcelId, districtIds) = await SeedParcelAsync(TaxYear, 1m, 1m, 1m);
        await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 100, Utc("2026-09-30T18:00:00"));

        var a = Mine(await CreateReports().CollectionsByDistrictAsync(new(2026, 9, 30), new(2026, 10, 1)), districtIds);
        var b = Mine(await CreateReports().CollectionsByDistrictRecomputedAsync(new(2026, 9, 30), new(2026, 10, 1)), districtIds);

        Assert.Equal(100, a.Sum(r => r.AmountCents));   // 34 + 33 + 33
        Assert.Equal(99, b.Sum(r => r.AmountCents));    // 33 + 33 + 33
    }

    [SqlFact]
    public async Task ControlTotal_IsZero_ForPostedPayments()
    {
        var (parcelId, _) = await SeedParcelAsync(TaxYear, 1.5m, 0.75m, 4.2m);
        await PostAsync(CreateService(), Guid.NewGuid(), parcelId, 100_000, Utc("2026-09-30T18:00:00"));

        Assert.Equal(0, await CreateReports().ControlTotalDifferenceAsync(new(2026, 9, 1), new(2026, 11, 1)));
    }

    // Helpers
    private IReportsDbService CreateReports()
    {
        var scope = _provider.CreateScope();
        lock (_scopesLock)
        {
            _scopes.Add(scope);
        }

        return scope.ServiceProvider.GetRequiredService<IReportsDbService>();
    }

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private static List<DistrictDayTotal> Mine(IEnumerable<DistrictDayTotal> rows, int[] districtIds) =>
        rows.Where(r => districtIds.Contains(r.DistrictId)).ToList();
}
