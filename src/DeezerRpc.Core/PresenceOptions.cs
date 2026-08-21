namespace DeezerRpc.Core;

public sealed record PresenceOptions
{
    public bool ShowAlbum { get; init; } = true;
    public bool ShowProgress { get; init; } = true;
    public bool ShowDeezerButton { get; init; } = true;
    public bool ShowPauseState { get; init; } = true;
}
