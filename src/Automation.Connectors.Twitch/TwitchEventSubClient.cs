using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Automation.Connectors.Twitch;

public sealed class TwitchEventSubClient : IAsyncDisposable
{
    private static readonly Uri DefaultWebSocketEndpoint = new("wss://eventsub.wss.twitch.tv/ws");
    private static readonly Uri SubscriptionsEndpoint = new("https://api.twitch.tv/helix/eventsub/subscriptions");
    private const int MaximumMessageBytes = 1024 * 1024;
    private ClientWebSocket socket = new();
    private readonly HttpClient httpClient;
    private readonly Uri webSocketEndpoint;
    private readonly CancellationTokenSource lifetime = new();
    private Task? receiveTask;

    public event EventHandler<TwitchFollowEvent>? FollowReceived;
    public event EventHandler<TwitchNotification>? NotificationReceived;
    public List<string> SubscriptionErrors { get; } = new();

    public string? SessionId { get; private set; }

    public bool IsConnected => socket.State == WebSocketState.Open && receiveTask is { IsCompleted: false };

    public TwitchEventSubClient(HttpClient? httpClient = null, Uri? webSocketEndpoint = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.webSocketEndpoint = webSocketEndpoint ?? DefaultWebSocketEndpoint;
    }

    public async Task SubscribeAsync(string clientId, TwitchUserToken token, string type, string version, JsonObject condition, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SessionId) || !IsConnected) throw new InvalidOperationException("EventSub n’est pas connecté.");
        using var request = new HttpRequestMessage(HttpMethod.Post, SubscriptionsEndpoint);
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new { type, version, condition, transport = new { method = "websocket", session_id = SessionId } });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Twitch a refusé l’abonnement EventSub ({(int)response.StatusCode}).");
    }

    public async Task ConnectAndSubscribeToFollowsAsync(
        string clientId,
        TwitchUserToken token,
        TwitchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (socket.State != WebSocketState.None)
        {
            throw new InvalidOperationException("Cette instance EventSub a déjà été utilisée.");
        }

        await socket.ConnectAsync(webSocketEndpoint, cancellationToken);
        var welcome = await ReceiveMessageAsync(socket, cancellationToken);
        var messageType = welcome["metadata"]?["message_type"]?.GetValue<string>();
        if (!string.Equals(messageType, "session_welcome", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Twitch n’a pas envoyé le message session_welcome attendu.");
        }

        SessionId = welcome["payload"]?["session"]?["id"]?.GetValue<string>()
            ?? throw new InvalidDataException("Le message session_welcome ne contient pas de session_id.");
        receiveTask = ReceiveLoopAsync(lifetime.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, SubscriptionsEndpoint);
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new
        {
            type = "channel.follow",
            version = "2",
            condition = new
            {
                broadcaster_user_id = identity.UserId,
                moderator_user_id = identity.UserId
            },
            transport = new
            {
                method = "websocket",
                session_id = SessionId
            }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 500)
            {
                detail = detail[..500];
            }

            throw new InvalidOperationException(
                $"Twitch a refusé l’abonnement EventSub ({(int)response.StatusCode}) : {detail}");
        }
        foreach (var trigger in TwitchTriggerCatalog.All.Where(item => item.EventType != "channel.follow" && (item.Scope.Length == 0 || identity.Scopes.Contains(item.Scope))))
        {
            try { await SubscribeAsync(clientId, token, trigger.EventType, trigger.Version, trigger.Condition(identity.UserId), cancellationToken); }
            catch (HttpRequestException) { SubscriptionErrors.Add(trigger.EventType); }
            catch (InvalidOperationException) { SubscriptionErrors.Add(trigger.EventType); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Arrêt de CuezyBot",
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask;
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
            }
        }

        socket.Dispose();
        httpClient.Dispose();
        lifetime.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(socket, cancellationToken);
            var metadata = message["metadata"]?.AsObject();
            var type = metadata?["message_type"]?.GetValue<string>();

            if (string.Equals(type, "notification", StringComparison.Ordinal))
            {
                var notification = TwitchEventParser.ParseNotification(message);
                NotificationReceived?.Invoke(this, notification);
                if (notification.EventType == "channel.follow") FollowReceived?.Invoke(this, TwitchEventParser.ParseFollow(message));
            }
            else if (string.Equals(type, "revocation", StringComparison.Ordinal))
            {
                var revoked = message["payload"]?["subscription"]?["type"]?.ToString();
                if (revoked is not null && !SubscriptionErrors.Contains(revoked)) SubscriptionErrors.Add(revoked);
            }
            else if (string.Equals(type, "session_reconnect", StringComparison.Ordinal))
            {
                var reconnectUrl = message["payload"]?["session"]?["reconnect_url"]?.GetValue<string>()
                    ?? throw new InvalidDataException("Missing EventSub reconnect URL.");
                var endpoint = new Uri(reconnectUrl);
                if (endpoint.Scheme != "wss" || endpoint.Host != DefaultWebSocketEndpoint.Host)
                    throw new InvalidDataException("Invalid EventSub reconnect endpoint.");
                var replacement = new ClientWebSocket();
                try
                {
                    await replacement.ConnectAsync(endpoint, cancellationToken);
                    var welcome = await ReceiveMessageAsync(replacement, cancellationToken);
                    if (welcome["metadata"]?["message_type"]?.ToString() != "session_welcome") throw new InvalidDataException("Missing EventSub migration welcome.");
                    SessionId = welcome["payload"]?["session"]?["id"]?.ToString() ?? throw new InvalidDataException("Missing EventSub session.");
                    var previous = socket; socket = replacement; previous.Dispose();
                }
                catch { replacement.Dispose(); throw; }
            }
        }
    }

    private static async Task<JsonObject> ReceiveMessageAsync(
        ClientWebSocket webSocket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("Twitch a fermé la connexion EventSub.");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Twitch a envoyé un message EventSub non textuel.");
            }

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            if (stream.Length > MaximumMessageBytes)
            {
                throw new InvalidDataException("Le message EventSub dépasse la taille maximale autorisée.");
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return JsonNode.Parse(stream.ToArray())?.AsObject()
            ?? throw new InvalidDataException("Twitch a envoyé un message EventSub vide.");
    }
}
