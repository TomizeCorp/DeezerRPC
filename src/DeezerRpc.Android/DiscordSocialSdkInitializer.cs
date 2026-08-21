using Android.App;
using Android.Runtime;

namespace DeezerRpc.Android;

internal static class DiscordSocialSdkInitializer
{
    private const string JavaClassName = "com/discord/socialsdk/DiscordSocialSdkInit";
    private static readonly object Sync = new();

    public static bool IsInitialized { get; private set; }

    public static bool TryInitialize(Activity activity, out string error)
    {
        lock (Sync)
        {
            nint javaClass = 0;
            try
            {
                javaClass = JNIEnv.FindClass(JavaClassName);
                var method = JNIEnv.GetStaticMethodID(
                    javaClass,
                    "setEngineActivity",
                    "(Landroid/app/Activity;)V");
                JNIEnv.CallStaticVoidMethod(javaClass, method, [new JValue(activity)]);
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
            finally
            {
                if (javaClass != 0)
                {
                    JNIEnv.DeleteLocalRef(javaClass);
                }
            }
        }
    }
}
