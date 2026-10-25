using Automation.Application.Follows;
using Automation.Connectors.Twitch;

namespace Automation.Desktop;

internal static class AutomationSession
{
    internal static event EventHandler<FollowProcessedEvent>? FollowProcessed;

    internal static async Task ConnectTwitchAsync(
        TwitchUserToken token,
        TwitchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (TwitchSessionState.EventSubClient is not null)
        {
            await TwitchSessionState.EventSubClient.DisposeAsync();
        }

        var eventSub = new TwitchEventSubClient();
        eventSub.FollowReceived += OnFollowReceived;
        await eventSub.ConnectAndSubscribeToFollowsAsync(
            TwitchAppConfiguration.ClientId,
            token,
            identity,
            cancellationToken);
        TwitchSessionState.Token = token;
        TwitchSessionState.Identity = identity;
        TwitchSessionState.EventSubClient = eventSub;
    }

    private static async void OnFollowReceived(object? sender, TwitchFollowEvent follow)
    {
        try
        {
            var result = await PrototypeServices.ProcessFollowAsync(follow);
            FollowProcessed?.Invoke(null, new FollowProcessedEvent(follow, result, null));
        }
        catch (Exception exception)
        {
            FollowProcessed?.Invoke(null, new FollowProcessedEvent(follow, null, exception.Message));
        }
    }
}

internal sealed record FollowProcessedEvent(
    TwitchFollowEvent Follow,
    FollowerProcessingResult? Result,
    string? Error);
