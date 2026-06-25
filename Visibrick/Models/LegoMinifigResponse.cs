using System.Text.Json.Serialization;
using SQLite;

public class LegoMinifigResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("previous")]
    public string? Previous { get; set; }
    public List<LegoMinifig>? Results { get; set; }
}