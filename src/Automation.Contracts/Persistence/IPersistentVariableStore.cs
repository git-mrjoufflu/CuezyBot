namespace Automation.Contracts.Persistence;

public interface IPersistentVariableStore
{
    Task<IReadOnlyList<PersistentVariable>> ListAsync(CancellationToken cancellationToken = default);
    Task SetIntegerAsync(string name, long value, CancellationToken cancellationToken = default);
    Task SetTextAsync(string name, string value, CancellationToken cancellationToken = default);
    Task<long> IncrementAsync(
        string name,
        long amount,
        CancellationToken cancellationToken = default);

    Task<long?> GetIntegerAsync(
        string name,
        CancellationToken cancellationToken = default);
    Task<string?> GetTextAsync(string name, CancellationToken cancellationToken = default);
    Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

public sealed record PersistentVariable(string Name, string Type, string Value, long Revision, DateTimeOffset UpdatedAt,
    string Scope = "system", string Lifetime = "permanent", string? ViewerId = null);
