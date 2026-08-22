using System.Runtime.InteropServices;
using System.Text;
using DeezerRpc.Core;

namespace DeezerRpc.Android;

internal sealed class AndroidDiscordPresenceClient : IDisposable
{
    private const string LibraryName = "deezerrpc_discord_bridge";
    private string? _initializedApplicationId;
    private bool _libraryAvailable = true;

    public bool TrySetActivity(string applicationId, DiscordActivity activity, out string error)
    {
        if (!DiscordSocialSdkInitializer.IsInitialized)
        {
            error = "Appuie sur le logo Discord dans Deezer Presence";
            return false;
        }

        if (!_libraryAvailable)
        {
            error = "SDK Discord Social 1.10+ absent de cette compilation";
            return false;
        }

        try
        {
            if (!string.Equals(_initializedApplicationId, applicationId, StringComparison.Ordinal))
            {
                if (Native.Initialize(applicationId) != 0)
                {
                    error = "Initialisation Discord Android impossible";
                    return false;
                }

                _initializedApplicationId = applicationId;
            }

            var timestamps = activity.Timestamps;
            var assets = activity.Assets;
            var button = activity.Buttons?.FirstOrDefault();
            var result = Native.SetActivity(
                activity.Details,
                activity.DetailsUrl ?? string.Empty,
                activity.State,
                timestamps?.Start ?? 0,
                timestamps?.End ?? 0,
                assets?.LargeImage ?? string.Empty,
                assets?.LargeText ?? string.Empty,
                assets?.LargeUrl ?? string.Empty,
                assets?.SmallImage ?? string.Empty,
                assets?.SmallText ?? string.Empty,
                assets?.SmallUrl ?? string.Empty,
                button?.Label ?? string.Empty,
                button?.Url ?? string.Empty);
            if (result != 0)
            {
                ResetNativeConnection();
            }
            error = result switch
            {
                0 => string.Empty,
                3 => "Connexion Discord interrompue — nouvelle tentative automatique",
                _ => "Discord Android a refusé la présence — nouvelle tentative automatique"
            };
            return result == 0;
        }
        catch (DllNotFoundException)
        {
            _libraryAvailable = false;
            error = "SDK Discord Social 1.10+ absent de cette compilation";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _libraryAvailable = false;
            error = "Pont natif Discord incompatible";
            return false;
        }
    }

    public bool TryGetConnectedUser(string applicationId, out DiscordAccountProfile? profile)
    {
        profile = null;
        if (!_libraryAvailable || _initializedApplicationId is null)
        {
            return false;
        }

        try
        {
            return TryReadConnectedUser(applicationId, out profile);
        }
        catch (DllNotFoundException)
        {
            _libraryAvailable = false;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _libraryAvailable = false;
            return false;
        }
    }

    public static bool TryConnectAccount(string applicationId, out DiscordAccountProfile? profile, out string error)
    {
        profile = null;
        try
        {
            if (Native.Initialize(applicationId) != 0)
            {
                error = "Initialisation Discord Android impossible";
                return false;
            }
            if (!TryReadConnectedUser(applicationId, out profile))
            {
                error = "Ouvre Discord puis reviens dans Deezer Presence";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (DllNotFoundException)
        {
            error = "SDK Discord Social 1.10+ absent de cette compilation";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            error = "Pont natif Discord incompatible";
            return false;
        }
    }

    public static void DisconnectExisting()
    {
        try
        {
            Native.ClearActivity();
            Native.Shutdown();
        }
        catch (DllNotFoundException)
        {
            // Detector-only builds deliberately have no native Discord bridge.
        }
        catch (EntryPointNotFoundException)
        {
            // An older bridge cannot keep an active connection after the process exits.
        }
    }

    public void TryClear()
    {
        if (!_libraryAvailable || _initializedApplicationId is null)
        {
            return;
        }

        try
        {
            Native.ClearActivity();
        }
        catch (DllNotFoundException)
        {
            _libraryAvailable = false;
        }
    }

    public void Dispose()
    {
        if (!_libraryAvailable)
        {
            return;
        }

        try
        {
            Native.Shutdown();
        }
        catch (DllNotFoundException)
        {
            // The detector-only build deliberately has no native bridge.
        }
    }

    private void ResetNativeConnection()
    {
        try
        {
            Native.Shutdown();
        }
        catch (DllNotFoundException)
        {
            _libraryAvailable = false;
        }
        finally
        {
            _initializedApplicationId = null;
        }
    }

    private static bool TryReadConnectedUser(string applicationId, out DiscordAccountProfile? profile)
    {
        var userId = new StringBuilder(32);
        var displayName = new StringBuilder(256);
        var username = new StringBuilder(256);
        var avatarUrl = new StringBuilder(1024);
        var result = Native.GetConnectedUser(
            applicationId,
            userId,
            userId.Capacity,
            displayName,
            displayName.Capacity,
            username,
            username.Capacity,
            avatarUrl,
            avatarUrl.Capacity);
        if (result != 0)
        {
            profile = null;
            return false;
        }

        profile = new DiscordAccountProfile
        {
            UserId = userId.ToString(),
            DisplayName = displayName.ToString(),
            Username = username.ToString(),
            AvatarUrl = avatarUrl.ToString()
        };
        return !string.IsNullOrWhiteSpace(profile.UserId);
    }

    private static class Native
    {
        [DllImport(LibraryName, EntryPoint = "drpc_initialize", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Initialize([MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId);

        [DllImport(LibraryName, EntryPoint = "drpc_set_activity", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetActivity(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string details,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detailsUrl,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string state,
            long startTimestamp,
            long endTimestamp,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string largeImage,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string largeText,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string largeUrl,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string smallImage,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string smallText,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string smallUrl,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string buttonLabel,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string buttonUrl);

        [DllImport(LibraryName, EntryPoint = "drpc_clear_activity", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ClearActivity();

        [DllImport(LibraryName, EntryPoint = "drpc_get_connected_user", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetConnectedUser(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId,
            [Out] StringBuilder userId,
            int userIdCapacity,
            [Out] StringBuilder displayName,
            int displayNameCapacity,
            [Out] StringBuilder username,
            int usernameCapacity,
            [Out] StringBuilder avatarUrl,
            int avatarUrlCapacity);

        [DllImport(LibraryName, EntryPoint = "drpc_shutdown", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Shutdown();
    }
}
