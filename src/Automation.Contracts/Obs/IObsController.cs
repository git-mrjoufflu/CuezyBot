namespace Automation.Contracts.Obs;

public interface IObsController
{
    Task SetCurrentProgramSceneAsync(
        string connectionId,
        string sceneName,
        CancellationToken cancellationToken = default);
}
