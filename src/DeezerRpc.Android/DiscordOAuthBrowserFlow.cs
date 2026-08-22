using System.Security.Cryptography;
using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Browser.CustomTabs;
using Uri = Android.Net.Uri;

namespace DeezerRpc.Android;

internal sealed record DiscordBrowserAuthorizationResult(
    bool Successful,
    string Code,
    string CodeVerifier,
    string RedirectUri,
    string Error);

internal static class DiscordOAuthBrowserFlow
{
    public const string CallbackScheme = "discord-1540336569532031116";
    public const string CallbackPath = "/authorize/callback";
    public const string RedirectUri = $"{CallbackScheme}:{CallbackPath}";

    private static readonly object Sync = new();
    private static PendingAuthorization? _pending;

    private sealed record PendingAuthorization(
        string State,
        string CodeVerifier,
        TaskCompletionSource<DiscordBrowserAuthorizationResult> Source);

    public static bool HasPendingAuthorization
    {
        get
        {
            lock (Sync)
            {
                return _pending is not null;
            }
        }
    }

    public static async Task<DiscordBrowserAuthorizationResult> AuthorizeAsync(
        Activity activity,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var source = new TaskCompletionSource<DiscordBrowserAuthorizationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (Sync)
        {
            _pending?.Source.TrySetResult(Failure("Une nouvelle connexion Discord a été lancée"));
            _pending = new PendingAuthorization(state, verifier, source);
        }

        var authorizationUrl = BuildAuthorizationUrl(applicationId, state, challenge);
        var launchSource = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                var uri = Uri.Parse(authorizationUrl)
                    ?? throw new InvalidOperationException("URL Discord invalide");
                var customTab = new CustomTabsIntent.Builder().Build();
                var browserPackage = CustomTabsHelper.GetPackageNameToUse(activity);
                if (!string.IsNullOrWhiteSpace(browserPackage))
                {
                    customTab.Intent.SetPackage(browserPackage);
                }
                customTab.LaunchUrl(activity, uri);
                launchSource.TrySetResult(string.Empty);
            }
            catch (Exception exception)
            {
                launchSource.TrySetResult($"Impossible d’ouvrir la page Discord : {exception.Message}");
            }
        });

        var launchError = await launchSource.Task.WaitAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(launchError))
        {
            Cancel(launchError);
        }

        try
        {
            return await source.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            Cancel("Connexion Discord expirée");
            return await source.Task;
        }
    }

    public static void Complete(Uri? callbackUri)
    {
        PendingAuthorization? pending;
        lock (Sync)
        {
            pending = _pending;
            _pending = null;
        }
        if (pending is null)
        {
            return;
        }

        if (callbackUri is null ||
            !string.Equals(callbackUri.Scheme, CallbackScheme, StringComparison.Ordinal) ||
            !string.Equals(callbackUri.Path, CallbackPath, StringComparison.Ordinal))
        {
            pending.Source.TrySetResult(Failure("Retour OAuth Discord invalide"));
            return;
        }

        var returnedState = callbackUri.GetQueryParameter("state") ?? string.Empty;
        if (!string.Equals(returnedState, pending.State, StringComparison.Ordinal))
        {
            pending.Source.TrySetResult(Failure("La sécurité de la connexion Discord a refusé le retour"));
            return;
        }

        var oauthError = callbackUri.GetQueryParameter("error");
        if (!string.IsNullOrWhiteSpace(oauthError))
        {
            var description = callbackUri.GetQueryParameter("error_description");
            pending.Source.TrySetResult(Failure(
                string.IsNullOrWhiteSpace(description)
                    ? $"Discord a refusé l’autorisation : {oauthError}"
                    : description));
            return;
        }

        var code = callbackUri.GetQueryParameter("code") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            pending.Source.TrySetResult(Failure("Discord n’a renvoyé aucun code d’autorisation"));
            return;
        }

        pending.Source.TrySetResult(new DiscordBrowserAuthorizationResult(
            true,
            code,
            pending.CodeVerifier,
            RedirectUri,
            string.Empty));
    }

    public static void Cancel(string error)
    {
        PendingAuthorization? pending;
        lock (Sync)
        {
            pending = _pending;
            _pending = null;
        }
        pending?.Source.TrySetResult(Failure(error));
    }

    private static DiscordBrowserAuthorizationResult Failure(string error) =>
        new(false, string.Empty, string.Empty, RedirectUri, error);

    private static string BuildAuthorizationUrl(
        string applicationId,
        string state,
        string challenge)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = applicationId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid sdk.social_layer_presence",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        var query = string.Join('&', parameters.Select(pair =>
            $"{System.Uri.EscapeDataString(pair.Key)}={System.Uri.EscapeDataString(pair.Value)}"));
        return $"https://discord.com/oauth2/authorize?{query}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

[Activity(
    Name = "com.tomize.deezerrpc.DiscordOAuthCallbackActivity",
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    NoHistory = true,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = DiscordOAuthBrowserFlow.CallbackScheme,
    DataPath = DiscordOAuthBrowserFlow.CallbackPath)]
public sealed class DiscordOAuthCallbackActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleCallback(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleCallback(intent);
    }

    private void HandleCallback(Intent? intent)
    {
        DiscordOAuthBrowserFlow.Complete(intent?.Data);
        var appIntent = new Intent(this, typeof(MainActivity));
        appIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(appIntent);
        Finish();
    }
}
