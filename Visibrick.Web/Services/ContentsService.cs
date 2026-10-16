using System.Text.Json.Serialization;
using Visibrick.Core.Models;

namespace Visibrick.Web.Services;

// What's inside things, from Rebrickable's data (see Visibrick.Sync): the parts and minifigs in a set,
// the parts a minifig is made of, and where a part or minifig appears. Public data: just the anon key.
public class ContentsService
{
    // Supabase returns at most 1,000 rows per request
    private const int PageSize = 1000;

    private readonly SupabaseRest _rest;

    public ContentsService(SupabaseRest rest)
    {
        _rest = rest;
    }

    // A set's parts, spares included (IsSpare), grouped by color
    public Task<List<PartUse>> GetSetPartsAsync(string setNumber)
        => GetAllAsync<PartUse>($"rest/v1/set_part_list?select=*&set_num=eq.{CatalogService.Quote(setNumber)}&order=color_name.asc,part_num.asc");

    // The parts a BrickLink minifig is made of
    public Task<List<PartUse>> GetMinifigPartsAsync(string minifigNumber)
        => GetAllAsync<PartUse>($"rest/v1/minifig_part_list?select=*&minifig_number=eq.{CatalogService.Quote(minifigNumber)}&order=part_num.asc");

    public Task<List<SetMinifig>> GetSetMinifigsAsync(string setNumber)
        => GetAllAsync<SetMinifig>($"rest/v1/set_minifigs?select=*&set_num=eq.{CatalogService.Quote(setNumber)}&order=name.asc");

    // Sets that come inside this set (bundles and value packs)
    public async Task<List<CatalogItem>> GetSetSubsetsAsync(string setNumber)
        => (await GetAllAsync<SetRef>($"rest/v1/set_subset_list?select=*&set_num=eq.{CatalogService.Quote(setNumber)}&order=child_set_num.asc"))
            .Select(s => s.ToCatalogItem(s.ChildSetNumber)).ToList();

    // The sets a BrickLink minifig appears in, newest first
    public async Task<List<CatalogItem>> GetMinifigSetsAsync(string minifigNumber)
        => (await GetAllAsync<SetRef>($"rest/v1/minifig_set_list?select=*&minifig_number=eq.{CatalogService.Quote(minifigNumber)}&order=year_released.desc.nullslast,set_num.asc"))
            .Select(s => s.ToCatalogItem(s.SetNumber)).ToList();

    // One page of the sets a part appears in, newest first (common parts are in thousands of sets)
    public async Task<(List<CatalogItem> Rows, int Total)> GetPartSetsAsync(string partNumber, int skip, int take)
    {
        var (rows, total) = await _rest.GetPageAsync<SetRef>(
            $"rest/v1/part_set_list?select=*&part_num=eq.{CatalogService.Quote(partNumber)}&order=year_released.desc.nullslast,set_num.asc&limit={take}&offset={skip}");
        return (rows.Select(s => s.ToCatalogItem(s.SetNumber)).ToList(), total);
    }

    // The colors a part comes in, most used first
    public Task<List<PartColor>> GetPartColorsAsync(string partNumber)
        => GetAllAsync<PartColor>($"rest/v1/part_color_list?select=*&part_num=eq.{CatalogService.Quote(partNumber)}&order=set_count.desc");

    private async Task<List<T>> GetAllAsync<T>(string path)
    {
        var all = new List<T>();
        while (true)
        {
            var page = await _rest.GetAsync<T>($"{path}&limit={PageSize}&offset={all.Count}");
            all.AddRange(page);
            if (page.Count < PageSize)
                return all;
        }
    }

    // A row of set_subset_list, minifig_set_list or part_set_list
    private class SetRef
    {
        [JsonPropertyName("set_num")] public string SetNumber { get; set; } = "";
        [JsonPropertyName("child_set_num")] public string ChildSetNumber { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("year_released")] public int? YearReleased { get; set; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
        [JsonPropertyName("num_parts")] public int? NumParts { get; set; }

        public CatalogItem ToCatalogItem(string number) => new()
        {
            Type = CatalogType.Sets.Code,
            Number = number,
            Name = Name ?? number,
            YearReleased = YearReleased,
            SourceImageUrl = ImageUrl,
            NumParts = NumParts
        };
    }
}

// A part in a set or minifig, in one color
public class PartUse
{
    [JsonPropertyName("part_num")] public string PartNumber { get; set; } = "";
    [JsonPropertyName("part_name")] public string? PartName { get; set; }
    [JsonPropertyName("color_id")] public int ColorId { get; set; }
    [JsonPropertyName("color_name")] public string? ColorName { get; set; }
    [JsonPropertyName("rgb")] public string? Rgb { get; set; }
    [JsonPropertyName("is_trans")] public bool IsTrans { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("is_spare")] public bool IsSpare { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
}

// A minifig in a set. BrickLinkNumber links to the minifig's page; null when BrickLink doesn't have it.
public class SetMinifig
{
    [JsonPropertyName("fig_num")] public string RebrickableNumber { get; set; } = "";
    [JsonPropertyName("bricklink_number")] public string? BrickLinkNumber { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
}

// A color a part comes in
public class PartColor
{
    [JsonPropertyName("color_id")] public int ColorId { get; set; }
    [JsonPropertyName("color_name")] public string? ColorName { get; set; }
    [JsonPropertyName("rgb")] public string? Rgb { get; set; }
    [JsonPropertyName("is_trans")] public bool IsTrans { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("set_count")] public int SetCount { get; set; }
}
