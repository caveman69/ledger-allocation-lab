using Dapper.Contrib.Extensions;
using LedgerAllocationLab.Data.Models;
using Microsoft.Extensions.Logging;

namespace LedgerAllocationLab.Data.Services;

public class BaseDbService<T, TKey> : IDbStandardService<T, TKey> where T : class, IEntity<TKey>, new()
{
    private readonly LedgerLabDapperDbContext _context;
    private readonly ILogger<BaseDbService<T, TKey>> _logger;

    public BaseDbService(LedgerLabDapperDbContext context, ILogger<BaseDbService<T, TKey>> logger)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<bool> DeleteAsync(T entity)
    {
        _logger.LogInformation("Deleting entity of type {EntityType} with ID {EntityId}", typeof(T).Name, entity.Id);
        using var connection = _context.CreateConnection();
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
        _logger.LogInformation("Retrieving all entities of type {EntityType}", typeof(T).Name);
        using var connection = _context.CreateConnection();
        var result = await connection.GetAllAsync<T>();
        return result;
    }

    public async Task<T?> GetByIdAsync(TKey id)
    {
        _logger.LogInformation("Retrieving entity of type {EntityType} with ID {EntityId}", typeof(T).Name, id);
        if (id == null)
        {
            throw new ArgumentNullException(nameof(id));
        }
        using var connection = _context.CreateConnection();
        var result = await connection.GetAsync<T>(id);
        return result;
    }

    public async Task<T> InsertAsync(T entity)
    {
        _logger.LogInformation("Inserting entity of type {EntityType}", typeof(T).Name);
        using var connection = _context.CreateConnection();
        var result = await connection.InsertAsync(entity);
        return entity;
    }
    public async Task<bool> UpdateAsync(T entity)
    {
        _logger.LogInformation("Updating entity of type {EntityType} with ID {EntityId}", typeof(T).Name, entity.Id);
        using var connection = _context.CreateConnection();
        var result = await connection.UpdateAsync(entity);
        return result;
    }

}
