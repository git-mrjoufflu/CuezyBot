using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Automation.Connectors.Twitch;

public sealed class TwitchChannelClient(HttpClient http)
{
    private static HttpRequestMessage Request(HttpMethod method, string path, TwitchIdentity identity, TwitchUserToken token)
    {
        var request = new HttpRequestMessage(method, "https://api.twitch.tv/helix/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Client-Id", identity.ClientId);
        return request;
    }

    public async Task SendMessageAsync(TwitchIdentity identity, TwitchUserToken token, string message, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("user:write:chat")) throw new InvalidOperationException("Twitch: user:write:chat required. Reconnect Twitch.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500) throw new ArgumentException("Chat message must contain 1–500 characters.", nameof(message));
        using var request = Request(HttpMethod.Post, "chat/messages", identity, token);
        request.Content = JsonContent.Create(new { broadcaster_id = identity.UserId, sender_id = identity.UserId, message });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        if (result?["data"]?[0]?["is_sent"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Twitch rejected the chat message.");
    }

    public async Task<string> GetUserAsync(TwitchIdentity identity, TwitchUserToken token, string login, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, "users?login=" + Uri.EscapeDataString(login.Trim().TrimStart('@')), identity, token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var user = result?["data"]?.AsArray().FirstOrDefault() ?? throw new InvalidOperationException("Twitch user not found.");
        return $"{user["display_name"]} · ID {user["id"]}";
    }

    public async Task<string> CreateClipAsync(TwitchIdentity identity, TwitchUserToken token, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("clips:edit")) throw new InvalidOperationException("Twitch: clips:edit required. Reconnect Twitch.");
        using var request = Request(HttpMethod.Post, "clips?broadcaster_id=" + Uri.EscapeDataString(identity.UserId), identity, token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var id = result?["data"]?[0]?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Twitch clip ID missing.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        do
        {
            using var check = Request(HttpMethod.Get, "clips?id=" + Uri.EscapeDataString(id), identity, token);
            using var checkedResponse = await http.SendAsync(check, cancellationToken);
            checkedResponse.EnsureSuccessStatusCode();
            var clips = await checkedResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            if (clips?["data"]?.AsArray().FirstOrDefault() is { } clip)
                return clip["url"]?.GetValue<string>() ?? id;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new TimeoutException("Twitch did not confirm clip creation within 60 seconds.");
    }

    public async Task SetTitleAsync(TwitchIdentity identity, TwitchUserToken token, string title, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("channel:manage:broadcast"))
            throw new InvalidOperationException("Twitch: channel:manage:broadcast required. Reconnect Twitch.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 140)
            throw new ArgumentException("Twitch title must contain 1–140 characters.", nameof(title));
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(identity.UserId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Client-Id", identity.ClientId);
        request.Content = JsonContent.Create(new { title });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Twitch title update failed ({(int)response.StatusCode}).", null, response.StatusCode);
    }
}
