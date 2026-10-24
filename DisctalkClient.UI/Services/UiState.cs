using DisctalkClient.Core.Models;

namespace DisctalkClient.UI.Services;

/// <summary>Per-window view state: what's selected and which panels/overlays are open.</summary>
public sealed class UiState
{
    public ulong? GuildId { get; private set; }
    public ulong? ChannelId { get; private set; }

    /// <summary>Phone layout: the channel drawer (left) and member drawer (right).</summary>
    public bool ChannelDrawerOpen { get; private set; }
    public bool MemberDrawerOpen { get; private set; }

    /// <summary>Desktop layout: member list column visible.</summary>
    public bool MemberListVisible { get; private set; } = true;

    public bool SettingsOpen { get; private set; }
    public ChatMessage? ReplyingTo { get; private set; }
    public ChatMessage? Editing { get; private set; }
    public string? LightboxUrl { get; private set; }
    public string? Toast { get; private set; }

    public event Action? Changed;

    public void SelectGuild(ulong? guildId)
    {
        if (GuildId == guildId) return;
        GuildId = guildId;
        ChannelId = null;
        Notify();
    }

    public void SelectChannel(ulong guildId, ulong channelId)
    {
        GuildId = guildId;
        ChannelId = channelId;
        ChannelDrawerOpen = false;
        ReplyingTo = null;
        Editing = null;
        Notify();
    }

    public void ToggleChannelDrawer() { ChannelDrawerOpen = !ChannelDrawerOpen; MemberDrawerOpen = false; Notify(); }
    public void ToggleMemberDrawer() { MemberDrawerOpen = !MemberDrawerOpen; ChannelDrawerOpen = false; Notify(); }
    public void ToggleMemberList() { MemberListVisible = !MemberListVisible; Notify(); }
    public void CloseDrawers() { if (!ChannelDrawerOpen && !MemberDrawerOpen) return; ChannelDrawerOpen = MemberDrawerOpen = false; Notify(); }

    public void OpenSettings() { SettingsOpen = true; ChannelDrawerOpen = MemberDrawerOpen = false; Notify(); }
    public void CloseSettings() { SettingsOpen = false; Notify(); }

    public void StartReply(ChatMessage message) { ReplyingTo = message; Editing = null; Notify(); }
    public void StartEdit(ChatMessage message) { Editing = message; ReplyingTo = null; Notify(); }
    public void CancelComposerMode() { if (ReplyingTo is null && Editing is null) return; ReplyingTo = null; Editing = null; Notify(); }

    public void ShowImage(string url) { LightboxUrl = url; Notify(); }
    public void CloseImage() { LightboxUrl = null; Notify(); }

    public void ShowToast(string? message) { Toast = message; Notify(); }

    void Notify() => Changed?.Invoke();
}
