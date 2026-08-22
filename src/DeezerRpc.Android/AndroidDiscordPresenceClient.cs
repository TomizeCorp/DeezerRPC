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
            error = "Ouvre Deezer Presence pour reconnecter Discord";
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

    public static bool TryBeginAuthorization(string applicationId, out string error)
    {
        try
        {
            if (Native.Initialize(applicationId) != 0)
            {
                error = "Initialisation Discord Android impossible";
                return false;
            }

            var result = Native.BeginAuthorize(applicationId);
            error = result switch
            {
                0 => string.Empty,
                1 => "Identifiant d’application Discord invalide",
                _ => "Impossible d’ouvrir la connexion Discord"
            };
            return result == 0;
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

    public static bool TryCompleteAuthorization(
        string applicationId,
        out DiscordOAuthTokens? tokens,
        out DiscordAccountProfile? profile,
        out string error)
    {
        tokens = null;
        profile = null;
        try
        {
            var accessToken = new StringBuilder(4096);
            var refreshToken = new StringBuilder(4096);
            var authorizeResult = Native.FinishAuthorize(
                applicationId,
                accessToken,
                accessToken.Capacity,
                refreshToken,
                refreshToken.Capacity,
                out var expiresInSeconds);
            if (authorizeResult != 0)
            {
                error = authorizeResult switch
                {
                    3 => "Connexion Discord annulée ou expirée",
                    4 => "Autorisation Discord refusée",
                    6 => "Échange OAuth refusé — vérifie le client public et la redirection Discord",
                    _ => "Connexion OAuth Discord impossible"
                };
                return false;
            }

            tokens = new DiscordOAuthTokens
            {
                AccessToken = accessToken.ToString(),
                RefreshToken = refreshToken.ToString(),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds))
            };

            if (Native.ConnectAuthenticated(applicationId, tokens.AccessToken) != 0)
            {
                error = "Compte lié — connexion Discord en attente du réseau";
                return true;
            }

            TryReadConnectedUser(applicationId, out profile);

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

    public static bool TryExchangeAuthorizationCode(
        string applicationId,
        string code,
        string codeVerifier,
        string redirectUri,
        out DiscordOAuthTokens? tokens,
        out DiscordAccountProfile? profile,
        out string error)
    {
        tokens = null;
        profile = null;
        try
        {
            var accessToken = new StringBuilder(4096);
            var refreshToken = new StringBuilder(4096);
            var exchangeResult = Native.ExchangeAuthorizationCode(
                applicationId,
                code,
                codeVerifier,
                redirectUri,
                accessToken,
                accessToken.Capacity,
                refreshToken,
                refreshToken.Capacity,
                out var expiresInSeconds);
            if (exchangeResult != 0)
            {
                error = exchangeResult switch
                {
                    5 => "Discord n’a pas répondu à temps",
                    6 => "Échange OAuth refusé — vérifie le client public et la redirection Discord",
                    _ => "Connexion OAuth Discord impossible"
                };
                return false;
            }

            tokens = new DiscordOAuthTokens
            {
                AccessToken = accessToken.ToString(),
                RefreshToken = refreshToken.ToString(),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds))
            };

            if (Native.ConnectAuthenticated(applicationId, tokens.AccessToken) != 0)
            {
                error = "Compte lié — connexion Discord en attente du réseau";
                return true;
            }

            TryReadConnectedUser(applicationId, out profile);
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

    public static bool TryRestoreAccount(
        string applicationId,
        DiscordOAuthTokens tokens,
        out DiscordAccountProfile? profile,
        out string error)
    {
        profile = null;
        try
        {
            if (Native.Initialize(applicationId) != 0)
            {
                error = "Initialisation Discord Android impossible";
                return false;
            }

            var result = Native.ConnectAuthenticated(applicationId, tokens.AccessToken);
            if (result != 0)
            {
                error = result switch
                {
                    2 => "Discord n’a pas répondu à temps",
                    3 => "Jeton Discord refusé",
                    4 => "Connexion Discord interrompue — nouvelle tentative automatique",
                    _ => "Connexion Discord mobile impossible"
                };
                return false;
            }

            TryReadConnectedUser(applicationId, out profile);
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

    public static bool TryRefreshTokens(
        string applicationId,
        DiscordOAuthTokens current,
        out DiscordOAuthTokens? refreshed,
        out string error)
    {
        refreshed = null;
        try
        {
            var accessToken = new StringBuilder(4096);
            var refreshToken = new StringBuilder(4096);
            var result = Native.RefreshToken(
                applicationId,
                current.RefreshToken,
                accessToken,
                accessToken.Capacity,
                refreshToken,
                refreshToken.Capacity,
                out var expiresInSeconds);
            if (result != 0)
            {
                error = "Renouvellement Discord impossible — reconnecte ton compte";
                return false;
            }

            refreshed = new DiscordOAuthTokens
            {
                AccessToken = accessToken.ToString(),
                RefreshToken = refreshToken.ToString(),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds))
            };
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

    public static bool IsAuthenticatedConnectionReady()
    {
        try
        {
            return Native.ConnectionStatus() == 3;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
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

        [DllImport(LibraryName, EntryPoint = "drpc_begin_authorize", CallingConvention = CallingConvention.Cdecl)]
        public static extern int BeginAuthorize(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId);

        [DllImport(LibraryName, EntryPoint = "drpc_finish_authorize", CallingConvention = CallingConvention.Cdecl)]
        public static extern int FinishAuthorize(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId,
            [Out] StringBuilder accessToken,
            int accessTokenCapacity,
            [Out] StringBuilder refreshToken,
            int refreshTokenCapacity,
            out long expiresInSeconds);

        [DllImport(LibraryName, EntryPoint = "drpc_exchange_authorization_code", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ExchangeAuthorizationCode(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string code,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string codeVerifier,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string redirectUri,
            [Out] StringBuilder accessToken,
            int accessTokenCapacity,
            [Out] StringBuilder refreshToken,
            int refreshTokenCapacity,
            out long expiresInSeconds);

        [DllImport(LibraryName, EntryPoint = "drpc_refresh_token", CallingConvention = CallingConvention.Cdecl)]
        public static extern int RefreshToken(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string currentRefreshToken,
            [Out] StringBuilder accessToken,
            int accessTokenCapacity,
            [Out] StringBuilder refreshToken,
            int refreshTokenCapacity,
            out long expiresInSeconds);

        [DllImport(LibraryName, EntryPoint = "drpc_connect_authenticated", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ConnectAuthenticated(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string accessToken);

        [DllImport(LibraryName, EntryPoint = "drpc_connection_status", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ConnectionStatus();

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
