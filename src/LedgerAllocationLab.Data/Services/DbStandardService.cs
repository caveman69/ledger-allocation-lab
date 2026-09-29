using Dapper.Contrib.Extensions;
using LedgerAllocationLab.Data.Models;
using Microsoft.Extensions.Logging;

namespace LedgerAllocationLab.Data.Services;

public class DbServiceBase(LedgerLabDapperDbContext context, ILogger logger)
{
    protected readonly LedgerLabDapperDbContext context = context ?? throw new ArgumentNullException(nameof(context));
    protected readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));
}

public class DbStandardService<T, TKey>(LedgerLabDapperDbContext context, ILogger<DbStandardService<T, TKey>> logger) : DbServiceBase(context, logger), IDbStandardService<T, TKey> where T : class, IEntity<TKey>, new()
{
    public async Task<bool> DeleteAsync(T entity)
    {
        logger.LogInformation("Deleting entity of type {EntityType} with ID {EntityId}", typeof(T).Name, entity.Id);
        using var connection = context.CreateConnection();
        var existingEntity = await connection.GetAsync<T>(entity.Id);
        if (existingEntity == null)
        {
            return false;
        }

        var result = await connection.DeleteAsync(entity);
        return result;
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        logger.LogInformation("Retrieving all entities of type {EntityType}", typeof(T).Name);
        using var connection = context.CreateConnection();
        var result = await connection.GetAllAsync<T>();
        return result;
    }

    public async Task<T?> GetByIdAsync(TKey id)
    {
        logger.LogInformation("Retrieving entity of type {EntityType} with ID {EntityId}", typeof(T).Name, id);
        if (id == null)
        {
            throw new ArgumentNullException(nameof(id));
        }
        using var connection = context.CreateConnection();
        var result = await connection.GetAsync<T>(id);
        return result;
    }

    public async Task<T> InsertAsync(T entity)
    {
        logger.LogInformation("Inserting entity of type {EntityType}", typeof(T).Name);
        using var connection = context.CreateConnection();
        var result = await connection.InsertAsync(entity);
        return entity;
    }
    public async Task<bool> UpdateAsync(T entity)
    {
        logger.LogInformation("Updating entity of type {EntityType} with ID {EntityId}", typeof(T).Name, entity.Id);
        using var connection = context.CreateConnection();
        var result = await connection.UpdateAsync(entity);
        return result;
    }

}
