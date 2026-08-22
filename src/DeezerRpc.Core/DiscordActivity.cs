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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LargeImage { get; init; }

    [JsonPropertyName("large_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LargeText { get; init; }

    [JsonPropertyName("small_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SmallImage { get; init; }

    [JsonPropertyName("small_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SmallText { get; init; }
}

public sealed class DiscordButton
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}
