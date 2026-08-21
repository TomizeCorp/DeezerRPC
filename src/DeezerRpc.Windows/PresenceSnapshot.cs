using DeezerRpc.Core;

namespace DeezerRpc.Windows;

internal sealed record PresenceSnapshot
{
    public string StatusText { get; init; } = "Initialisation…";
    public NowPlayingTrack? Track { get; init; }
    public bool DiscordConnected { get; init; }
    public bool DeezerDetected => Track is not null && Track.Status != PlaybackStatus.Stopped;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
