namespace Automation.Desktop;

internal static class AutoReconnect
{
    internal static bool ObsEnabled { get; set; }
    internal static bool TwitchEnabled { get; set; }
    internal static bool ObsPaused { get; set; }
    internal static bool TwitchPaused { get; set; }
    private static bool running;

    internal static async Task LoadAsync()
    {
        ObsEnabled = await ConnectionPersistence.GetAutoReconnectAsync(false);
        TwitchEnabled = await ConnectionPersistence.GetAutoReconnectAsync(true);
    }

    internal static async Task RetryAsync(Func<bool, bool>? connected = null, Func<bool, Task>? reconnect = null)
    {
        if (running) return;
        running = true;
        try
        {
            connected ??= twitch => twitch ? TwitchSessionState.EventSubClient?.IsConnected == true : ObsSessionState.Client?.IsConnected == true;
            reconnect ??= async twitch => { await ConnectionPersistence.ReconnectAsync(twitch); };
            if (ObsEnabled && !ObsPaused && !connected(false)) await reconnect(false);
            if (TwitchEnabled && !TwitchPaused && !connected(true)) await reconnect(true);
        }
        finally { running = false; }
    }
}
