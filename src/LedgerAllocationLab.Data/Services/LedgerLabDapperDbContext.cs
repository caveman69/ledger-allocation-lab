using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace LedgerAllocationLab.Data.Services;

public class LedgerLabDapperDbContext
{
    private readonly string _connectionString;

    public LedgerLabDapperDbContext(IOptions<LedgerLabDbOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is null or empty. Please provide a valid value for the connection string");
        }
        _connectionString = connectionString;

    }

    public DbConnection CreateConnection()
        => new SqlConnection(_connectionString);

}
