namespace Automation.Connectors.Twitch;

public sealed record TwitchFollowEvent(
    string MessageId,
    string UserId,
    string UserLogin,
    string UserName,
    string BroadcasterUserId,
    DateTimeOffset FollowedAt);
