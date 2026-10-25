using System.Text.Json.Nodes;
using Automation.Connectors.Obs;
using Automation.Contracts.Execution;
using Automation.Contracts.Obs;
using Automation.Contracts.Persistence;
using Automation.Domain.Execution;
using Automation.Domain.Workflows;
using Automation.Engine.Blocks;
using Automation.Engine.Execution;
using Automation.Engine.Workflows;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

var applicationDirectory = ReadApplicationDirectoryOverride(args);
var paths = new AppDataPaths(applicationDirectory);
var database = new SqliteDatabase(paths);
await database.InitializeAsync();

var workflow = new WorkflowDefinition(
    PrototypeWorkflowIds.WorkflowId,
    1,
    "Remercier un nouveau follower",
    true,
    [
        new WorkflowBlock(PrototypeWorkflowIds.TriggerBlockId, "twitch.follow.received", 1, new JsonObject()),
        new WorkflowBlock(PrototypeWorkflowIds.CounterBlockId, "variables.increment", 1, new JsonObject
        {
            ["name"] = "followersTraites",
            ["amount"] = 1
        }),
        new WorkflowBlock(PrototypeWorkflowIds.ObsBlockId, "obs.scene.set-current", 1, new JsonObject
        {
            ["connectionId"] = "obs-principal",
            ["sceneName"] = "Merci"
        })
    ],
    [
        new WorkflowConnection(PrototypeWorkflowIds.TriggerBlockId, "next", PrototypeWorkflowIds.CounterBlockId),
        new WorkflowConnection(PrototypeWorkflowIds.CounterBlockId, "next", PrototypeWorkflowIds.ObsBlockId)
    ]);

var validation = new WorkflowValidator(new InitialBlockCatalog()).Validate(workflow);
if (!validation.IsValid)
{
    Console.Error.WriteLine("L’automatisation de démonstration est invalide :");
    foreach (var diagnostic in validation.Diagnostics)
    {
        Console.Error.WriteLine($"- {diagnostic.Code}: {diagnostic.Message}");
    }

    return 1;
}

var eventId = Guid.Parse("05a33017-58d3-421e-ab27-d7fdf572693f");
var receivedAt = DateTimeOffset.Parse(
    "2026-10-24T12:00:00-04:00",
    System.Globalization.CultureInfo.InvariantCulture);
var eventEnvelope = new EventEnvelope(
    eventId,
    "twitch",
    "twitch-principal",
    "synthetic-follow-0001",
    "channel.follow",
    2,
    receivedAt,
    new JsonObject { ["userName"] = "PersonneExemple" });
var request = new ExecutionRequest(
    Guid.Parse("939356be-6a15-45ed-9238-9d7e99ae41ea"),
    eventId,
    workflow.Id,
    workflow.Revision,
    receivedAt);

IEventAdmissionStore admissionStore = new SqliteEventAdmissionStore(database);
IPersistentVariableStore variableStore = new SqlitePersistentVariableStore(database);
IExecutionStore executionStore = new SqliteExecutionStore(database);
ObsWebSocketClient? liveObsClient = null;
IObsController obsController;
var obsUrl = Environment.GetEnvironmentVariable("AUTOMATION_OBS_URL");
if (Uri.TryCreate(obsUrl, UriKind.Absolute, out var obsEndpoint))
{
    liveObsClient = new ObsWebSocketClient();
    await liveObsClient.ConnectAsync(
        obsEndpoint,
        Environment.GetEnvironmentVariable("AUTOMATION_OBS_PASSWORD"));
    obsController = liveObsClient;
    Console.WriteLine($"OBS connecté : {obsEndpoint}");
}
else
{
    obsController = new PreviewObsController();
    Console.WriteLine("OBS en mode simulation. Définissez AUTOMATION_OBS_URL pour un essai réel.");
}

var admission = await admissionStore.TryAdmitAsync(eventEnvelope, [request]);

Console.WriteLine($"Automatisation : {workflow.Name}");
Console.WriteLine($"Base locale : {paths.DatabasePath}");

Console.WriteLine(admission == EventAdmissionResult.Admitted
    ? "Événement admis et exécution placée dans la file durable."
    : "Événement déjà traité : aucune nouvelle exécution créée.");

var executionStatus = await new PrototypeExecutionRunner(
    executionStore,
    variableStore,
    obsController).RunNextAsync();

if (executionStatus is not null)
{
    Console.WriteLine($"Exécution terminée : {executionStatus}.");
}

var counter = await variableStore.GetIntegerAsync("followersTraites") ?? 0;
Console.WriteLine($"Compteur followersTraites : {counter}");

if (liveObsClient is not null)
{
    await liveObsClient.DisposeAsync();
}

return 0;

static string? ReadApplicationDirectoryOverride(string[] arguments)
{
    const string option = "--application-directory";
    for (var index = 0; index < arguments.Length; index++)
    {
        if (arguments[index].Equals(option, StringComparison.Ordinal) && index + 1 < arguments.Length)
        {
            return arguments[index + 1];
        }
    }

    return null;
}

file sealed class PreviewObsController : IObsController
{
    public Task SetCurrentProgramSceneAsync(
        string connectionId,
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Simulation OBS ({connectionId}) : sélectionner la scène « {sceneName} ».");
        return Task.CompletedTask;
    }
}
