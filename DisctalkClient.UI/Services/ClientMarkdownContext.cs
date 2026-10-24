using DisctalkClient.Core.Settings;
using DisctalkClient.Core.State;
using DisctalkClient.UI.Markdown;

namespace DisctalkClient.UI.Services;

/// <summary>Resolves markdown ids against the live <see cref="ChatClient"/> cache for one guild.</summary>
public sealed class ClientMarkdownContext(ChatClient client, TimestampFormatter time, ulong? guildId) : IMarkdownContext
{
    public string? UserName(ulong userId) =>
        guildId is ulong g ? client.GetMember(g, userId)?.DisplayName : null;

    public (string Name, string? Color)? Role(ulong roleId) =>
        guildId is ulong g && client.GetRole(g, roleId) is { } role ? (role.Name, role.CssColor) : null;

    public string? ChannelName(ulong channelId) => client.GetChannel(channelId)?.Name;

    public ulong? SelfUserId => client.CurrentUser?.Id;

    public string FormatTimestamp(long unixSeconds, char style) => time.FormatDiscordTimestamp(unixSeconds, style);

    public string FormatTimestampTooltip(long unixSeconds) => time.FormatFull(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
}
