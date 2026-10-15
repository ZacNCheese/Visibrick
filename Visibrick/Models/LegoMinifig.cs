using System.Text.Json.Serialization;
using SQLite;

namespace Visibrick.Models;

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
    public string? Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("num_parts")]
    public int NumParts { get; set; }

    [JsonPropertyName("set_img_url")]
    public string? ImageUrl { get; set; }

    // Rebrickable's CDN serves small thumbnails of every image, e.g.
    // .../media/sets/fig-000001.jpg (~2 MB) -> .../media/thumbs/sets/fig-000001.jpg/250x250p.jpg (~50 KB)
    [Ignore]
    [JsonIgnore]
    public string? ThumbnailUrl => ImageUrl?.Contains("/media/") == true
        ? ImageUrl.Replace("/media/", "/media/thumbs/") + "/250x250p.jpg"
        : ImageUrl;

    [JsonPropertyName("set_url")]
    public string? Url { get; set; }

    [JsonPropertyName("last_modified_dt")]
    public DateTime LastModified { get; set; }
}
