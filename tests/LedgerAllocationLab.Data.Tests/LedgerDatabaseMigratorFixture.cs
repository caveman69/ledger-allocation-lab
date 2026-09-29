using Dapper;
using DbUp.Engine.Output;
using LedgerAllocationLab.Database;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace LedgerAllocationLab.Data.Tests;

// One throwaway database per test run, built by the same migrator the README uses.
public sealed class LedgerDatabaseMigratorFixture(IMessageSink sink) : IAsyncLifetime
{
    private const string Prefix = "LedgerLab_Test_";
    private const string KeepEnvVar = "LEDGERLAB_KEEP_TEST_DB";

    private string? _masterConnectionString;

    public string ConnectionString { get; private set; } = string.Empty;

    public string DatabaseName { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(SqlFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return; // Every [SqlFact] skips, so there's nothing to build.
        }

        _masterConnectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await DropOrphansAsync();

        DatabaseName = $"{Prefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        ConnectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = DatabaseName }.ConnectionString;

        var result = LedgerMigrator.Run(ConnectionString, new NoOpUpgradeLog());
        if (!result.Successful)
        {
            throw new InvalidOperationException($"Migrating {DatabaseName} failed.", result.Error);
        }

        Log($"Test database: {DatabaseName}");
    }

    public async Task DisposeAsync()
    {
        if (_masterConnectionString is null || DatabaseName.Length == 0)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable(KeepEnvVar) == "1")
        {
            Log($"Kept {DatabaseName} for inspection ({KeepEnvVar}=1).");
            return;
        }

        SqlConnection.ClearAllPools(); // Pooled connections would block the drop.
        await DropAsync(DatabaseName);
    }

    // Databases left by runs that never reached DisposeAsync (a stopped debugger, a crash).
    // The age check keeps two runs at once from dropping each other's live database.
    private async Task DropOrphansAsync()
    {
        await using var conn = new SqlConnection(_masterConnectionString);
        var orphans = await conn.QueryAsync<string>(
            """
            SELECT name FROM sys.databases
            WHERE name LIKE 'LedgerLab[_]Test[_]%'
              AND create_date < DATEADD(HOUR, -1, GETDATE())
            """);

        foreach (var name in orphans)
        {
            await DropAsync(name);
            Log($"Dropped leftover test database {name}.");
        }
    }

    private async Task DropAsync(string name)
    {
        var quoted = new SqlCommandBuilder().QuoteIdentifier(name);
        await using var conn = new SqlConnection(_masterConnectionString);
        await conn.ExecuteAsync($"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};");
    }

    private void Log(string message) => sink.OnMessage(new DiagnosticMessage(message));
}

[CollectionDefinition(Name)]
public sealed class LedgerDatabaseCollection : ICollectionFixture<LedgerDatabaseMigratorFixture>
{
    public const string Name = "Ledger database";
}
