using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Automation.Engine.Execution;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Desktop;

internal static class AutomationQueues
{
    private static readonly ConcurrentDictionary<string, AlertExecutionQueue> queues = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static AlertExecutionQueue Get(string name) => queues.GetOrAdd(name, _ => new AlertExecutionQueue());
    internal static async Task<AlertExecutionQueue> GetConfiguredAsync(string name)
    {
        var config = await LoadAsync(); var queue = Get(name);
        queue.SetInterval(TimeSpan.FromSeconds(config["queueOptions"]?[name]?["intervalSeconds"]?.GetValue<int>() ?? 0));
        queue.SetBlocking(config["queueOptions"]?[name]?["blocking"]?.GetValue<bool>() ?? true);
        return queue;
    }
    internal static async Task<JsonObject> LoadAsync()
    {
        var database = new SqliteDatabase(new AppDataPaths()); await database.InitializeAsync();
        var text = await new SqliteApplicationSettingsStore(database).GetAsync("automation.queues");
        return text is null ? new JsonObject { ["names"] = new JsonArray("Default"), ["workflows"] = new JsonObject() } : JsonNode.Parse(text)!.AsObject();
    }
    internal static async Task ChangeAsync(Action<JsonObject> change)
    {
        await gate.WaitAsync();
        try
        {
            var config = await LoadAsync(); ManagementCategories.RememberNames(config); change(config); ManagementCategories.RememberNames(config);
            var database = new SqliteDatabase(new AppDataPaths()); await database.InitializeAsync();
            await new SqliteApplicationSettingsStore(database).SetAsync("automation.queues", config.ToJsonString());
        }
        finally { gate.Release(); }
    }
    internal static async Task<string> ForWorkflowAsync(Guid id)
    {
        var config = await LoadAsync(); var name = config["workflows"]?[id.ToString()]?.ToString();
        return name is not null && config["names"]!.AsArray().Any(item => string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase)) ? name : "Default";
    }

    internal static async Task SaveQueueAsync(string? previousName, string name, string category, bool english, int intervalSeconds = 0, bool blocking = true)
    {
        if (intervalSeconds < 0 || intervalSeconds > 3600) throw new InvalidOperationException(english ? "Interval must be between 0 and 3600 seconds." : "Le délai doit être compris entre 0 et 3600 secondes.");
        if (name.Length is < 1 or > 48) throw new InvalidOperationException(english ? "Use a name of 1–48 characters." : "Utilise un nom de 1 à 48 caractères.");
        if (previousName == "Default" && name != "Default") throw new InvalidOperationException(english ? "Default cannot be renamed." : "Default ne peut pas être renommée.");
        await ChangeAsync(config =>
        {
            var names = config["names"]!.AsArray();
            if (names.Any(item => !string.Equals(item?.ToString(), previousName, StringComparison.OrdinalIgnoreCase) && string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(english ? "This queue already exists." : "Cette file existe déjà.");
            if (previousName is not null)
            {
                var index = names.ToList().FindIndex(item => string.Equals(item?.ToString(), previousName, StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new InvalidOperationException(english ? "Queue not found." : "File introuvable.");
                names[index] = name;
                foreach (var pair in config["workflows"]!.AsObject().ToArray())
                    if (string.Equals(pair.Value?.ToString(), previousName, StringComparison.OrdinalIgnoreCase)) config["workflows"]![pair.Key] = name;
                config["queueCategories"]?.AsObject().Remove(previousName);
            }
            else names.Add(name);
            config["queueCategories"] ??= new JsonObject();
            config["queueOptions"] ??= new JsonObject();
            if (previousName is not null && previousName != name) config["queueOptions"]!.AsObject().Remove(previousName);
            config["queueOptions"]![name] = new JsonObject { ["intervalSeconds"] = intervalSeconds, ["blocking"] = blocking };
            if (category.Length > 0) config["queueCategories"]![name] = ManagementCategories.Names(config, "").FirstOrDefault(value => string.Equals(value, category, StringComparison.OrdinalIgnoreCase)) ?? category;
            else config["queueCategories"]!.AsObject().Remove(name);
        });
        if (previousName is not null) queues[name] = Get(previousName);
        Get(name).SetInterval(TimeSpan.FromSeconds(intervalSeconds));
        Get(name).SetBlocking(blocking);
    }

    internal static async Task DeleteQueueAsync(string name, bool english)
    {
        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(english ? "Keep the Default queue." : "La file Default doit être conservée.");
        var queue = Get(name);
        if (queue.IsRunning || queue.PendingCount > 0) throw new InvalidOperationException(english ? "Wait until this queue is empty before deleting it." : "Attends que cette file soit vide avant de la supprimer.");
        await ChangeAsync(config =>
        {
            var names = config["names"]!.AsArray();
            var item = names.FirstOrDefault(item => string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (item is not null) names.Remove(item);
            foreach (var pair in config["workflows"]!.AsObject().ToArray())
                if (string.Equals(pair.Value?.ToString(), name, StringComparison.OrdinalIgnoreCase)) config["workflows"]![pair.Key] = "Default";
            config["queueCategories"]?.AsObject().Remove(name);
        });
    }
}
