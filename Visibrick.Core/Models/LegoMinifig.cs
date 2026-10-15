using System.Text.Json.Serialization;
using SQLite;

namespace Visibrick.Core.Models;

// A minifig from BrickLink's catalog. JSON names match the Supabase "minifigs" table.
public class LegoMinifig
{
    // BrickLink item number, e.g. "sw0001a"
    [PrimaryKey]
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category_id")]
    public int? CategoryId { get; set; }

    // e.g. "Star Wars / Star Wars Episode 1"
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    // Null when BrickLink lists the year as unknown ("?")
    [JsonPropertyName("year_released")]
    public int? YearReleased { get; set; }

    [JsonPropertyName("weight_grams")]
    public double? WeightGrams { get; set; }

    // BrickLink serves an image for every catalog item at a predictable address
    [Ignore]
    [JsonIgnore]
    public string? ImageUrl => Number is null ? null : $"https://img.bricklink.com/ItemImage/MN/0/{Number}.png";

    // The minifig's page on BrickLink
    [Ignore]
    [JsonIgnore]
    public string? BrickLinkUrl => Number is null ? null : $"https://www.bricklink.com/v2/catalog/catalogitem.page?M={Uri.EscapeDataString(Number)}";
}
