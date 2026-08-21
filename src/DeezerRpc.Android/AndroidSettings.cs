using Android.Content;
using DeezerRpc.Core;

namespace DeezerRpc.Android;

internal sealed record AndroidAppSettings
{
    public bool RichPresenceEnabled { get; init; } = true;
    public bool ShowAlbum { get; init; } = true;
    public bool ShowProgress { get; init; } = true;
    public bool ShowDeezerButton { get; init; } = true;
    public bool KeepRunningInBackground { get; init; } = true;
    public bool ShowNotifications { get; init; } = true;
}

internal sealed record AndroidPlaybackSnapshot
{
    public string StatusText { get; init; } = "En attente";
    public NowPlayingTrack? Track { get; init; }
    public bool DiscordConnected { get; init; }
}

internal static class AndroidSettings
{
    private const string PreferencesName = "deezerrpc";
    private const string LastStatusKey = "last_status";

    public static AndroidAppSettings GetAppSettings(Context context)
    {
        var preferences = Preferences(context);
        return new AndroidAppSettings
        {
            RichPresenceEnabled = preferences.GetBoolean("presence_enabled", true),
            ShowAlbum = preferences.GetBoolean("show_album", true),
            ShowProgress = preferences.GetBoolean("show_progress", true),
            ShowDeezerButton = preferences.GetBoolean("show_button", true),
            KeepRunningInBackground = preferences.GetBoolean("keep_background", true),
            ShowNotifications = preferences.GetBoolean("show_notifications", true)
        };
    }

    public static void SaveAppSettings(Context context, AndroidAppSettings settings) =>
        Preferences(context).Edit()!
            .PutBoolean("presence_enabled", settings.RichPresenceEnabled)!
            .PutBoolean("show_album", settings.ShowAlbum)!
            .PutBoolean("show_progress", settings.ShowProgress)!
            .PutBoolean("show_button", settings.ShowDeezerButton)!
            .PutBoolean("keep_background", settings.KeepRunningInBackground)!
            .PutBoolean("show_notifications", settings.ShowNotifications)!
            .Apply();

    public static string GetLastStatus(Context context) =>
        Preferences(context).GetString(LastStatusKey, "En attente") ?? "En attente";

    public static void SetLastStatus(Context context, string status) =>
        Preferences(context).Edit()?.PutString(LastStatusKey, status)?.Apply();

    public static void SavePlayback(Context context, NowPlayingTrack track, string status, bool discordConnected)
    {
        var edit = Preferences(context).Edit();
        edit?.PutString(LastStatusKey, status);
        edit?.PutString("track_title", track.Title);
        edit?.PutString("track_artist", track.Artist);
        edit?.PutString("track_album", track.Album);
        edit?.PutLong("track_duration", (long)track.Duration.TotalMilliseconds);
        edit?.PutLong("track_position", (long)track.Position.TotalMilliseconds);
        edit?.PutLong("track_observed", track.ObservedAt.ToUnixTimeMilliseconds());
        edit?.PutInt("track_status", (int)track.Status);
        edit?.PutString("track_cover", track.CoverUrl?.AbsoluteUri ?? string.Empty);
        edit?.PutString("track_url", track.TrackUrl?.AbsoluteUri ?? string.Empty);
        edit?.PutBoolean("discord_connected", discordConnected);
        edit?.Apply();
    }

    public static void ClearPlayback(Context context, string status)
    {
        var edit = Preferences(context).Edit();
        edit?.PutString(LastStatusKey, status);
        edit?.Remove("track_title");
        edit?.Remove("track_artist");
        edit?.Remove("track_album");
        edit?.Remove("track_duration");
        edit?.Remove("track_position");
        edit?.Remove("track_observed");
        edit?.Remove("track_status");
        edit?.Remove("track_cover");
        edit?.Remove("track_url");
        edit?.PutBoolean("discord_connected", false);
        edit?.Apply();
    }

    public static AndroidPlaybackSnapshot GetPlayback(Context context)
    {
        var preferences = Preferences(context);
        var title = preferences.GetString("track_title", null);
        NowPlayingTrack? track = null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            var cover = ParseHttps(preferences.GetString("track_cover", null));
            var url = ParseHttps(preferences.GetString("track_url", null));
            track = new NowPlayingTrack
            {
                Title = title,
                Artist = preferences.GetString("track_artist", string.Empty) ?? string.Empty,
                Album = preferences.GetString("track_album", string.Empty) ?? string.Empty,
                Duration = TimeSpan.FromMilliseconds(Math.Max(0, preferences.GetLong("track_duration", 0))),
                Position = TimeSpan.FromMilliseconds(Math.Max(0, preferences.GetLong("track_position", 0))),
                ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(preferences.GetLong("track_observed", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())),
                Status = (PlaybackStatus)preferences.GetInt("track_status", (int)PlaybackStatus.Stopped),
                CoverUrl = cover,
                TrackUrl = url,
                SourceId = "deezer.android.app"
            };
        }

        return new AndroidPlaybackSnapshot
        {
            StatusText = GetLastStatus(context),
            Track = track,
            DiscordConnected = preferences.GetBoolean("discord_connected", false)
        };
    }

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
        ?? throw new InvalidOperationException("Stockage Android indisponible.");

    private static Uri? ParseHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
}
