using Android.App;

namespace DeezerRpc.Android;

internal static class DiscordSocialSdkInitializer
{
    private const string JavaClassName = "com.discord.socialsdk.DiscordSocialSdkInit";
    private static readonly object Sync = new();

    public static bool IsInitialized { get; private set; }

    public static bool TryInitialize(Activity activity, out string error)
    {
        lock (Sync)
        {
            try
            {
                using var javaClass = Java.Lang.Class.ForName(JavaClassName, true, activity.ClassLoader);
                using var activityClass = Java.Lang.Class.FromType(typeof(Activity));
                using var method = javaClass.GetDeclaredMethod("setEngineActivity", [activityClass]);
                method.Invoke(null, [activity]);
                IsInitialized = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                IsInitialized = false;
                error = $"Initialisation Discord impossible : {exception.Message}";
                return false;
            }
        }
    }
}
