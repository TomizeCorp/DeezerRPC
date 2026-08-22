using DeezerRpc.Core;
using DeezerRpc.Windows;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await using var discord = new DiscordRpcClient(AppIdentity.DiscordApplicationId);
await discord.ConnectAsync(timeout.Token);
if (!discord.IsConnected)
{
    throw new InvalidOperationException("Le canal RPC Discord Desktop n’est pas connecté.");
}

if (discord.Account is null || string.IsNullOrWhiteSpace(discord.Account.UserId))
{
    throw new InvalidOperationException("Le compte Discord Desktop n’a pas été identifié.");
}

var now = DateTimeOffset.UtcNow;
var activity = new DiscordActivityBuilder().Build(new NowPlayingTrack
{
    Title = "Test Deezer Presence",
    Artist = "Vérification Windows",
    Album = "Lien Deezer",
    Duration = TimeSpan.FromMinutes(3),
    Position = TimeSpan.FromSeconds(30),
    Status = PlaybackStatus.Playing,
    ObservedAt = now,
    CoverUrl = new Uri("https://e-cdns-images.dzcdn.net/images/cover/264f5b9b00632c6319eaa6b93a6c34f5/1000x1000-000000-80-0-0.jpg"),
    TrackUrl = new Uri("https://www.deezer.com/track/3135556"),
    SourceId = "integration-test"
}, now);
await discord.SetActivityAsync(activity, timeout.Token);
await discord.ClearActivityAsync(timeout.Token);

Console.WriteLine("Connexion, compte et liens Discord Desktop vérifiés.");
