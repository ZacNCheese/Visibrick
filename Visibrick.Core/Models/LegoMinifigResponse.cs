using System.Text.Json.Serialization;

namespace Visibrick.Core.Models;

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
