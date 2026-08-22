using Android.Content;
using DeezerRpc.Core;

namespace DeezerRpc.Android;

internal sealed record DiscordConnectionOutcome(
    bool Connected,
    DiscordAccountProfile? Profile,
    string Error);

internal static class DiscordMobileConnection
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromHours(12);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<DiscordConnectionOutcome> AuthorizeAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            DiscordOAuthTokens? tokens = null;
            DiscordAccountProfile? profile = null;
            var error = string.Empty;
            var authorized = await Task.Run(() =>
                AndroidDiscordPresenceClient.TryAuthorizeAccount(
                    AppIdentity.DiscordApplicationId,
                    out tokens,
                    out profile,
                    out error), cancellationToken);

            if (tokens is not null)
            {
                AndroidSettings.SaveDiscordOAuthTokens(context, tokens);
            }
            if (profile is not null)
            {
                AndroidSettings.SaveDiscordAccount(context, profile);
            }

            return new DiscordConnectionOutcome(authorized && profile is not null, profile, error);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<DiscordConnectionOutcome> EnsureConnectedAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        if (!AndroidSettings.IsDiscordConnectionEnabled(context))
        {
            return new DiscordConnectionOutcome(false, null, "Discord déconnecté");
        }
        if (!DiscordSocialSdkInitializer.IsInitialized)
        {
            return new DiscordConnectionOutcome(
                false,
                null,
                "Ouvre Deezer Presence une fois pour relancer la connexion Discord");
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var tokens = AndroidSettings.GetDiscordOAuthTokens(context);
            if (tokens is null)
            {
                return new DiscordConnectionOutcome(
                    false,
                    null,
                    "Appuie sur Discord pour lier ton compte");
            }

            var now = DateTimeOffset.UtcNow;
            if (tokens.ExpiresAt - now <= RefreshWindow)
            {
                DiscordOAuthTokens? refreshed = null;
                var refreshError = string.Empty;
                var refreshSucceeded = await Task.Run(() =>
                    AndroidDiscordPresenceClient.TryRefreshTokens(
                        AppIdentity.DiscordApplicationId,
                        tokens,
                        out refreshed,
                        out refreshError), cancellationToken);
                if (refreshSucceeded && refreshed is not null)
                {
                    tokens = refreshed;
                    AndroidSettings.SaveDiscordOAuthTokens(context, tokens);
                }
                else if (tokens.ExpiresAt <= now)
                {
                    AndroidSettings.ClearDiscordOAuthTokens(context);
                    return new DiscordConnectionOutcome(false, null, refreshError);
                }
            }

            DiscordAccountProfile? profile = null;
            var error = string.Empty;
            var connected = await Task.Run(() =>
                AndroidDiscordPresenceClient.TryRestoreAccount(
                    AppIdentity.DiscordApplicationId,
                    tokens,
                    out profile,
                    out error), cancellationToken);
            if (connected && profile is not null)
            {
                AndroidSettings.SaveDiscordAccount(context, profile);
            }
            else
            {
                AndroidSettings.SetDiscordAccountConnected(context, connected);
            }

            return new DiscordConnectionOutcome(connected, profile, error);
        }
        finally
        {
            Gate.Release();
        }
    }
}
