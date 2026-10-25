using System.Net;
using Automation.Connectors.Twitch;

namespace Automation.Tests;

public sealed class TwitchChannelClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChatMessage_ChecksIsSentInsteadOfOnlyHttpSuccess(bool sent)
    {
        using var http = new HttpClient(new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"data\":[{\"is_sent\":" + (sent ? "true" : "false") + "}]}", System.Text.Encoding.UTF8, "application/json") }));
        var call = new TwitchChannelClient(http).SendMessageAsync(Identity(["user:write:chat"]), Token(), "Hello", TestContext.Current.CancellationToken);
        if (sent) await call;
        else await Assert.ThrowsAsync<InvalidOperationException>(() => call);
    }

    [Fact]
    public async Task ClipCreation_RequiresSuccessfulConfirmation()
    {
        using var http = new HttpClient(new ScriptedHandler(
            Json("{\"data\":[{\"id\":\"clip-42\"}]}"),
            Json("{\"data\":[{\"id\":\"clip-42\",\"url\":\"https://clips.twitch.tv/test\"}]}")));
        var url = await new TwitchChannelClient(http).CreateClipAsync(Identity(["clips:edit"]), Token(), TestContext.Current.CancellationToken);
        Assert.Equal("https://clips.twitch.tv/test", url);
    }

    [Fact]
    public async Task GetUser_ReportsMissingUser()
    {
        using var http = new HttpClient(new ScriptedHandler(Json("{\"data\":[]}")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TwitchChannelClient(http).GetUserAsync(Identity([]), Token(), "missing", TestContext.Current.CancellationToken));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> pending = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(pending.Dequeue());
    }

    [Fact]
    public async Task TitleRequest_UsesBroadcasterAndExpectedBody()
    {
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        await new TwitchChannelClient(http).SetTitleAsync(Identity(["channel:manage:broadcast"]), Token(), "A test title", TestContext.Current.CancellationToken);
        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Contains("broadcaster_id=42", handler.Address);
        Assert.Equal("{\"title\":\"A test title\"}", handler.Body);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MissingScope_RejectsBeforeNetwork()
    {
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TwitchChannelClient(http).SetTitleAsync(Identity([]), Token(), "Title", TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    private static TwitchIdentity Identity(string[] scopes) => new("42", "test", "client", scopes, DateTimeOffset.UtcNow.AddHours(1));
    private static TwitchUserToken Token() => new("test-token", "test-refresh", DateTimeOffset.UtcNow.AddHours(1), ["channel:manage:broadcast"]);
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls; public string Address = ""; public string Body = ""; public HttpMethod? Method;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Address = request.RequestUri!.ToString(); Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
