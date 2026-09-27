using LedgerAllocationLab.Data.Models;
namespace LedgerAllocationLab.Data.Services;

public interface IDbReadService<T, TKey> where T : class, IEntity<TKey>
{
    public Task<IEnumerable<T>> GetAllAsync();
    public Task<T?> GetByIdAsync(TKey id);
}

public interface IDbInsertService<T, TKey> where T : class, IEntity<TKey>
{
    public Task<T> InsertAsync(T entity);
}

public interface IDbUpdateService<T, TKey> where T : class, IEntity<TKey>
{
    public Task<bool> UpdateAsync(T entity);
}

public interface IDbDeleteService<T, TKey> where T : class, IEntity<TKey>
{
    public Task<bool> DeleteAsync(T entity);
}

public interface IDbStandardService<T, TKey> : IDbReadService<T, TKey>, IDbInsertService<T, TKey>, IDbUpdateService<T, TKey>, IDbDeleteService<T, TKey> where T : class, IEntity<TKey>
{
}
