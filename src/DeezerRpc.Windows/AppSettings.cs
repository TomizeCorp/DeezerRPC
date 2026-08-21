namespace DeezerRpc.Windows;

internal sealed record AppSettings
{
    public bool RichPresenceEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool EnableBrowserDetection { get; init; }
    public bool ShowAlbum { get; init; } = true;
    public bool ShowProgress { get; init; } = true;
    public bool ShowDeezerButton { get; init; } = true;
    public bool KeepRunningInBackground { get; init; } = true;
    public bool ShowNotifications { get; init; } = true;
    public int PollIntervalMilliseconds { get; init; } = 1_000;
}
