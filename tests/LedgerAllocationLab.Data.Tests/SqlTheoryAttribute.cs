namespace LedgerAllocationLab.Data.Tests;

public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlFactAttribute.EnvVar)))
        {
            Skip = $"{SqlFactAttribute.EnvVar} is not set.";
        }
    }
}
