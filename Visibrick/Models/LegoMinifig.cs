using System.Text.Json.Serialization;
using SQLite;

public class LegoMinifig
{
    /*
    "set_num": "fig-000001",
      "name": "Toy Store Employee",
      "num_parts": 4,
      "set_img_url": "https://cdn.rebrickable.com/media/sets/fig-000001/55726.jpg",
      "set_url": "https://rebrickable.com/minifigs/fig-000001/toy-store-employee/",
      "last_modified_dt": "2020-05-27T21:47:00.694941Z"
    */

    [PrimaryKey]
    [JsonPropertyName("set_num")]
    public string? MinifigNumber { get; set; }

    [JsonPropertyName("name")]
    public string? MinifigName { get; set; }

    [JsonPropertyName("num_parts")]
    public int MinifigNumParts {get; set;}

    [JsonPropertyName("set_img_url")]
    public string? MinifigImageUrl { get; set; }

    [JsonPropertyName("set_url")]
    public string? MinifigUrl { get; set; }

    [JsonPropertyName("last_modified_dt")]
    public DateTime LastModified {get; set;}


}