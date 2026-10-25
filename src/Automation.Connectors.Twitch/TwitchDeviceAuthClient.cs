using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automation.Connectors.Twitch;

public sealed class TwitchDeviceAuthClient : IDisposable
{
    private static readonly Uri DeviceEndpoint = new("https://id.twitch.tv/oauth2/device");
    private static readonly Uri TokenEndpoint = new("https://id.twitch.tv/oauth2/token");
    private static readonly Uri ValidateEndpoint = new("https://id.twitch.tv/oauth2/validate");
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public TwitchDeviceAuthClient(HttpClient? httpClient = null)
    {
        ownsHttpClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task<TwitchDeviceAuthorization> StartAsync(
        string clientId,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        if (scopes.Count == 0 || scopes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Au moins une permission Twitch est requise.", nameof(scopes));
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scopes"] = string.Join(' ', scopes)
        });
        using var response = await httpClient.PostAsync(DeviceEndpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<DeviceAuthorizationResponse>(response, cancellationToken);

        return new TwitchDeviceAuthorization(
            payload.DeviceCode,
            payload.UserCode,
            new Uri(payload.VerificationUri),
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
            TimeSpan.FromSeconds(Math.Max(payload.Interval, 1)),
            scopes.ToArray());
    }

    public async Task<TwitchUserToken> WaitForAuthorizationAsync(
        string clientId,
        TwitchDeviceAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        var interval = authorization.PollInterval;

        while (DateTimeOffset.UtcNow < authorization.ExpiresAt)
        {
            await Task.Delay(interval, cancellationToken);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = string.Join(' ', authorization.Scopes),
                ["device_code"] = authorization.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });
            using var response = await httpClient.PostAsync(TokenEndpoint, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var payload = await DeserializeAsync<TokenResponse>(response, cancellationToken);
                return new TwitchUserToken(
                    payload.AccessToken,
                    payload.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
                    payload.Scope);
            }

            var error = await TryReadErrorAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest && error == "authorization_pending")
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest && error == "slow_down")
            {
                interval += TimeSpan.FromSeconds(5);
                continue;
            }

            throw new InvalidOperationException(
                $"Twitch a refusé l’autorisation : {error ?? response.ReasonPhrase ?? "erreur inconnue"}.");
        }

        throw new TimeoutException("Le code Twitch a expiré avant la fin de l’autorisation.");
    }

    public async Task<TwitchIdentity> ValidateAsync(
        TwitchUserToken token,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ValidateEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token.AccessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<ValidateResponse>(response, cancellationToken);

        return new TwitchIdentity(
            payload.UserId,
            payload.Login,
            payload.ClientId,
            payload.Scopes,
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn));
    }

    public async Task<TwitchUserToken> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Le jeton de renouvellement Twitch est obligatoire.", nameof(refreshToken));
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        using var response = await httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<TokenResponse>(response, cancellationToken);
        return new TwitchUserToken(
            payload.AccessToken,
            payload.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
            payload.Scope);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Le Client ID Twitch est obligatoire.", nameof(clientId));
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await TryReadErrorAsync(response, cancellationToken);
        throw new InvalidOperationException(
            $"Twitch a refusé la demande : {error ?? response.ReasonPhrase ?? "erreur inconnue"}.");
    }

    private static async Task<string?> TryReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
            return payload?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        return payload ?? throw new InvalidDataException("Twitch a retourné une réponse vide.");
    }

    private sealed record DeviceAuthorizationResponse(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_uri")] string VerificationUri);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string[] Scope);

    private sealed record ValidateResponse(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("scopes")] string[] Scopes,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("message")] string Message);
}
