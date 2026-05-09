using System.Text.Json.Serialization;

namespace KaoyanEnglishMod.Vocab;

public sealed class KaoyanWord
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("rank")]
    public int Rank { get; init; }

    [JsonPropertyName("word")]
    public string Word { get; init; } = string.Empty;

    [JsonPropertyName("zh_meaning")]
    public string ZhMeaning { get; init; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; init; } = string.Empty;

    [JsonPropertyName("spawn_weight")]
    public int SpawnWeight { get; init; }
}
