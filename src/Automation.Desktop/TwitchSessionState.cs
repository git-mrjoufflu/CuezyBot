using Automation.Connectors.Twitch;

namespace Automation.Desktop;

internal static class TwitchSessionState
{
    internal static TwitchUserToken? Token { get; set; }

    internal static TwitchIdentity? Identity { get; set; }

    internal static TwitchEventSubClient? EventSubClient { get; set; }
}
