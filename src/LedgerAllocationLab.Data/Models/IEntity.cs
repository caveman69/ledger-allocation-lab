namespace LedgerAllocationLab.Data.Models;

public interface IEntity<TKey>
{
    public TKey Id { get; }
}
