using System.Text.Json.Serialization;

public class LegoColorResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("previous")]
    public string? Previous { get; set; }
    public List<LegoColor>? Results { get; set; }
}