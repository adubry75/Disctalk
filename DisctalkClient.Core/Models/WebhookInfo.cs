namespace DisctalkClient.Core.Models;

/// <summary>A channel webhook. <see cref="Token"/> is a secret: never log it or show it.</summary>
public sealed record WebhookInfo(ulong Id, ulong ChannelId, string Name, string? Token, ulong? ApplicationId)
{
    public bool CanExecute => !string.IsNullOrEmpty(Token);

    public override string ToString() => $"Webhook {Id} ({Name}) in {ChannelId}";
}

public sealed record OutgoingFile(string FileName, ReadOnlyMemory<byte> Content, string? ContentType = null);
