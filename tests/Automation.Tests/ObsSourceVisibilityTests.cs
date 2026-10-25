using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Automation.Connectors.Obs;

namespace Automation.Tests;

public sealed class ObsSourceVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Visibility_ResolvesSceneItemsAndAppliesOnlySelectedSources(bool missingSource)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var commands = new List<JsonObject>();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(token);
            using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            await Send(socket, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["rpcVersion"] = 1 } }, token);
            await Receive(socket, token);
            await Send(socket, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } }, token);
            while (socket.State == WebSocketState.Open)
            {
                var message = await Receive(socket, token);
                if (message is null) break;
                var data = message["d"]!.AsObject(); commands.Add((JsonObject)data.DeepClone());
                var type = data["requestType"]!.GetValue<string>();
                var payload = type switch
                {
                    "GetSourceScreenshot" => new JsonObject { ["imageData"] = "data:image/png;base64,AQID" },
                    "GetSceneList" => new JsonObject { ["scenes"] = new JsonArray(new JsonObject { ["sceneName"] = "Studio" }) },
                    "GetSceneItemList" => new JsonObject { ["sceneItems"] = new JsonArray(
                        new JsonObject { ["sourceName"] = "Camera", ["sceneItemId"] = 11 },
                        new JsonObject { ["sourceName"] = "Logo", ["sceneItemId"] = 22 },
                        new JsonObject { ["sourceName"] = "Music", ["sceneItemId"] = 33 }) },
                    _ => new JsonObject()
                };
                await Send(socket, new JsonObject { ["op"] = 7, ["d"] = new JsonObject
                {
                    ["requestId"] = data["requestId"]!.GetValue<string>(), ["requestType"] = type,
                    ["requestStatus"] = new JsonObject { ["result"] = true, ["code"] = 100 }, ["responseData"] = payload
                } }, token);
            }
        }, token);
        await using (var client = new ObsWebSocketClient())
        {
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), null, token);
            Assert.Equal(new[] { "Studio" }, await client.GetSceneNamesAsync(token));
            Assert.Equal(3, (await client.GetSourceNamesAsync("Studio", token)).Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, await client.GetScreenshotAsync("Studio", token));
            if (missingSource)
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetSourceVisibilityAsync("Studio", ["Camera", "Missing"], false, token));
            else await client.SetSourceVisibilityAsync("Studio", ["Camera", "Logo"], false, token);
        }
        await server;
        var changes = commands.Where(command => command["requestType"]!.GetValue<string>() == "SetSceneItemEnabled").ToArray();
        if (missingSource) Assert.Empty(changes);
        else
        {
            Assert.Equal(new[] { 11, 22 }, changes.Select(command => command["requestData"]!["sceneItemId"]!.GetValue<int>()));
            Assert.All(changes, command => Assert.False(command["requestData"]!["sceneItemEnabled"]!.GetValue<bool>()));
            Assert.All(changes, command => Assert.Equal("Studio", command["requestData"]!["sceneName"]!.GetValue<string>()));
        }
    }

    private static async Task Send(WebSocket socket, JsonObject data, CancellationToken token) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(data.ToJsonString()), WebSocketMessageType.Text, true, token);

    private static async Task<JsonObject?> Receive(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];
        ValueWebSocketReceiveResult response;
        try { response = await socket.ReceiveAsync(buffer.AsMemory(), token); }
        catch (WebSocketException) when (socket.State is WebSocketState.Aborted or WebSocketState.Closed) { return null; }
        if (response.MessageType == WebSocketMessageType.Close)
        {
            if (socket.State == WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", token);
            return null;
        }
        return JsonNode.Parse(buffer.AsSpan(0, response.Count))!.AsObject();
    }
}
