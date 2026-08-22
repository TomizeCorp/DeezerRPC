namespace DeezerRpc.Windows;

internal sealed record DiscordAccountProfile
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
}
