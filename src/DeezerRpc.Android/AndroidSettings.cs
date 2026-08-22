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
    public bool DiscordAccountConnected { get; init; }
    public DiscordAccountProfile? DiscordAccount { get; init; }
}

internal sealed record DiscordAccountProfile
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
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
        if (discordConnected)
        {
            edit?.PutBoolean("discord_account_connected", true);
            edit?.PutBoolean("discord_connection_enabled", true);
        }
        edit?.Apply();
    }

    public static void SaveDiscordAccount(Context context, DiscordAccountProfile profile)
    {
        var edit = Preferences(context).Edit();
        edit?.PutBoolean("discord_account_connected", true);
        edit?.PutBoolean("discord_connection_enabled", true);
        edit?.PutString("discord_user_id", profile.UserId);
        edit?.PutString("discord_display_name", profile.DisplayName);
        edit?.PutString("discord_username", profile.Username);
        edit?.PutString("discord_avatar_url", profile.AvatarUrl);
        edit?.Apply();
    }

    public static void SetDiscordAccountConnected(Context context, bool connected) =>
        Preferences(context).Edit()?.PutBoolean("discord_account_connected", connected)?.Apply();

    public static bool IsDiscordConnectionEnabled(Context context) =>
        Preferences(context).GetBoolean("discord_connection_enabled", true);

    public static void SetDiscordConnectionEnabled(Context context, bool enabled) =>
        Preferences(context).Edit()?.PutBoolean("discord_connection_enabled", enabled)?.Apply();

    public static void DisconnectDiscordAccount(Context context)
    {
        var edit = Preferences(context).Edit();
        edit?.PutBoolean("discord_connected", false);
        edit?.PutBoolean("discord_account_connected", false);
        edit?.PutBoolean("discord_connection_enabled", false);
        edit?.Remove("discord_user_id");
        edit?.Remove("discord_display_name");
        edit?.Remove("discord_username");
        edit?.Remove("discord_avatar_url");
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
            DiscordConnected = preferences.GetBoolean("discord_connected", false),
            DiscordAccountConnected = preferences.GetBoolean("discord_account_connected", false),
            DiscordAccount = ReadDiscordAccount(preferences)
        };
    }

    private static DiscordAccountProfile? ReadDiscordAccount(ISharedPreferences preferences)
    {
        var userId = preferences.GetString("discord_user_id", string.Empty) ?? string.Empty;
        var displayName = preferences.GetString("discord_display_name", string.Empty) ?? string.Empty;
        var username = preferences.GetString("discord_username", string.Empty) ?? string.Empty;
        var avatarUrl = preferences.GetString("discord_avatar_url", string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(displayName) &&
            string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(avatarUrl))
        {
            return null;
        }

        return new DiscordAccountProfile
        {
            UserId = userId,
            DisplayName = displayName,
            Username = username,
            AvatarUrl = avatarUrl
        };
    }

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
        ?? throw new InvalidOperationException("Stockage Android indisponible.");

    private static Uri? ParseHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
}
