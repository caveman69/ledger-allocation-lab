using Dapper;
using LedgerAllocationLab.Data.Extensions;
using LedgerAllocationLab.Data.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LedgerAllocationLab.Data.Tests;

public class LedgerDataTestsBase : IDisposable
{
    protected const string DistrictTable = "dbo.Districts";
    protected const string ParcelTable = "dbo.Parcels";
    protected const string RateTable = "dbo.ParcelDistrictRates";
    protected const string PaymentTable = "dbo.Payments";
    protected const string AllocationTable = "dbo.PaymentAllocations";
    protected const short TaxYear = 2026;
    protected readonly ServiceProvider _provider;
    protected readonly List<IServiceScope> _scopes = [];
    protected readonly Lock _scopesLock = new();

    protected string ConnectionString { get; }

    public LedgerDataTestsBase(LedgerDatabaseMigratorFixture db, ITestOutputHelper output)
    {
        ConnectionString = db.ConnectionString;
        _provider = BuildProvider(ConnectionString, output);
    }

    protected static ServiceProvider BuildProvider(string connectionString, ITestOutputHelper output)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use whatever key LedgerLabDapperDbContext reads.
                ["LedgerLabDb:ConnectionString"] = connectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging(builder => builder
                .AddProvider(new XunitLoggerProvider(output))
                .SetMinimumLevel(LogLevel.Debug))
            .RegisterLedgerAllocationLabDataServices()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    protected static async Task<long> PostAsync(IPaymentsDbService service, Guid key, int parcelId, long amountCents, DateTime receivedOnUtc, short taxYear = TaxYear)
    {
        var payment = await service.PostPaymentAsync(key, parcelId, taxYear, amountCents, receivedOnUtc);
        return payment.Id;
    }

    // ===== Helpers: seed and read through SQL, never through the code under test =====

    protected async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // Random IDs keep runs from colliding.
    protected static int NextIdBlock() => Random.Shared.Next(100_000_000, 2_000_000_000);

    protected async Task<(int ParcelId, int[] DistrictIds)> SeedParcelAsync(short taxYear, params decimal[] rates)
    {
        var parcelId = NextIdBlock();
        var districtIds = Enumerable.Range(NextIdBlock(), rates.Length).ToArray();

        await using var conn = await OpenAsync();

        // Highest ID first, so insert order and DistrictId order disagree.
        foreach (var districtId in districtIds.Reverse())
        {
            await conn.ExecuteAsync($"INSERT INTO {DistrictTable} (Id, Name) VALUES (@districtId, @name)",
                new { districtId, name = $"Test district {districtId}" });
        }

        await conn.ExecuteAsync($"INSERT INTO {ParcelTable} (Id, ParcelNumber) VALUES (@parcelId, @number)",
            new { parcelId, number = $"T{parcelId}" });

        await AddRatesAsync(parcelId, districtIds, taxYear, rates);
        return (parcelId, districtIds);
    }

    protected async Task AddRatesAsync(int parcelId, int[] districtIds, short taxYear, params decimal[] rates)
    {
        await using var conn = await OpenAsync();
        for (var i = rates.Length - 1; i >= 0; i--)
        {
            await conn.ExecuteAsync(
                $"INSERT INTO {RateTable} (ParcelId, DistrictId, TaxYear, Rate) VALUES (@parcelId, @districtId, @taxYear, @rate)",
                new { parcelId, districtId = districtIds[i], taxYear, rate = rates[i] });
        }
    }
 
    // One scope per simulated request, same as a web request in production.
    protected IPaymentsDbService CreateService()
    {
        var scope = _provider.CreateScope();
        lock (_scopesLock)
        {
            _scopes.Add(scope);
        }

        return scope.ServiceProvider.GetRequiredService<IPaymentsDbService>();
    }

    #region IDisposable Support - Cleanup

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }
        _provider.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}
