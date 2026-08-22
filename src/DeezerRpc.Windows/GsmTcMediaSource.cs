using DeezerRpc.Core;
using System.Security.Cryptography;
using System.Text;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DeezerRpc.Windows;

internal sealed class GsmTcMediaSource
{
    private static readonly string[] BrowserMarkers =
    [
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"
    ];

    private GlobalSystemMediaTransportControlsSessionManager? _manager;

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

    private static async Task<Uri?> ReadThumbnailAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties properties,
        string identity,
        CancellationToken cancellationToken)
    {
        if (properties.Thumbnail is null)
        {
            return null;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeezerPresence",
            "artwork");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
        var path = Path.Combine(directory, $"{hash}.img");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return LocalArtworkUri(path);
        }

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
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            foreach (var previous in Directory.EnumerateFiles(directory, "*.img"))
            {
                if (!string.Equals(previous, path, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(previous);
                }
            }
            return LocalArtworkUri(path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Uri LocalArtworkUri(string path) =>
        new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = path }.Uri;
}
