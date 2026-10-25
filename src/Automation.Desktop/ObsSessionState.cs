using Automation.Connectors.Obs;

namespace Automation.Desktop;

internal static class ObsSessionState
{
    internal static ObsWebSocketClient? Client { get; set; }
}
