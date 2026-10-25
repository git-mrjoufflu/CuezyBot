namespace Automation.Connectors.Twitch;

public sealed record TwitchUserToken(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);
