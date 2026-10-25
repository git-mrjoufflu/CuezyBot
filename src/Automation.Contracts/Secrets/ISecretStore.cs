namespace Automation.Contracts.Secrets;

public interface ISecretStore
{
    Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
