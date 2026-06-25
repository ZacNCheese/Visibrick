using System.Text.Json.Serialization;
using SQLite;

public class LegoColor
{
    //[PrimaryKey] [AutoIncrement]
    //public int AutoID {get; set;}

    [PrimaryKey]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? ColorName { get; set; }

    [JsonPropertyName("rgb")]
    public string? ColorRGB { get; set; }

    [JsonPropertyName("is_trans")]
    public bool Is_Transluscent { get; set; }
}