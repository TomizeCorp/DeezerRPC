using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace DeezerRpc.Core;

public sealed class DeezerCatalogClient : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://api.deezer.com/"),
        Timeout = TimeSpan.FromSeconds(8)
    };
    private readonly Dictionary<string, ResolvedDeezerTrack?> _cache = new(StringComparer.Ordinal);

    public DeezerCatalogClient() => _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DeezerRPC/1.0");

    public async Task<NowPlayingTrack?> EnrichAsync(
        NowPlayingTrack track,
        bool requireCatalogMatch,
        CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(track.Identity, out var resolved))
        {
            resolved = await ResolveAsync(track, cancellationToken);
            _cache[track.Identity] = resolved;
        }

        if (resolved is null)
        {
            return requireCatalogMatch ? null : track;
        }

        return track with
        {
            Album = string.IsNullOrWhiteSpace(track.Album) ? resolved.Album : track.Album,
            Duration = track.Duration > TimeSpan.Zero
                ? track.Duration
                : TimeSpan.FromSeconds(resolved.DurationSeconds),
            // The media session artwork is the exact cover currently shown by Deezer.
            // Keep it ahead of a catalog match, which can point at another remix/release.
            CoverUrl = track.CoverUrl ?? resolved.CoverUrl,
            TrackUrl = resolved.TrackUrl
        };
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ResolvedDeezerTrack?> ResolveAsync(NowPlayingTrack track, CancellationToken cancellationToken)
    {
        try
        {
            var albumClause = string.IsNullOrWhiteSpace(track.Album)
                ? string.Empty
                : $" album:\"{SanitizeQueryValue(track.Album)}\"";
            var baseQuery = $"track:\"{SanitizeQueryValue(track.Title)}\" artist:\"{SanitizeQueryValue(track.Artist)}\"";
            var response = await SearchAsync(baseQuery + albumClause, cancellationToken);
            var resolved = SelectBest(track, response, requireAlbumMatch: true);
            if (resolved is null && !string.IsNullOrWhiteSpace(albumClause))
            {
                // Deezer's strict search sometimes rejects equivalent album labels such as
                // deluxe/remastered editions. Retry broadly, then validate the album locally.
                response = await SearchAsync(baseQuery, cancellationToken);
                resolved = SelectBest(track, response, requireAlbumMatch: true);
                resolved ??= SelectBest(track, response, requireAlbumMatch: false);
            }

            return resolved;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<SearchResponse?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var path = $"search?q={Uri.EscapeDataString(query)}&limit=25&strict=on";
        return await _httpClient.GetFromJsonAsync<SearchResponse>(path, cancellationToken);
    }

    private static ResolvedDeezerTrack? SelectBest(
        NowPlayingTrack track,
        SearchResponse? response,
        bool requireAlbumMatch) =>
        response?.Data
            .Select(item => new { Item = item, Score = Score(track, item, requireAlbumMatch) })
            .Where(candidate => candidate.Score >= 0.86)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => ToResolved(candidate.Item))
            .FirstOrDefault(candidate => candidate is not null);

    private static double Score(NowPlayingTrack source, SearchItem candidate, bool requireAlbumMatch)
    {
        var sourceTitle = Normalize(source.Title);
        var candidateTitle = Normalize(candidate.Title ?? string.Empty);
        var candidateShortTitle = Normalize(candidate.TitleShort ?? string.Empty);
        var sourceArtist = Normalize(source.Artist);
        var sourceAlbum = Normalize(source.Album);
        var candidateAlbum = Normalize(candidate.Album?.Title ?? string.Empty);

        var titleScore = sourceTitle == candidateTitle
            ? 1.0
            : sourceTitle == candidateShortTitle
                ? 0.9
                : candidateTitle.StartsWith(sourceTitle + " feat ", StringComparison.Ordinal) ||
                  sourceTitle.StartsWith(candidateTitle + " feat ", StringComparison.Ordinal)
                    ? 0.96
                : SimilarityByContainment(sourceTitle, candidateTitle, 0.8);
        var artistScore = ScoreArtist(sourceArtist, candidate);
        if (titleScore < 0.86 || artistScore < 0.82)
        {
            return -1;
        }

        if (string.IsNullOrEmpty(sourceAlbum))
        {
            return (titleScore * 0.7) + (artistScore * 0.3);
        }

        var albumScore = sourceAlbum == candidateAlbum
            ? 1.0
            : Math.Max(
                SimilarityByContainment(sourceAlbum, candidateAlbum, 0.9),
                SimilarityByTokens(sourceAlbum, candidateAlbum, 0.88));
        if (requireAlbumMatch && albumScore < 0.86)
        {
            // A matching title from a single, compilation or remix can have a different cover.
            // When Deezer exposes the current album, never borrow artwork from another release.
            return -1;
        }

        return requireAlbumMatch
            ? (titleScore * 0.6) + (artistScore * 0.25) + (albumScore * 0.15)
            : (titleScore * 0.68) + (artistScore * 0.27) + (albumScore * 0.05);
    }

    private static double SimilarityByContainment(string left, string right, double score)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        var shorter = Math.Min(left.Length, right.Length);
        var longer = Math.Max(left.Length, right.Length);
        var closeInLength = shorter / (double)longer >= 0.72;
        return closeInLength &&
            (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
                ? score
                : 0;
    }

    private static double ScoreArtist(string sourceArtist, SearchItem candidate)
    {
        var candidates = new[] { candidate.Artist?.Name }
            .Concat(candidate.Contributors?.Select(contributor => contributor.Name) ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Normalize(name!))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Any(name => name == sourceArtist))
        {
            return 1.0;
        }

        var combined = string.Join(' ', candidates);
        if (combined == sourceArtist)
        {
            return 1.0;
        }

        var sourceTokens = sourceArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var primaryTokens = candidates.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        if (primaryTokens.Count > 0 && primaryTokens.IsSubsetOf(sourceTokens))
        {
            // Search results expose only the primary artist for many collaborations,
            // e.g. David Guetta instead of David Guetta & Bebe Rexha.
            return 0.92;
        }

        return Math.Max(
            candidates.Select(name => SimilarityByContainment(sourceArtist, name, 0.88)).DefaultIfEmpty(0).Max(),
            SimilarityByTokens(sourceArtist, combined, 0.9));
    }

    private static double SimilarityByTokens(string left, string right, double score)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "avec", "bonus", "deluxe", "edition", "expanded", "explicit", "feat", "featuring",
            "remaster", "remastered", "version", "with"
        };
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !ignored.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !ignored.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        return intersection / (double)union >= 0.7 ? score : 0;
    }

    private static ResolvedDeezerTrack? ToResolved(SearchItem item)
    {
        if (!Uri.TryCreate(item.Link, UriKind.Absolute, out var trackUrl) ||
            !Uri.TryCreate(item.Album?.CoverXl, UriKind.Absolute, out var coverUrl))
        {
            return null;
        }

        return new ResolvedDeezerTrack(
            item.Album?.Title ?? string.Empty,
            item.Duration,
            coverUrl,
            trackUrl);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    private static string SanitizeQueryValue(string value) => value.Replace('"', ' ').Trim();

    private sealed record ResolvedDeezerTrack(string Album, int DurationSeconds, Uri CoverUrl, Uri TrackUrl);

    private sealed class SearchResponse
    {
        [JsonPropertyName("data")]
        public List<SearchItem> Data { get; init; } = [];
    }

    private sealed class SearchItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("title_short")]
        public string? TitleShort { get; init; }

        [JsonPropertyName("duration")]
        public int Duration { get; init; }

        [JsonPropertyName("link")]
        public string? Link { get; init; }

        [JsonPropertyName("artist")]
        public SearchArtist? Artist { get; init; }

        [JsonPropertyName("contributors")]
        public List<SearchArtist>? Contributors { get; init; }

        [JsonPropertyName("album")]
        public SearchAlbum? Album { get; init; }
    }

    private sealed class SearchArtist
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class SearchAlbum
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("cover_xl")]
        public string? CoverXl { get; init; }
    }
}
