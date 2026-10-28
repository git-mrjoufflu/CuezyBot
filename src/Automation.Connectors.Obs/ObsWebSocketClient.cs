using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Automation.Contracts.Obs;

namespace Automation.Connectors.Obs;

public sealed class ObsWebSocketClient : IObsController, IAsyncDisposable
{
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> pendingRequests = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? receiveTask;

    public bool IsConnected => socket.State == WebSocketState.Open && receiveTask is { IsCompleted: false };

    public async Task ConnectAsync(
        Uri endpoint,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (socket.State != WebSocketState.None)
        {
            throw new InvalidOperationException("Cette instance du client OBS a déjà été utilisée.");
        }

        await socket.ConnectAsync(endpoint, cancellationToken);
        var hello = await ReceiveMessageAsync(socket, cancellationToken);
        EnsureOpcode(hello, 0, "Hello");

        var identifyData = new JsonObject { ["rpcVersion"] = 1 };
        var authentication = hello["d"]?["authentication"];
        if (authentication is not null)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("OBS demande un mot de passe pour cette connexion.");
            }

            identifyData["authentication"] = ObsAuthentication.ComputeResponse(
                password,
                authentication["salt"]?.GetValue<string>()
                    ?? throw new InvalidDataException("Le message Hello OBS ne contient pas de sel."),
                authentication["challenge"]?.GetValue<string>()
                    ?? throw new InvalidDataException("Le message Hello OBS ne contient pas de défi."));
        }

        await SendMessageAsync(new JsonObject
        {
            ["op"] = 1,
            ["d"] = identifyData
        }, cancellationToken);

        var identified = await ReceiveMessageAsync(socket, cancellationToken);
        EnsureOpcode(identified, 2, "Identified");
        receiveTask = ReceiveLoopAsync(lifetime.Token);
    }

    public async Task SetCurrentProgramSceneAsync(
        string connectionId,
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new ArgumentException("Le nom de scène OBS est obligatoire.", nameof(sceneName));
        }

        await SendRequestAsync(
            "SetCurrentProgramScene",
            new JsonObject { ["sceneName"] = sceneName },
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetSceneNamesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSceneList", new JsonObject(), TimeSpan.FromSeconds(10), cancellationToken);
        return response["responseData"]?["scenes"]?.AsArray().Select(scene => scene?["sceneName"]?.GetValue<string>())
            .OfType<string>().ToArray() ?? [];
    }

    public async Task<IReadOnlyList<string>> GetSourceNamesAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = sceneName }, TimeSpan.FromSeconds(10), cancellationToken);
        return response["responseData"]?["sceneItems"]?.AsArray().Select(item => item?["sourceName"]?.GetValue<string>())
            .OfType<string>().Distinct().ToArray() ?? [];
    }

    public async Task SetSourceVisibilityAsync(string scene, IReadOnlyCollection<string> sources, bool visible, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = scene }, TimeSpan.FromSeconds(10), cancellationToken);
        var items = response["responseData"]?["sceneItems"]?.AsArray() ?? throw new InvalidDataException("OBS sceneItems missing");
        // Resolve every name before applying anything, so a missing source cannot produce partial changes.
        var targets = sources.Select(source => items.Where(item => item?["sourceName"]?.GetValue<string>() == source)
            .Select(item => item!["sceneItemId"]!.GetValue<int>()).ToArray()).ToArray();
        if (targets.Any(ids => ids.Length == 0)) throw new InvalidOperationException("OBS: source introuvable dans la scène sélectionnée.");
        foreach (var id in targets.SelectMany(ids => ids).Distinct())
            await SendRequestAsync("SetSceneItemEnabled", new JsonObject { ["sceneName"] = scene, ["sceneItemId"] = id, ["sceneItemEnabled"] = visible }, TimeSpan.FromSeconds(10), cancellationToken);
    }

    public async Task StartReplayBufferAsync(CancellationToken cancellationToken = default) =>
        await SendRequestAsync("StartReplayBuffer", new JsonObject(), TimeSpan.FromSeconds(10), cancellationToken);

    public async Task ControlOutputAsync(string output, string operation, CancellationToken cancellationToken = default)
    {
        var request = (output, operation) switch
        {
            ("record", "start") => "StartRecord", ("record", "stop") => "StopRecord",
            ("record", "pause") => "PauseRecord", ("record", "resume") => "ResumeRecord",
            ("replay", "start") => "StartReplayBuffer", ("replay", "stop") => "StopReplayBuffer",
            ("replay", "save") => "SaveReplayBuffer",
            ("virtual-camera", "start") => "StartVirtualCam", ("virtual-camera", "stop") => "StopVirtualCam",
            _ => throw new ArgumentException("Unsupported OBS output operation.", nameof(operation))
        };
        await SendRequestAsync(request, new JsonObject(), TimeSpan.FromSeconds(10), cancellationToken);
    }

    public async Task<byte[]> GetScreenshotAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSourceScreenshot", new JsonObject
        {
            ["sourceName"] = sourceName, ["imageFormat"] = "png", ["imageWidth"] = 1280
        }, TimeSpan.FromSeconds(10), cancellationToken);
        var data = response["responseData"]?["imageData"]?.GetValue<string>() ?? throw new InvalidDataException("OBS screenshot missing.");
        const string prefix = "data:image/png;base64,";
        if (!data.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("OBS screenshot is not a PNG data URL.");
        return Convert.FromBase64String(data[prefix.Length..]);
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
                    "Arrêt du Runtime",
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
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        }

        FailPendingRequests(new ObjectDisposedException(nameof(ObsWebSocketClient)));
        socket.Dispose();
        sendGate.Dispose();
        lifetime.Dispose();
    }

    private async Task<JsonObject> SendRequestAsync(
        string requestType,
        JsonObject requestData,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("OBS n’est pas connecté.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Impossible de créer un identifiant de requête OBS unique.");
        }

        try
        {
            await SendMessageAsync(new JsonObject
            {
                ["op"] = 6,
                ["d"] = new JsonObject
                {
                    ["requestType"] = requestType,
                    ["requestId"] = requestId,
                    ["requestData"] = requestData
                }
            }, cancellationToken);

            var response = await completion.Task.WaitAsync(timeout, cancellationToken);
            var status = response["requestStatus"]
                ?? throw new InvalidDataException("La réponse OBS ne contient pas de statut.");
            if (status["result"]?.GetValue<bool>() != true)
            {
                var code = status["code"]?.GetValue<int>() ?? 0;
                var comment = status["comment"]?.GetValue<string>() ?? "OBS a refusé la requête.";
                throw new ObsRequestException(code, comment);
            }

            return response;
        }
        finally
        {
            pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(socket, cancellationToken);
                if (message["op"]?.GetValue<int>() != 7)
                {
                    continue;
                }

                var data = message["d"]?.AsObject()
                    ?? throw new InvalidDataException("La réponse OBS ne contient pas de données.");
                var requestId = data["requestId"]?.GetValue<string>();
                if (requestId is not null && pendingRequests.TryGetValue(requestId, out var completion))
                {
                    completion.TrySetResult(data);
                }
            }

            FailPendingRequests(new WebSocketException("La connexion OBS a été fermée."));
        }
        catch (Exception exception)
        {
            FailPendingRequests(exception);
            throw;
        }
    }

    private async Task SendMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await sendGate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            sendGate.Release();
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
                throw new WebSocketException("OBS a fermé la connexion pendant la réception.");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("OBS a envoyé un message dans un format non pris en charge.");
            }

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            if (stream.Length > MaximumMessageBytes)
            {
                throw new InvalidDataException("Le message OBS dépasse la taille maximale autorisée.");
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        var node = JsonNode.Parse(stream.ToArray());
        return node?.AsObject() ?? throw new JsonException("Le message OBS n’est pas un objet JSON.");
    }

    private static void EnsureOpcode(JsonObject message, int expected, string name)
    {
        var actual = message["op"]?.GetValue<int>();
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"OBS devait envoyer {name} (opcode {expected}), mais a envoyé l’opcode {actual?.ToString() ?? "absent"}.");
        }
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var pending in pendingRequests.Values)
        {
            pending.TrySetException(exception);
        }
    }
}
