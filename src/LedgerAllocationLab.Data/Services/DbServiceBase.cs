using Microsoft.Extensions.Logging;

namespace LedgerAllocationLab.Data.Services;

public class DbServiceBase(LedgerLabDapperDbContext context, ILogger logger)
{
    protected readonly LedgerLabDapperDbContext context = context ?? throw new ArgumentNullException(nameof(context));
    protected readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
