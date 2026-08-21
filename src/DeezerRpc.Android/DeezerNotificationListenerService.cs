using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Service.Notification;
using DeezerRpc.Core;
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
    private readonly CancellationTokenSource _stop = new();
    private readonly DiscordActivityBuilder _activityBuilder = new();
    private readonly DeezerCatalogClient _catalog = new();
    private readonly AndroidDiscordPresenceClient _discord = new();
    private Task? _monitorTask;
    private string? _lastFingerprint;
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
        var track = ReadDeezerSession();
        if (track is null || track.Status == CorePlaybackStatus.Stopped)
        {
            if (_lastFingerprint is not null)
            {
                _discord.TryClear();
                _lastFingerprint = null;
            }

            AndroidSettings.ClearPlayback(this, "Aucune lecture Deezer détectée");
            return;
        }

        track = await _catalog.EnrichAsync(track, requireCatalogMatch: false, cancellationToken) ?? track;
        if (!settings.RichPresenceEnabled)
        {
            _discord.TryClear();
            _lastFingerprint = null;
            AndroidSettings.SavePlayback(this, track, "Rich Presence désactivée", false);
            return;
        }
        var fingerprint = string.Join('|',
            track.Identity,
            track.Status,
            track.Duration.TotalSeconds,
            track.CoverUrl,
            track.TrackUrl,
            settings.ShowAlbum,
            settings.ShowProgress,
            settings.ShowDeezerButton,
            settings.ShowPauseState);
        if (fingerprint == _lastFingerprint)
        {
            return;
        }

        var activity = _activityBuilder.Build(track, DateTimeOffset.UtcNow, new PresenceOptions
        {
            ShowAlbum = settings.ShowAlbum,
            ShowProgress = settings.ShowProgress,
            ShowDeezerButton = settings.ShowDeezerButton,
            ShowPauseState = settings.ShowPauseState
        });
        if (_discord.TrySetActivity(AppIdentity.DiscordApplicationId, activity, out var error))
        {
            _lastFingerprint = fingerprint;
            var status = track.Status == CorePlaybackStatus.Paused
                ? $"En pause — {track.Title}"
                : $"Publié — {track.Title}";
            AndroidSettings.SavePlayback(this, track, status, true);
        }
        else
        {
            AndroidSettings.SavePlayback(this, track, error, false);
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

        return new NowPlayingTrack
        {
            Title = title,
            Artist = artist,
            Album = metadata.GetString(MediaMetadata.MetadataKeyAlbum)?.Trim() ?? string.Empty,
            Duration = durationMilliseconds > 0 ? TimeSpan.FromMilliseconds(durationMilliseconds) : TimeSpan.Zero,
            Position = playback.Position > 0 ? TimeSpan.FromMilliseconds(playback.Position) : TimeSpan.Zero,
            ObservedAt = observedAt,
            Status = status,
            SourceId = controller?.PackageName ?? "deezer.android.app"
        };
    }

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
