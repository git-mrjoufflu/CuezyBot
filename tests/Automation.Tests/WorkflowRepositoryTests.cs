using System.Text.Json.Nodes;
using Automation.Contracts.Persistence;
using Automation.Domain.Workflows;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Tests;

public sealed class WorkflowRepositoryTests : IAsyncLifetime
{
    private readonly string applicationDirectory = Path.Combine(
        Path.GetTempPath(),
        $"workflow-tests-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(applicationDirectory))
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PublishedRevision_DoesNotChangeWhenDraftChanges()
    {
        var database = new SqliteDatabase(new AppDataPaths(applicationDirectory));
        await database.InitializeAsync(TestContext.Current.CancellationToken);
        IWorkflowRepository repository = new SqliteWorkflowRepository(database);
        var workflowId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var published = CreateWorkflow(workflowId, triggerId, 1, "Première version");

        await repository.SaveDraftAsync(published, TestContext.Current.CancellationToken);
        await repository.PublishAsync(published, TestContext.Current.CancellationToken);
        await repository.SaveDraftAsync(
            CreateWorkflow(workflowId, triggerId, 2, "Brouillon modifié"),
            TestContext.Current.CancellationToken);

        var loadedDraft = await repository.GetDraftAsync(
            workflowId,
            TestContext.Current.CancellationToken);
        var loadedPublished = await repository.GetPublishedAsync(
            workflowId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(loadedDraft);
        Assert.Equal(2, loadedDraft.Revision);
        Assert.Equal("Brouillon modifié", loadedDraft.Name);
        Assert.NotNull(loadedPublished);
        Assert.Equal(1, loadedPublished.Revision);
        Assert.Equal("Première version", loadedPublished.Name);
    }

    private static WorkflowDefinition CreateWorkflow(
        Guid workflowId,
        Guid triggerId,
        int revision,
        string name)
    {
        return new WorkflowDefinition(
            workflowId,
            revision,
            name,
            true,
            [new WorkflowBlock(triggerId, "twitch.follow.received", 1, new JsonObject())],
            []);
    }
}
