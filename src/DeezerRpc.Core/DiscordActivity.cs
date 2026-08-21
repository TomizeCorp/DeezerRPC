using System.Text.Json.Serialization;

namespace DeezerRpc.Core;

public sealed class DiscordActivity
{
    [JsonPropertyName("type")]
    public int Type { get; init; } = 2;

    [JsonPropertyName("details")]
    public required string Details { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("timestamps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordTimestamps? Timestamps { get; init; }

    [JsonPropertyName("assets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordAssets? Assets { get; init; }

    [JsonPropertyName("buttons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DiscordButton>? Buttons { get; init; }
}

public sealed class DiscordTimestamps
{
    [JsonPropertyName("start")]
    public required long Start { get; init; }

    [JsonPropertyName("end")]
    public required long End { get; init; }
}

public sealed class DiscordAssets
{
    [JsonPropertyName("large_image")]
    public required string LargeImage { get; init; }

    [JsonPropertyName("large_text")]
    public required string LargeText { get; init; }

    // Intentionally no small_image/small_text: the album cover must remain alone.
}

public sealed class DiscordButton
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

