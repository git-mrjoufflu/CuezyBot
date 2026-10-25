namespace Automation.Connectors.Twitch;

public sealed record TwitchDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAt,
    TimeSpan PollInterval,
    IReadOnlyList<string> Scopes);
