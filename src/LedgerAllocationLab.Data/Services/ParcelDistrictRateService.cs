using LedgerAllocationLab.Data.Models;
using Microsoft.Extensions.Logging;
using Dapper;

namespace LedgerAllocationLab.Data.Services;

public interface IParcelDistrictRateService : IDbStandardService<ParcelDistrictRate, long>
{
    public Task<IReadOnlyCollection<ParcelDistrictRate>> GetParcelDistrictRatesForTaxYear(long parcelId, short taxYear);
}

public class ParcelDistrictRateService(LedgerLabDapperDbContext context, IDbStandardService<ParcelDistrictRate, long> baseDbService, ILogger<ParcelDistrictRateService> logger) : IParcelDistrictRateService
{
    public Task<IEnumerable<ParcelDistrictRate>> GetAllAsync() => baseDbService.GetAllAsync();
    public Task<ParcelDistrictRate?> GetByIdAsync(long id) => baseDbService.GetByIdAsync(id);
    public Task<bool> DeleteAsync(ParcelDistrictRate entity) => baseDbService.DeleteAsync(entity);
    public Task<ParcelDistrictRate> InsertAsync(ParcelDistrictRate entity) => baseDbService.InsertAsync(entity);
    public Task<bool> UpdateAsync(ParcelDistrictRate entity) => baseDbService.UpdateAsync(entity);

    public async Task<IReadOnlyCollection<ParcelDistrictRate>> GetParcelDistrictRatesForTaxYear(long parcelId, short taxYear)
    {
        logger.LogInformation("Fetching ParcelDistrictRates for ParcelId: {ParcelId}, TaxYear: {TaxYear}", parcelId, taxYear);
        const string sql = @"
        SELECT * FROM ParcelDistrictRates 
        WHERE TaxYear = @TaxYear AND ParcelId = @ParcelId
        ORDER BY DistrictId";
        using var connection = context.CreateConnection();
        var result = await connection.QueryAsync<ParcelDistrictRate>(sql, new {taxYear, parcelId});
        return result.ToList().AsReadOnly(); // Fully materialized before returning to avoid deferred execution issues
    }
}
