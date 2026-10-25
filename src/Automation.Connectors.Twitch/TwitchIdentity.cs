namespace Automation.Connectors.Twitch;

public sealed record TwitchIdentity(
    string UserId,
    string Login,
    string ClientId,
    IReadOnlyList<string> Scopes,
    DateTimeOffset ExpiresAt,
    string? ProfileImageUrl = null);
