using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Helpers;

namespace LedgerAllocationLab.Database;

public static class LedgerMigrator
{
    // Folders that start with a digit get a "_" prefix in embedded resource
    // names, so Scripts/02_Migrations becomes "._02_Migrations." in the name.
    private const string MigrationsFolder = "._02_Migrations.";

    // Everything here is CREATE OR ALTER and runs on every deploy (triggers,
    // functions), so changing one means editing its file, not adding a script.
    private const string PostDeploymentFolder = "._04_PostDeployment.";

    public static DatabaseUpgradeResult Run(string connectionString, IUpgradeLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        log ??= new ConsoleUpgradeLog();
        var assembly = typeof(LedgerMigrator).Assembly;

        EnsureDatabase.For.SqlDatabase(connectionString, log);

        // Pass 1: tables and indexes. Journaled, so each script runs once.
        var migrations = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, name => name.Contains(MigrationsFolder, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogTo(log)
            .Build()
            .PerformUpgrade();

        if (!migrations.Successful)
        {
            return migrations;
        }

        // Pass 2: repeatable objects. Not journaled, so they run every time.
        return DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, name => name.Contains(PostDeploymentFolder, StringComparison.Ordinal))
            .JournalTo(new NullJournal())
            .LogTo(log)
            .Build()
            .PerformUpgrade();
    }
}
