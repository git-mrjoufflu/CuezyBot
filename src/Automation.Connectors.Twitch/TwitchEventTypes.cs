namespace Automation.Connectors.Twitch;

/// EventSub types supported by the shared subscription router.
public static class TwitchEventTypes
{
    public const string Follow = "channel.follow";
    public const string Subscribe = "channel.subscribe";
    public const string Resubscribe = "channel.subscription.message";
    public const string GiftSubscription = "channel.subscription.gift";
    public const string Cheer = "channel.cheer";
    public const string Raid = "channel.raid";
    public const string ChannelPoints = "channel.channel_points_custom_reward_redemption.add";
    public const string StreamOnline = "stream.online";
    public const string StreamOffline = "stream.offline";
    public const string ChannelUpdate = "channel.update";
}
