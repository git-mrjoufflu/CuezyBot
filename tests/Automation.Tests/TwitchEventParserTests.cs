using System.Text.Json.Nodes;
using Automation.Connectors.Twitch;

namespace Automation.Tests;

public sealed class TwitchEventParserTests
{
    [Fact]
    public void ParseFollow_PreservesMessageIdentityAndFollowerData()
    {
        var message = JsonNode.Parse("""
            {
              "metadata": {
                "message_id": "message-123",
                "message_type": "notification",
                "subscription_type": "channel.follow"
              },
              "payload": {
                "event": {
                  "user_id": "111",
                  "user_login": "personne_test",
                  "user_name": "PersonneTest",
                  "broadcaster_user_id": "222",
                  "followed_at": "2026-10-24T18:15:00Z"
                }
              }
            }
            """)!.AsObject();

        var follow = TwitchEventParser.ParseFollow(message);

        Assert.Equal("message-123", follow.MessageId);
        Assert.Equal("111", follow.UserId);
        Assert.Equal("personne_test", follow.UserLogin);
        Assert.Equal("222", follow.BroadcasterUserId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T18:15:00Z"), follow.FollowedAt);
    }
}
