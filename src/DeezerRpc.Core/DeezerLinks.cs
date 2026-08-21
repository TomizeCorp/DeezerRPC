namespace DeezerRpc.Core;

public static class DeezerLinks
{
    public static Uri GetListenUri(NowPlayingTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.TrackUrl is { Scheme: "https" } direct &&
            direct.Host.EndsWith("deezer.com", StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        var query = Uri.EscapeDataString($"{track.Artist.Trim()} {track.Title.Trim()}");
        return new Uri($"https://www.deezer.com/search/{query}");
    }
}
