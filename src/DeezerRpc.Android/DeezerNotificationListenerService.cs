using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Service.Notification;
using DeezerRpc.Core;
using System.Security.Cryptography;
using CorePlaybackStatus = DeezerRpc.Core.PlaybackStatus;

namespace DeezerRpc.Android;

[Service(
    Name = "com.tomize.deezerrpc.DeezerNotificationListenerService",
    Label = "Détection média DeezerRPC",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    ForegroundServiceType = ForegroundService.TypeDataSync,
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class DeezerNotificationListenerService : NotificationListenerService
{
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FailedPublishRetryInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ProfileRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DiscordConnectionCheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ArtworkRefreshInterval = TimeSpan.FromSeconds(2);
    private const int ArtworkFileLimit = 16;
    private readonly CancellationTokenSource _stop = new();
    private readonly DiscordActivityBuilder _activityBuilder = new();
    private readonly DeezerCatalogClient _catalog = new();
    private readonly AndroidDiscordPresenceClient _discord = new();
    private readonly Dictionary<string, ArtworkCacheEntry> _artworkCache = new(StringComparer.Ordinal);
    private Task? _monitorTask;
    private string? _lastFingerprint;
    private DateTimeOffset _lastPublishAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPublishedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastProfileRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDiscordConnectionCheck = DateTimeOffset.MinValue;
    private bool _lastPublishFailed;
    private bool _foregroundActive;

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        if (AndroidSettings.GetAppSettings(this).KeepRunningInBackground)
        {
            StartPersistentNotification();
        }
        AndroidSettings.SetLastStatus(this, "Surveillance de Deezer active");
        _monitorTask ??= Task.Run(() => MonitorAsync(_stop.Token));
    }

    public override void OnListenerDisconnected()
    {
        AndroidSettings.SetLastStatus(this, "Accès aux sessions média déconnecté");
        base.OnListenerDisconnected();
    }

    public override void OnDestroy()
    {
        _stop.Cancel();
        _discord.TryClear();
        _discord.Dispose();
        _catalog.Dispose();
        _stop.Dispose();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await UpdateOnceAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not System.OperationCanceledException)
            {
                AndroidSettings.SetLastStatus(this, $"Erreur : {exception.Message}");
            }

            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    private async Task UpdateOnceAsync(CancellationToken cancellationToken)
    {
        var settings = AndroidSettings.GetAppSettings(this);
        UpdateBackgroundMode(settings.KeepRunningInBackground);
        await MaintainDiscordConnectionAsync(cancellationToken);
        var track = ReadDeezerSession();
        if (track is null || track.Status == CorePlaybackStatus.Stopped)
        {
            if (_lastFingerprint is not null)
            {
                _discord.TryClear();
            }

            _lastFingerprint = null;
            _lastPublishedAt = DateTimeOffset.MinValue;
            _lastPublishFailed = false;

            AndroidSettings.ClearPlayback(this, "Aucune lecture Deezer détectée");
            return;
        }

        if (track.Status == CorePlaybackStatus.Paused)
        {
            var pausedFingerprint = $"paused|{track.Identity}";
            if (_lastFingerprint == pausedFingerprint)
            {
                return;
            }

            _discord.TryClear();
            _lastFingerprint = pausedFingerprint;
            _lastPublishedAt = DateTimeOffset.MinValue;
            _lastPublishFailed = false;
            track = await _catalog.EnrichAsync(track, requireCatalogMatch: false, cancellationToken) ?? track;
            AndroidSettings.SavePlayback(this, track, "En pause — activité Discord retirée", false);
            return;
        }

        track = await _catalog.EnrichAsync(track, requireCatalogMatch: false, cancellationToken) ?? track;
        if (!settings.RichPresenceEnabled)
        {
            _discord.TryClear();
            _lastFingerprint = null;
            _lastPublishedAt = DateTimeOffset.MinValue;
            _lastPublishFailed = false;
            AndroidSettings.SavePlayback(this, track, "Rich Presence désactivée", false);
            return;
        }
        var fingerprint = string.Join('|',
            track.Identity,
            track.Status,
            track.Duration.TotalSeconds,
            track.CoverUrl,
            track.LocalCoverUri,
            track.TrackUrl,
            settings.ShowAlbum,
            settings.ShowProgress,
            settings.ShowDeezerButton);
        var now = DateTimeOffset.UtcNow;
        var trackChanged = fingerprint != _lastFingerprint;
        var refreshDue = now - _lastPublishedAt >= PresenceRefreshInterval;
        if (!trackChanged && !refreshDue)
        {
            return;
        }
        if (_lastPublishFailed && now - _lastPublishAttempt < FailedPublishRetryInterval)
        {
            return;
        }

        _lastPublishAttempt = now;
        var activity = _activityBuilder.Build(track, now, new PresenceOptions
        {
            ShowAlbum = settings.ShowAlbum,
            ShowProgress = settings.ShowProgress,
            ShowDeezerButton = settings.ShowDeezerButton
        });
        if (_discord.TrySetActivity(AppIdentity.DiscordApplicationId, activity, out var error))
        {
            _lastFingerprint = fingerprint;
            _lastPublishedAt = now;
            _lastPublishFailed = false;
            AndroidSettings.SavePlayback(this, track, $"Publié — {track.Title}", true);
            RefreshDiscordAccount(now);
        }
        else
        {
            _lastPublishFailed = true;
            _lastDiscordConnectionCheck = DateTimeOffset.MinValue;
            AndroidSettings.SavePlayback(this, track, error, false);
        }
    }

    private async Task MaintainDiscordConnectionAsync(CancellationToken cancellationToken)
    {
        if (!AndroidSettings.IsDiscordConnectionEnabled(this))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (AndroidDiscordPresenceClient.IsAuthenticatedConnectionReady() &&
            now - _lastDiscordConnectionCheck < DiscordConnectionCheckInterval)
        {
            return;
        }
        if (now - _lastDiscordConnectionCheck < DiscordConnectionCheckInterval)
        {
            return;
        }

        _lastDiscordConnectionCheck = now;
        var result = await DiscordMobileConnection.EnsureConnectedAsync(this, cancellationToken);
        if (result.Connected)
        {
            _lastPublishFailed = false;
            return;
        }

        if (AndroidSettings.GetPlayback(this).Track is null)
        {
            AndroidSettings.SetLastStatus(this, result.Error);
        }
    }

    private void RefreshDiscordAccount(DateTimeOffset now)
    {
        var existing = AndroidSettings.GetPlayback(this).DiscordAccount;
        if (existing is not null && now - _lastProfileRefresh < ProfileRefreshInterval)
        {
            return;
        }
        if (now - _lastProfileRefresh < FailedPublishRetryInterval)
        {
            return;
        }

        _lastProfileRefresh = now;
        if (_discord.TryGetConnectedUser(AppIdentity.DiscordApplicationId, out var profile) && profile is not null)
        {
            AndroidSettings.SaveDiscordAccount(this, profile);
        }
        else
        {
            AndroidSettings.SetDiscordAccountConnected(this, true);
        }
    }

    private NowPlayingTrack? ReadDeezerSession()
    {
        var manager = GetSystemService(MediaSessionService) as MediaSessionManager;
        if (manager is null)
        {
            return null;
        }

        var listener = new ComponentName(this, Java.Lang.Class.FromType(typeof(DeezerNotificationListenerService)));
        var controller = manager.GetActiveSessions(listener)
            .FirstOrDefault(session => session.PackageName?.Contains("deezer", StringComparison.OrdinalIgnoreCase) == true);
        var metadata = controller?.Metadata;
        var playback = controller?.PlaybackState;
        if (metadata is null || playback is null)
        {
            return null;
        }

        var title = metadata.GetString(MediaMetadata.MetadataKeyTitle)?.Trim() ?? string.Empty;
        var artist = metadata.GetString(MediaMetadata.MetadataKeyArtist)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var durationMilliseconds = metadata.GetLong(MediaMetadata.MetadataKeyDuration);
        var elapsedSinceUpdate = Math.Max(0, SystemClock.ElapsedRealtime() - playback.LastPositionUpdateTime);
        var observedAt = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(elapsedSinceUpdate);
        var status = playback.State switch
        {
            PlaybackStateCode.Playing => CorePlaybackStatus.Playing,
            PlaybackStateCode.Paused => CorePlaybackStatus.Paused,
            _ => CorePlaybackStatus.Stopped
        };

        var album = metadata.GetString(MediaMetadata.MetadataKeyAlbum)?.Trim() ?? string.Empty;
        var cover = ReadSessionCover(metadata, $"{title}\n{artist}\n{album}");
        return new NowPlayingTrack
        {
            Title = title,
            Artist = artist,
            Album = album,
            Duration = durationMilliseconds > 0 ? TimeSpan.FromMilliseconds(durationMilliseconds) : TimeSpan.Zero,
            Position = playback.Position > 0 ? TimeSpan.FromMilliseconds(playback.Position) : TimeSpan.Zero,
            ObservedAt = observedAt,
            Status = status,
            CoverUrl = cover.PublicUri,
            LocalCoverUri = cover.LocalUri,
            SourceId = controller?.PackageName ?? "deezer.android.app"
        };
    }

    private (System.Uri? PublicUri, System.Uri? LocalUri) ReadSessionCover(
        MediaMetadata metadata,
        string identity)
    {
        var uriValues = new[]
        {
            metadata.GetString(MediaMetadata.MetadataKeyAlbumArtUri),
            metadata.GetString(MediaMetadata.MetadataKeyArtUri),
            metadata.GetString(MediaMetadata.MetadataKeyDisplayIconUri),
            metadata.Description?.IconUri?.ToString()
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();

        foreach (var value in uriValues)
        {
            if (System.Uri.TryCreate(value, UriKind.Absolute, out var remote) &&
                remote.Scheme == System.Uri.UriSchemeHttps)
            {
                CacheArtwork(identity, (remote, null), DateTimeOffset.UtcNow);
                return (remote, null);
            }
        }

        var now = DateTimeOffset.UtcNow;
        _artworkCache.TryGetValue(identity, out var cached);
        if (cached is not null &&
            now - cached.CheckedAt < ArtworkRefreshInterval &&
            IsReadableLocalArtwork(cached.Cover.LocalUri))
        {
            return cached.Cover;
        }

        foreach (var value in uriValues)
        {
            try
            {
                var androidUri = global::Android.Net.Uri.Parse(value);
                if (androidUri is null)
                {
                    continue;
                }
                using var stream = ContentResolver?.OpenInputStream(androidUri);
                using var bitmap = stream is null ? null : BitmapFactory.DecodeStream(stream);
                var persisted = PersistArtwork(bitmap);
                if (persisted is not null)
                {
                    var result = (PublicUri: (System.Uri?)null, LocalUri: persisted);
                    CacheArtwork(identity, result, now);
                    return result;
                }
            }
            catch
            {
                // Some providers deliberately expose their artwork only inside the Deezer process.
            }
        }

        foreach (var bitmap in new[]
        {
            metadata.GetBitmap(MediaMetadata.MetadataKeyAlbumArt),
            metadata.GetBitmap(MediaMetadata.MetadataKeyArt),
            metadata.GetBitmap(MediaMetadata.MetadataKeyDisplayIcon),
            metadata.Description?.IconBitmap
        })
        {
            var persisted = PersistArtwork(bitmap);
            if (persisted is not null)
            {
                var result = (PublicUri: (System.Uri?)null, LocalUri: persisted);
                CacheArtwork(identity, result, now);
                return result;
            }
        }

        (System.Uri? PublicUri, System.Uri? LocalUri) fallback = cached?.Cover ?? (null, null);
        CacheArtwork(identity, fallback, now);
        return fallback;
    }

    private System.Uri? PersistArtwork(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        var directory = ArtworkDirectory();
        if (directory is null)
        {
            return null;
        }

        try
        {
            byte[] bytes;
            using (var output = new MemoryStream())
            {
                if (!bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 92, output))
                {
                    return null;
                }

                bytes = output.ToArray();
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..24];
            var path = System.IO.Path.Combine(directory, $"{hash}.jpg");
            Directory.CreateDirectory(directory);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                File.WriteAllBytes(path, bytes);
            }

            PruneArtwork(directory, path);
            return LocalArtworkUri(path);
        }
        catch
        {
            return null;
        }
    }

    private void CacheArtwork(
        string identity,
        (System.Uri? PublicUri, System.Uri? LocalUri) cover,
        DateTimeOffset checkedAt)
    {
        if (!IsReadableLocalArtwork(cover.LocalUri))
        {
            cover.LocalUri = null;
        }

        _artworkCache[identity] = new ArtworkCacheEntry(cover, checkedAt);
        if (_artworkCache.Count <= ArtworkFileLimit)
        {
            return;
        }

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

    private static bool IsReadableLocalArtwork(System.Uri? uri) =>
        uri is { IsFile: true } &&
        File.Exists(uri.LocalPath) &&
        new FileInfo(uri.LocalPath).Length > 0;

    private static void PruneArtwork(string directory, string currentPath)
    {
        try
        {
            foreach (var previous in Directory
                .EnumerateFiles(directory, "*.jpg")
                .Where(path => !string.Equals(path, currentPath, StringComparison.Ordinal))
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

    private string? ArtworkDirectory()
    {
        var filesPath = FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(filesPath))
        {
            return null;
        }

        return System.IO.Path.Combine(filesPath, "artwork");
    }

    private static System.Uri LocalArtworkUri(string path) =>
        new System.UriBuilder(System.Uri.UriSchemeFile, string.Empty) { Path = path }.Uri;

    private sealed record ArtworkCacheEntry(
        (System.Uri? PublicUri, System.Uri? LocalUri) Cover,
        DateTimeOffset CheckedAt);

    private void StartPersistentNotification()
    {
        const string channelId = "deezerrpc_background";
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(channelId, "Deezer Presence", NotificationImportance.Low)
            {
                Description = "Maintient la détection Deezer et la Rich Presence actives"
            };
            (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
        }

        var openApp = new Intent(this, typeof(MainActivity));
        openApp.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pending = PendingIntent.GetActivity(
            this,
            0,
            openApp,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, channelId)
            : new Notification.Builder(this);
        var notification = builder
            .SetContentTitle("Deezer Presence actif")
            .SetContentText("Détection Deezer et connexion Discord en arrière-plan")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(pending)
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(1540, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(1540, notification);
        }

        _foregroundActive = true;
    }

    private void UpdateBackgroundMode(bool enabled)
    {
        if (enabled && !_foregroundActive)
        {
            StartPersistentNotification();
        }
        else if (!enabled && _foregroundActive)
        {
            StopForeground(StopForegroundFlags.Remove);
            _foregroundActive = false;
        }
    }
}
