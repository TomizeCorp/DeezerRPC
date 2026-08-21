using System.Text.Json;
using DeezerRpc.Core;

var now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);
var builder = new DiscordActivityBuilder();
Assert(AppIdentity.DiscordApplicationId == "1540336569532031116", "L’Application ID Discord doit être intégré à l’application.");

var playing = new NowPlayingTrack
{
    Title = "Faded",
    Artist = "Alan Walker",
    Album = "Different World",
    Duration = TimeSpan.FromSeconds(212),
    Position = TimeSpan.FromSeconds(102),
    ObservedAt = now,
    Status = PlaybackStatus.Playing,
    CoverUrl = new Uri("https://cdn.example.test/cover.jpg"),
    TrackUrl = new Uri("https://www.deezer.com/track/123")
};

var activity = builder.Build(playing, now);
var json = JsonSerializer.Serialize(activity);
Assert(activity.Details == "Faded", "Le titre doit occuper details.");
Assert(activity.State == "Alan Walker", "La ligne state doit contenir uniquement l’artiste.");
Assert(activity.Timestamps?.Start == now.AddSeconds(-102).ToUnixTimeSeconds(), "Le début Discord doit refléter la position.");
Assert(activity.Timestamps?.End == now.AddSeconds(110).ToUnixTimeSeconds(), "La fin Discord doit refléter la durée.");
Assert(activity.Assets?.LargeImage == playing.CoverUrl.AbsoluteUri, "La pochette doit être large_image.");
Assert(!json.Contains("small_image", StringComparison.Ordinal), "small_image est strictement interdit.");
Assert(!json.Contains("small_text", StringComparison.Ordinal), "small_text est strictement interdit.");
Assert(activity.Buttons is [{ Label: "Écouter sur Deezer" }], "Le bouton Deezer doit être présent.");

AssertThrows<InvalidOperationException>(
    () => builder.Build(playing with { Status = PlaybackStatus.Paused }, now),
    "Une piste en pause ne doit jamais produire d’activité Discord.");

var minimal = builder.Build(playing, now, new PresenceOptions
{
    ShowAlbum = false,
    ShowProgress = false,
    ShowDeezerButton = false
});
Assert(minimal.State == "Alan Walker", "L’album ne doit jamais être répété à côté de l’artiste.");
Assert(minimal.Timestamps is null, "La progression doit pouvoir être masquée.");
Assert(minimal.Buttons is null, "Le bouton Deezer doit pouvoir être masqué.");
Assert(minimal.Assets?.LargeImage == playing.CoverUrl.AbsoluteUri, "La pochette reste la seule image dans tous les modes.");

var withoutAlbum = builder.Build(playing with { Album = string.Empty }, now);
Assert(withoutAlbum.State == "Alan Walker", "Un album indisponible ne doit pas afficher de texte de remplacement.");
Assert(!withoutAlbum.State.Contains("Album inconnu", StringComparison.Ordinal), "La Rich Presence ne doit jamais inventer un album.");
Assert(DeezerLinks.GetListenUri(playing) == playing.TrackUrl, "Le bouton doit privilégier le lien direct du morceau.");
var searchLink = DeezerLinks.GetListenUri(playing with { TrackUrl = null });
Assert(searchLink.Host == "www.deezer.com" && searchLink.AbsolutePath.StartsWith("/search/", StringComparison.Ordinal), "Une recherche Deezer doit servir de solution de secours.");

var withoutRemoteAssets = builder.Build(
    playing with
    {
        CoverUrl = new Uri("file:///C:/cover.jpg"),
        TrackUrl = new Uri("http://localhost:8080/track")
    },
    now);
Assert(withoutRemoteAssets.Assets is null, "Une image locale ne doit pas être envoyée à Discord.");
Assert(withoutRemoteAssets.Buttons is null, "Un lien local ne doit pas devenir un bouton public.");

Assert(playing.ProjectPosition(now.AddSeconds(10)) == TimeSpan.FromSeconds(112), "La position doit progresser pendant la lecture.");
Assert(
    playing with { Position = TimeSpan.FromSeconds(210) } is var nearEnd &&
    nearEnd.ProjectPosition(now.AddSeconds(10)) == playing.Duration,
    "La position projetée doit être bornée par la durée.");

if (Environment.GetEnvironmentVariable("DEEZERRPC_LIVE_TESTS") == "1")
{
    using var catalog = new DeezerCatalogClient();
    var collaboration = await catalog.EnrichAsync(
        new NowPlayingTrack
        {
            Title = "I'm Good (Blue)",
            Artist = "David Guetta & Bebe Rexha",
            Album = "I'm Good (Blue)",
            Status = PlaybackStatus.Playing
        },
        requireCatalogMatch: false,
        CancellationToken.None);
    Assert(collaboration?.CoverUrl is not null, "Une collaboration David Guetta doit conserver sa pochette.");
    Assert(collaboration?.TrackUrl is not null, "Une collaboration David Guetta doit obtenir son lien Deezer.");
}

Console.WriteLine("Tous les tests DeezerRpc.Core ont réussi.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
