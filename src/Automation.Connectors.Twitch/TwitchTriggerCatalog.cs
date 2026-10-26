using System.Text.Json.Nodes;

namespace Automation.Connectors.Twitch;

public sealed record TwitchTrigger(string EventType, string Version, string Scope, string French, string English)
{
    public string BlockType => EventType == "channel.follow" ? "twitch.follow.received" : $"twitch.{EventType}.received";
    public JsonObject Condition(string userId) => EventType switch
    {
        "channel.follow" => new() { ["broadcaster_user_id"] = userId, ["moderator_user_id"] = userId },
        "channel.raid" => new() { ["to_broadcaster_user_id"] = userId },
        _ => new() { ["broadcaster_user_id"] = userId }
    };
}

public static class TwitchTriggerCatalog
{
    public static IReadOnlyList<TwitchTrigger> All { get; } = new TwitchTrigger[]
    {
        new("channel.follow", "2", "moderator:read:followers", "Nouveau follower Twitch", "New Twitch follower"),
        new("channel.subscribe", "1", "channel:read:subscriptions", "Nouvel abonnement", "New subscription"),
        new("channel.subscription.message", "1", "channel:read:subscriptions", "Réabonnement", "Resubscription"),
        new("channel.subscription.gift", "1", "channel:read:subscriptions", "Abonnements offerts", "Gift subscriptions"),
        new("channel.cheer", "1", "bits:read", "Cheer / Bits", "Cheer / Bits"),
        new("channel.raid", "1", "", "Raid reçu", "Incoming raid"),
        new("channel.channel_points_custom_reward_redemption.add", "1", "channel:read:redemptions", "Récompense de points de chaîne", "Channel points redemption"),
        new("stream.online", "1", "", "Début du live", "Stream online"),
        new("stream.offline", "1", "", "Fin du live", "Stream offline"),
        new("channel.update", "2", "", "Informations du live modifiées", "Channel updated")
    };
    public static TwitchTrigger? FindBlock(string type) => All.FirstOrDefault(item => item.BlockType == type);
    public static string Title(string type, bool english) => type == "manual.run"
        ? (english ? "Manual run" : "Exécution manuelle")
        : FindBlock(type) is { } item ? (english ? item.English : item.French) : type;
}

public sealed record TwitchNotification(string MessageId, string EventType, int Version, JsonObject Data);
