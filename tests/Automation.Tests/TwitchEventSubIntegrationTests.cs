using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Automation.Connectors.Twitch;

namespace Automation.Tests;

public sealed class TwitchEventSubIntegrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubscribesAuthorizedEventsAndDispatchesNotifications(bool allScopes)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource<TwitchNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(token);
            using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"metadata\":{\"message_type\":\"session_welcome\"},\"payload\":{\"session\":{\"id\":\"fake-session\"}}}"), WebSocketMessageType.Text, true, token);
            await subscribed.Task.WaitAsync(token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"metadata\":{\"message_type\":\"notification\",\"message_id\":\"raid-123\",\"subscription_type\":\"channel.raid\",\"subscription_version\":\"1\"},\"payload\":{\"event\":{\"from_broadcaster_user_name\":\"Raider\",\"viewers\":42}}}"), WebSocketMessageType.Text, true, token);
            await received.Task.WaitAsync(token);
            var buffer = new byte[1024];
            try
            {
                var close = await socket.ReceiveAsync(buffer.AsMemory(), token);
                if (close.MessageType == WebSocketMessageType.Close) await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", token);
            }
            catch (WebSocketException) when (socket.State is WebSocketState.Aborted or WebSocketState.Closed) { }
        }, token);
        var handler = new SubscriptionHandler();
        var scopes = allScopes ? TwitchTriggerCatalog.All.Select(item => item.Scope).Where(scope => scope.Length > 0).Distinct().ToArray() : new[] { "moderator:read:followers" };
        await using (var client = new TwitchEventSubClient(new HttpClient(handler), new Uri($"ws://127.0.0.1:{port}/")))
        {
            client.NotificationReceived += (_, notification) => received.TrySetResult(notification);
            await client.ConnectAndSubscribeToFollowsAsync("client", new("fake-token", "fake-refresh", DateTimeOffset.UtcNow.AddHours(1), scopes), new("123", "broadcaster", "client", scopes, DateTimeOffset.UtcNow.AddHours(1)), token);
            subscribed.SetResult();
            var notification = await received.Task.WaitAsync(token);
            Assert.Equal("channel.raid", notification.EventType);
            Assert.Equal(42, notification.Data["viewers"]!.GetValue<int>());
            var expected = TwitchTriggerCatalog.All.Where(item => item.Scope.Length == 0 || scopes.Contains(item.Scope)).Select(item => item.EventType);
            Assert.Equal(expected.Order(), handler.Requests.Select(item => item["type"]!.ToString()).Order());
            Assert.All(handler.Requests, item => Assert.Equal("fake-session", item["transport"]!["session_id"]!.ToString()));
        }
        await server;
    }

    private sealed class SubscriptionHandler : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
