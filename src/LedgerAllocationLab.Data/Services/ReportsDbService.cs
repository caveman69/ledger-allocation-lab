using LedgerAllocationLab.Data.Models.ReportModels;
using Microsoft.Extensions.Logging;

namespace LedgerAllocationLab.Data.Services;

public interface IReportsDbService
{
    // Report A: sums stored allocation rows. The correct one.
    public Task<IReadOnlyList<DistrictDayTotal>> CollectionsByDistrictAsync(DateOnly start, DateOnly endExclusive);

    // Report B: recomputes from rates. Deliberately defective; see SPEC section 9.
    public Task<IReadOnlyList<DistrictDayTotal>> CollectionsByDistrictRecomputedAsync(DateOnly start, DateOnly endExclusive);

    // sum(allocations) - sum(payments) for the range. Must always be zero.
    public Task<long> ControlTotalDifferenceAsync(DateOnly start, DateOnly endExclusive);
}

public class ReportsDbService(LedgerLabDapperDbContext context, ILogger<ReportsDbService> logger) : DbServiceBase(context, logger), IReportsDbService
{
    public async Task<IReadOnlyList<DistrictDayTotal>> CollectionsByDistrictAsync(DateOnly start, DateOnly endExclusive)
    {
        throw new NotImplementedException("This method is not implemented yet. It should return a report of sum stored allocation rows.");
    }
    public async Task<IReadOnlyList<DistrictDayTotal>> CollectionsByDistrictRecomputedAsync(DateOnly start, DateOnly endExclusive)
    {
        throw new NotImplementedException("This method is not implemented yet. It should return a report of recomputed district day totals.");
    }
    public async Task<long> ControlTotalDifferenceAsync(DateOnly start, DateOnly endExclusive)
    {
       throw new NotImplementedException("This method is not implemented yet. It should return a report of the difference between sum of allocations and sum of payments for the given date range.");
    }
}
