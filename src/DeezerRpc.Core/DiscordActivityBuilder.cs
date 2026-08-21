namespace DeezerRpc.Core;

public sealed class DiscordActivityBuilder
{
    private const int TextLimit = 128;

    public DiscordActivity Build(NowPlayingTrack track, DateTimeOffset now, PresenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        options ??= new PresenceOptions();
        if (!track.IsUsable)
        {
            throw new ArgumentException("A track title and artist are required.", nameof(track));
        }

        var album = track.Album.Trim();
        var state = track.Artist.Trim();
        if (track.Status == PlaybackStatus.Paused && options.ShowPauseState)
        {
            state += " • En pause";
        }

        DiscordTimestamps? timestamps = null;
        if (options.ShowProgress && track.Status == PlaybackStatus.Playing && track.Duration > TimeSpan.Zero)
        {
            var position = track.ProjectPosition(now);
            var start = now - position;
            var end = start + track.Duration;
            timestamps = new DiscordTimestamps
            {
                Start = start.ToUnixTimeSeconds(),
                End = end.ToUnixTimeSeconds()
            };
        }

        DiscordAssets? assets = null;
        if (IsPublicHttps(track.CoverUrl))
        {
            assets = new DiscordAssets
            {
                LargeImage = track.CoverUrl!.AbsoluteUri,
                LargeText = Trim(
                    options.ShowAlbum && !string.IsNullOrWhiteSpace(album)
                        ? album
                        : $"{track.Title.Trim()} — {track.Artist.Trim()}",
                    TextLimit)
            };
        }

        IReadOnlyList<DiscordButton>? buttons = null;
        if (options.ShowDeezerButton && IsPublicHttps(track.TrackUrl))
        {
            buttons =
            [
                new DiscordButton
                {
                    Label = "Écouter sur Deezer",
                    Url = track.TrackUrl!.AbsoluteUri
                }
            ];
        }

        return new DiscordActivity
        {
            Details = Trim(track.Title.Trim(), TextLimit),
            State = Trim(state, TextLimit),
            Timestamps = timestamps,
            Assets = assets,
            Buttons = buttons
        };
    }

    private static bool IsPublicHttps(Uri? uri) =>
        uri is { Scheme: "https", IsLoopback: false } && !string.IsNullOrWhiteSpace(uri.Host);

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : string.Concat(value.AsSpan(0, maximum - 1), "…");
}
