using DeezerRpc.Core;
using System.Security.Cryptography;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DeezerRpc.Windows;

internal sealed class GsmTcMediaSource
{
    private static readonly TimeSpan ArtworkRefreshInterval = TimeSpan.FromSeconds(2);
    private const int ArtworkFileLimit = 16;

    private static readonly string[] BrowserMarkers =
    [
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"
    ];

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly Dictionary<string, ArtworkCacheEntry> _artworkCache = new(StringComparer.Ordinal);

    public async Task<NowPlayingTrack?> GetCurrentAsync(bool includeBrowsers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        var candidates = new List<(NowPlayingTrack Track, int Score, DateTimeOffset Updated)>();
        foreach (var session in _manager.GetSessions())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = session.SourceAppUserModelId ?? string.Empty;
            var isDeezer = sourceId.Contains("deezer", StringComparison.OrdinalIgnoreCase);
            var isBrowser = includeBrowsers && BrowserMarkers.Any(
                marker => sourceId.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (!isDeezer && !isBrowser)
            {
                continue;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties properties;
            try
            {
                properties = await session.TryGetMediaPropertiesAsync();
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(properties.Title) || string.IsNullOrWhiteSpace(properties.Artist))
            {
                continue;
            }

            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo().PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
                _ => PlaybackStatus.Stopped
            };

            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            var observedAt = timeline.LastUpdatedTime == default
                ? DateTimeOffset.UtcNow
                : timeline.LastUpdatedTime;
            var album = properties.AlbumTitle?.Trim() ?? string.Empty;
            var localCover = await ReadThumbnailAsync(
                properties,
                $"{sourceId}\n{properties.Title.Trim()}\n{properties.Artist.Trim()}\n{album}",
                cancellationToken);

            var track = new NowPlayingTrack
            {
                Title = properties.Title.Trim(),
                Artist = properties.Artist.Trim(),
                Album = album,
                Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
                Position = position > TimeSpan.Zero ? position : TimeSpan.Zero,
                Status = playback,
                ObservedAt = observedAt,
                LocalCoverUri = localCover,
                SourceId = sourceId
            };

            var score = (isDeezer ? 100 : 10) + (playback == PlaybackStatus.Playing ? 20 : 0);
            candidates.Add((track, score, observedAt));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Updated)
            .Select(candidate => candidate.Track)
            .FirstOrDefault();
    }

    private async Task<Uri?> ReadThumbnailAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties properties,
        string identity,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        _artworkCache.TryGetValue(identity, out var cached);
        if (cached is not null &&
            now - cached.CheckedAt < ArtworkRefreshInterval &&
            IsReadableLocalArtwork(cached.Uri))
        {
            return cached.Uri;
        }

        if (properties.Thumbnail is null)
        {
            return CacheResult(identity, cached?.Uri, now);
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeezerPresence",
            "artwork");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = await properties.Thumbnail.OpenReadAsync();
            if (source.Size is 0 or > 16 * 1024 * 1024)
            {
                return null;
            }

            using var reader = new DataReader(source.GetInputStreamAt(0));
            var expected = checked((uint)source.Size);
            var loaded = await reader.LoadAsync(expected);
            cancellationToken.ThrowIfCancellationRequested();
            if (loaded != expected)
            {
                return null;
            }

            var bytes = new byte[expected];
            reader.ReadBytes(bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..24];
            var path = Path.Combine(directory, $"{hash}.img");
            Directory.CreateDirectory(directory);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }

            var uri = LocalArtworkUri(path);
            PruneArtwork(directory, path);
            return CacheResult(identity, uri, now);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return CacheResult(identity, cached?.Uri, now);
        }
    }

    private Uri? CacheResult(string identity, Uri? uri, DateTimeOffset checkedAt)
    {
        if (!IsReadableLocalArtwork(uri))
        {
            uri = null;
        }

        _artworkCache[identity] = new ArtworkCacheEntry(uri, checkedAt);
        if (_artworkCache.Count > ArtworkFileLimit)
        {
            var oldest = _artworkCache
                .OrderBy(pair => pair.Value.CheckedAt)
                .Take(_artworkCache.Count - ArtworkFileLimit)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in oldest)
            {
                _artworkCache.Remove(key);
            }
        }

        return uri;
    }

    private static bool IsReadableLocalArtwork(Uri? uri) =>
        uri is { IsFile: true } &&
        File.Exists(uri.LocalPath) &&
        new FileInfo(uri.LocalPath).Length > 0;

    private static void PruneArtwork(string directory, string currentPath)
    {
        try
        {
            foreach (var previous in Directory
                .EnumerateFiles(directory, "*.img")
                .Where(path => !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(ArtworkFileLimit - 1))
            {
                File.Delete(previous);
            }
        }
        catch
        {
            // A stale cache file must never interrupt media detection.
        }
    }

    private static Uri LocalArtworkUri(string path) =>
        new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = path }.Uri;

    private sealed record ArtworkCacheEntry(Uri? Uri, DateTimeOffset CheckedAt);
}
