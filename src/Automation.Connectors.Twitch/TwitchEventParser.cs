using System.Globalization;
using System.Text.Json.Nodes;

namespace Automation.Connectors.Twitch;

public static class TwitchEventParser
{
    public static TwitchFollowEvent ParseFollow(JsonObject message)
    {
        var metadata = message["metadata"]?.AsObject()
            ?? throw new InvalidDataException("La notification Twitch ne contient pas de métadonnées.");
        var payload = message["payload"]?.AsObject()
            ?? throw new InvalidDataException("La notification Twitch ne contient pas de payload.");
        var eventData = payload["event"]?.AsObject()
            ?? throw new InvalidDataException("La notification Twitch ne contient pas d’événement.");

        return new TwitchFollowEvent(
            RequiredString(metadata, "message_id"),
            RequiredString(eventData, "user_id"),
            RequiredString(eventData, "user_login"),
            RequiredString(eventData, "user_name"),
            RequiredString(eventData, "broadcaster_user_id"),
            DateTimeOffset.Parse(
                RequiredString(eventData, "followed_at"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal));
    }

    private static string RequiredString(JsonObject source, string propertyName) =>
        source[propertyName]?.GetValue<string>()
        ?? throw new InvalidDataException($"Le champ Twitch « {propertyName} » est absent.");
}
