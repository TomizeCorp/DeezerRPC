using DeezerRpc.Core;
using Windows.Media.Control;

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

            var track = new NowPlayingTrack
            {
                Title = properties.Title.Trim(),
                Artist = properties.Artist.Trim(),
                Album = properties.AlbumTitle?.Trim() ?? string.Empty,
                Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
                Position = position > TimeSpan.Zero ? position : TimeSpan.Zero,
                Status = playback,
                ObservedAt = observedAt,
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
}

