using LedgerAllocationLab.Database;

internal class Program
{
    private static int Main(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? Environment.GetEnvironmentVariable("LEDGERLAB_SQL");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.Error.WriteLine("Pass a connection string or set LEDGERLAB_SQL.");
            return 1;
        }

        var result = LedgerMigrator.Run(connectionString);
        return result.Successful ? 0 : 1;
    }
}
