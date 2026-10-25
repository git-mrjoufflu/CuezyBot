namespace Automation.Contracts.Persistence;

public interface IApplicationSettingsStore
{
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
