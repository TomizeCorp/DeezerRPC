using DeezerRpc.Core;

namespace DeezerRpc.Windows;

internal sealed class PresenceWorker : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(20);

    private readonly AppSettings _settings;
    private readonly Action<PresenceSnapshot> _reportSnapshot;
    private readonly CancellationTokenSource _stop = new();
    private readonly GsmTcMediaSource _mediaSource = new();
    private readonly DeezerCatalogClient _catalog = new();
    private readonly DiscordActivityBuilder _activityBuilder = new();
    private readonly DiscordRpcClient _discord;
    private Task? _runTask;
    private string? _lastFingerprint;
    private NowPlayingTrack? _lastPublishedTrack;
    private bool _presenceWasSet;
    private DateTimeOffset _lastDiscordAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPublishedAt = DateTimeOffset.MinValue;

    public PresenceWorker(AppSettings settings, Action<PresenceSnapshot> reportSnapshot)
    {
        _settings = settings;
        _reportSnapshot = reportSnapshot;
        _discord = new DiscordRpcClient(AppIdentity.DiscordApplicationId);
    }

    public void Start() => _runTask ??= RunAsync(_stop.Token);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        if (_presenceWasSet)
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await TryClearAsync(cleanupTimeout.Token);
        }

        _catalog.Dispose();
        await _discord.DisposeAsync();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Report("Recherche de Deezer…");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.PollIntervalMilliseconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await UpdateOnceAsync(cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                Report("Accès aux sessions média Windows refusé");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Report($"En attente : {exception.Message}");
            }

            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    private async Task UpdateOnceAsync(CancellationToken cancellationToken)
    {
        if (_settings.DiscordConnectionEnabled)
        {
            await EnsureDiscordConnectedAsync(cancellationToken);
        }

        var rawTrack = await _mediaSource.GetCurrentAsync(_settings.EnableBrowserDetection, cancellationToken);
        if (rawTrack is null || rawTrack.Status == PlaybackStatus.Stopped)
        {
            if (_presenceWasSet)
            {
                await TryClearAsync(cancellationToken);
            }

            _lastFingerprint = null;
            _lastPublishedTrack = null;
            Report("Aucune lecture Deezer détectée");
            return;
        }

        if (rawTrack.Status == PlaybackStatus.Paused)
        {
            var pausedFingerprint = $"paused|{rawTrack.Identity}";
            if (_lastFingerprint == pausedFingerprint && !_presenceWasSet)
            {
                return;
            }

            await TryClearAsync(cancellationToken);
            var pausedTrack = _lastPublishedTrack is { } previous && previous.Identity == rawTrack.Identity
                ? previous with
                {
                    Status = PlaybackStatus.Paused,
                    Position = rawTrack.Position,
                    ObservedAt = rawTrack.ObservedAt,
                    Duration = rawTrack.Duration > TimeSpan.Zero ? rawTrack.Duration : previous.Duration
                }
                : rawTrack;
            _lastFingerprint = pausedFingerprint;
            _lastPublishedTrack = pausedTrack;
            Report("En pause — activité Discord retirée", pausedTrack);
            return;
        }

        var browserSource = !rawTrack.SourceId.Contains("deezer", StringComparison.OrdinalIgnoreCase);
        var track = await _catalog.EnrichAsync(rawTrack, browserSource, cancellationToken);
        if (track is null)
        {
            Report("Session navigateur ignorée (non confirmée par Deezer)");
            return;
        }

        if (!_settings.RichPresenceEnabled)
        {
            if (_presenceWasSet)
            {
                await TryClearAsync(cancellationToken);
            }

            _lastPublishedTrack = track;
            Report("Rich Presence désactivée", track);
            return;
        }

        if (!_settings.DiscordConnectionEnabled)
        {
            _lastPublishedTrack = track;
            Report("Discord déconnecté de Deezer Presence", track);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var fingerprint = string.Join('|',
            track.Identity,
            track.Status,
            track.Duration.TotalSeconds,
            track.CoverUrl,
            track.LocalCoverUri,
            track.TrackUrl);
        var shouldReconnect = !_discord.IsConnected && now - _lastDiscordAttempt >= ReconnectInterval;
        var refreshDue = now - _lastPublishedAt >= PresenceRefreshInterval;
        var seekDetected = _lastPublishedTrack is not null &&
            _lastPublishedTrack.Identity == track.Identity &&
            Math.Abs((_lastPublishedTrack.ProjectPosition(now) - track.ProjectPosition(now)).TotalSeconds) >= 5;
        if (fingerprint == _lastFingerprint && !seekDetected && !shouldReconnect && !refreshDue)
        {
            return;
        }

        _lastDiscordAttempt = now;
        var options = new PresenceOptions
        {
            ShowAlbum = _settings.ShowAlbum,
            ShowProgress = _settings.ShowProgress,
            ShowDeezerButton = _settings.ShowDeezerButton
        };
        var activity = _activityBuilder.Build(track, now, options);
        _lastFingerprint = fingerprint;
        _lastPublishedTrack = track;
        try
        {
            await _discord.SetActivityAsync(activity, cancellationToken);
            _presenceWasSet = true;
            _lastPublishedAt = now;
            Report($"Publié — {track.Title}", track);
        }
        catch (IOException)
        {
            Report($"Deezer détecté — attente de Discord Desktop ({track.Title})", track);
        }
    }

    private async Task EnsureDiscordConnectedAsync(CancellationToken cancellationToken)
    {
        if (_discord.IsConnected || DateTimeOffset.UtcNow - _lastDiscordAttempt < ReconnectInterval)
        {
            return;
        }

        _lastDiscordAttempt = DateTimeOffset.UtcNow;
        try
        {
            await _discord.ConnectAsync(cancellationToken);
        }
        catch (IOException)
        {
            // The worker retries automatically; Deezer detection must continue meanwhile.
        }
    }

    private async Task TryClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _discord.ClearActivityAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // Discord may already be closed; there is nothing left to clear locally.
        }
        finally
        {
            _presenceWasSet = false;
            _lastPublishedAt = DateTimeOffset.MinValue;
        }
    }

    private void Report(string status, NowPlayingTrack? track = null) =>
        _reportSnapshot(new PresenceSnapshot
        {
            StatusText = status,
            Track = track ?? _lastPublishedTrack,
            DiscordConnected = _discord.IsConnected,
            DiscordAccount = _discord.Account,
            UpdatedAt = DateTimeOffset.UtcNow
        });
}
