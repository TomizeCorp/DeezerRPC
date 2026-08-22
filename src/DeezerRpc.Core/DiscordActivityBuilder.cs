namespace DeezerRpc.Core;

public sealed class DiscordActivityBuilder
{
    private const int TextLimit = 128;
    public const string DeezerMonochromeLogoUrl =
        "https://raw.githubusercontent.com/TomizeCorp/DeezerRPC/main/assets/discord-deezer-monochrome.png";

    public DiscordActivity Build(NowPlayingTrack track, DateTimeOffset now, PresenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        options ??= new PresenceOptions();
        if (!track.IsUsable)
        {
            throw new ArgumentException("A track title and artist are required.", nameof(track));
        }
        if (track.Status != PlaybackStatus.Playing)
        {
            throw new InvalidOperationException("Only a playing track can be published to Discord.");
        }

        var album = track.Album.Trim();
        var state = track.Artist.Trim();

        DiscordTimestamps? timestamps = null;
        if (options.ShowProgress && track.Duration > TimeSpan.Zero)
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

        string? largeImage = null;
        string? largeText = null;
        if (IsPublicHttps(track.CoverUrl))
        {
            largeImage = track.CoverUrl!.AbsoluteUri;
            largeText = Trim(
                options.ShowAlbum && !string.IsNullOrWhiteSpace(album)
                    ? album
                    : $"{track.Title.Trim()} — {track.Artist.Trim()}",
                TextLimit);
        }
        var assets = new DiscordAssets
        {
            LargeImage = largeImage,
            LargeText = largeText,
            SmallImage = DeezerMonochromeLogoUrl,
            SmallText = "Deezer"
        };

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
