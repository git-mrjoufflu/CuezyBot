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
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Twitch chat message failed ({(int)response.StatusCode}): {detail}", null, response.StatusCode);
        }
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
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Twitch clip creation failed ({(int)response.StatusCode}): {detail}", null, response.StatusCode);
        }
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

    public async Task<string> CreateCustomRewardAsync(TwitchIdentity identity, TwitchUserToken token, string title, int cost, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("channel:manage:redemptions")) throw new InvalidOperationException("Twitch: channel:manage:redemptions required. Reconnect Twitch.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 45) throw new ArgumentException("Reward title must contain 1–45 characters.", nameof(title));
        if (cost < 1 || cost > 1_000_000_000) throw new ArgumentOutOfRangeException(nameof(cost));
        using var request = Request(HttpMethod.Post, $"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(identity.UserId)}", identity, token);
        request.Content = JsonContent.Create(new { title, cost, prompt = "", is_enabled = true, is_user_input_required = false, should_redemptions_skip_request_queue = false });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return result?["data"]?[0]?["id"]?.ToString() ?? throw new InvalidOperationException("Twitch reward ID missing.");
    }

    public async Task<IReadOnlyList<(string Id, string Title, int Cost, int Cooldown)>> GetCustomRewardsAsync(TwitchIdentity identity, TwitchUserToken token, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("channel:read:redemptions") && !identity.Scopes.Contains("channel:manage:redemptions"))
            throw new InvalidOperationException("Twitch reward permission required. Reconnect Twitch.");
        using var request = Request(HttpMethod.Get, $"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(identity.UserId)}&only_manageable_rewards=true", identity, token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return result?["data"]?.AsArray().Select(item => (item?["id"]?.ToString() ?? "", item?["title"]?.ToString() ?? "", item?["cost"]?.GetValue<int>() ?? 0, item?["global_cooldown_seconds"]?.GetValue<int>() ?? 0))
            .Where(item => item.Item1.Length > 0 && item.Item2.Length > 0).ToArray() ?? [];
    }

    public async Task UpdateCustomRewardAsync(TwitchIdentity identity, TwitchUserToken token, string id, string title, int cost, int cooldownSeconds, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("channel:manage:redemptions")) throw new InvalidOperationException("Twitch reward management permission required. Reconnect Twitch.");
        using var request = Request(HttpMethod.Patch, $"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(identity.UserId)}&id={Uri.EscapeDataString(id)}", identity, token);
        request.Content = JsonContent.Create(new { title, cost, global_cooldown_seconds = Math.Max(0, cooldownSeconds) });
        using var response = await http.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task DeleteCustomRewardAsync(TwitchIdentity identity, TwitchUserToken token, string id, CancellationToken cancellationToken = default)
    {
        if (!identity.Scopes.Contains("channel:manage:redemptions")) throw new InvalidOperationException("Twitch reward management permission required. Reconnect Twitch.");
        using var request = Request(HttpMethod.Delete, $"channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(identity.UserId)}&id={Uri.EscapeDataString(id)}", identity, token);
        using var response = await http.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
    }
}
