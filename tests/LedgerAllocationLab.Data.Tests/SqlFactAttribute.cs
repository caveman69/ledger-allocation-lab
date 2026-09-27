namespace LedgerAllocationLab.Data.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public const string EnvVar = "LEDGERLAB_SQL";

    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"{EnvVar} is not set. Start the Docker SQL Server and set {EnvVar} to the lab database connection string.";
        }
    }
}
