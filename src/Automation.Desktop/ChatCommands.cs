using System.Text.Json;
using System.Text.Json.Nodes;
using Automation.Persistence.Sqlite.Storage;
using Automation.Persistence.Sqlite.Stores;

namespace Automation.Desktop;

internal static class ChatCommands
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static async Task<List<JsonObject>> ListAsync()
    {
        var database = new SqliteDatabase(new AppDataPaths());
        await database.InitializeAsync();
        var json = await new SqliteApplicationSettingsStore(database).GetAsync("twitch.commands");
        return json is null ? [] : JsonSerializer.Deserialize<List<JsonObject>>(json) ?? [];
    }
    internal static async Task SaveAsync(JsonObject command, bool delete = false)
    {
        await Gate.WaitAsync();
        try
        {
            var commands = await ListAsync();
            var id = command["id"]?.ToString();
            commands.RemoveAll(item => item["id"]?.ToString() == id);
            if (!delete) commands.Add((JsonObject)command.DeepClone());
            var database = new SqliteDatabase(new AppDataPaths());
            await database.InitializeAsync();
            await new SqliteApplicationSettingsStore(database).SetAsync("twitch.commands", JsonSerializer.Serialize(commands));
        }
        finally { Gate.Release(); }
    }
}
