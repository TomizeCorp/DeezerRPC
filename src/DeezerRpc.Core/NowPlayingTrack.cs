namespace DeezerRpc.Core;

public sealed record NowPlayingTrack
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string Album { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public TimeSpan Position { get; init; }
    public PlaybackStatus Status { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public Uri? CoverUrl { get; init; }
    public Uri? LocalCoverUri { get; init; }
    public Uri? TrackUrl { get; init; }
    public string SourceId { get; init; } = string.Empty;

    public bool IsUsable => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Artist);

    public TimeSpan ProjectPosition(DateTimeOffset now)
    {
        var projected = Position;
        if (Status == PlaybackStatus.Playing && now > ObservedAt)
        {
            projected += now - ObservedAt;
        }

        if (projected < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return Duration > TimeSpan.Zero && projected > Duration ? Duration : projected;
    }

    public string Identity => $"{Title.Trim()}\n{Artist.Trim()}\n{Album.Trim()}";
}
