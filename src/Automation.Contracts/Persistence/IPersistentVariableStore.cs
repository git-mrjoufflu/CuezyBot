namespace Automation.Contracts.Persistence;

public interface IPersistentVariableStore
{
    Task<long> IncrementAsync(
        string name,
        long amount,
        CancellationToken cancellationToken = default);

    Task<long?> GetIntegerAsync(
        string name,
        CancellationToken cancellationToken = default);
}
