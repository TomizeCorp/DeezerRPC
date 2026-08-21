using System.Runtime.InteropServices;
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
            error = "Appuie sur Connecter Discord dans Deezer Presence";
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
                activity.State,
                timestamps?.Start ?? 0,
                timestamps?.End ?? 0,
                assets?.LargeImage ?? string.Empty,
                assets?.LargeText ?? string.Empty,
                button?.Label ?? string.Empty,
                button?.Url ?? string.Empty);
            error = result switch
            {
                0 => string.Empty,
                3 => "Discord Android ne répond pas — ouvre Discord",
                _ => "Discord Android a refusé la présence"
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

    private static class Native
    {
        [DllImport(LibraryName, EntryPoint = "drpc_initialize", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Initialize([MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId);

        [DllImport(LibraryName, EntryPoint = "drpc_set_activity", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetActivity(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string details,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string state,
            long startTimestamp,
            long endTimestamp,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string largeImage,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string largeText,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string buttonLabel,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string buttonUrl);

        [DllImport(LibraryName, EntryPoint = "drpc_clear_activity", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ClearActivity();

        [DllImport(LibraryName, EntryPoint = "drpc_shutdown", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Shutdown();
    }
}
