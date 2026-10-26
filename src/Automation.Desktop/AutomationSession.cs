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
        eventSub.NotificationReceived += OnNotificationReceived;
        try
        {
            await eventSub.ConnectAndSubscribeToFollowsAsync(TwitchAppConfiguration.ClientId, token, identity, cancellationToken);
        }
        catch
        {
            await eventSub.DisposeAsync();
            throw;
        }
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

    private static async void OnNotificationReceived(object? sender, TwitchNotification notification)
    {
        if (notification.EventType == "channel.follow") return;
        try { await PrototypeServices.ProcessNotificationAsync(notification); }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("EventSub {0}: {1}", notification.EventType, exception.Message);
        }
    }
}

internal sealed record FollowProcessedEvent(
    TwitchFollowEvent Follow,
    FollowerProcessingResult? Result,
    string? Error);
