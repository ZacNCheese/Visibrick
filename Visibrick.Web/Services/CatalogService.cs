using System.Text.Json.Serialization;
using Visibrick.Core.Models;

namespace Visibrick.Web.Services;

// Reads the BrickLink catalog from Supabase (public data: just the anon key). Searching and paging happen
// on the server, so the browser only ever downloads the page being shown.
public class CatalogService
{
    private readonly SupabaseRest _rest;
    private Dictionary<string, int>? _counts;
    private readonly Dictionary<string, List<Category>> _categories = new();

    public CatalogService(SupabaseRest rest)
    {
        _rest = rest;
    }

    // How many items each type has (from the last import), keyed by type code
    public async Task<Dictionary<string, int>> GetCountsAsync()
    {
        if (_counts is not null)
            return _counts;

        var rows = await _rest.GetAsync<ImportState>("rest/v1/import_state?select=kind,row_count");
        return _counts = rows.ToDictionary(r => r.Kind, r => r.RowCount);
    }

    // The categories of a type, biggest first: the top-level ones (top = null), or the sub-categories of one.
    // Counted once per import (catalog_categories), and cached here since they only change when the catalog does.
    public async Task<List<Category>> GetCategoriesAsync(CatalogType type, string? top = null)
    {
        var key = $"{type.Code}|{top}";
        if (_categories.TryGetValue(key, out var cached))
            return cached;

        var path = $"rest/v1/catalog_categories?select=top,sub,item_count&item_type=eq.{type.Code}" +
            (top is null ? "&sub=is.null" : $"&top=eq.{Quote(top)}&sub=not.is.null") +
            "&order=item_count.desc,top.asc,sub.asc";

        return _categories[key] = await _rest.GetAsync<Category>(path);
    }

    // One page of a type: parts most-used first, everything else newest first.
    // search matches the name or number (case-insensitive); top and sub limit it to a category.
    public Task<(List<CatalogItem> Rows, int Total)> SearchAsync(CatalogType type, string? search, int skip, int take,
        string? top = null, string? sub = null)
    {
        var order = type == CatalogType.Parts ? "set_count.desc.nullslast,number.asc" : "year_released.desc.nullslast,number.asc";
        var path = $"rest/v1/catalog_items?select=*&item_type=eq.{type.Code}" +
            (top is null ? "" : $"&category_top=eq.{Quote(top)}") +
            (sub is null ? "" : $"&category_sub=eq.{Quote(sub)}") +
            SearchFilter(search, "name", "number") +
            $"&order={order}&limit={take}&offset={skip}";

        return _rest.GetPageAsync<CatalogItem>(path);
    }

    public async Task<CatalogItem?> GetAsync(string type, string number)
    {
        var rows = await _rest.GetAsync<CatalogItem>(
            $"rest/v1/catalog_items?select=*&item_type=eq.{Uri.EscapeDataString(type)}&number=eq.{Quote(number)}&limit=1");
        return rows.FirstOrDefault();
    }

    // An "or" filter matching any of the columns, e.g. &or=(name.ilike."*batman*",number.ilike."*batman*")
    public static string SearchFilter(string? search, params string[] columns)
    {
        if (string.IsNullOrWhiteSpace(search))
            return "";

        // Characters that mean something in Supabase's filter syntax aren't useful in a search anyway
        var text = new string(search.Trim().Where(c => c is not ('"' or '\\' or '*' or ',' or '(' or ')')).ToArray());
        if (text.Length == 0)
            return "";

        var conditions = string.Join(",", columns.Select(c => $"{c}.ilike.\"*{text}*\""));
        return "&or=" + Uri.EscapeDataString($"({conditions})");
    }

    // A value for an eq. filter. Supabase takes everything after "eq." literally (quotes are only special inside
    // in.(...) and or=(...) lists), so it just needs URL-encoding.
    public static string Quote(string value) => Uri.EscapeDataString(value);

    // A category chip: Top alone for a top-level category, Top + Sub for a sub-category
    public record Category(
        [property: JsonPropertyName("top")] string Top,
        [property: JsonPropertyName("sub")] string? Sub,
        [property: JsonPropertyName("item_count")] int ItemCount);

    private record ImportState(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("row_count")] int RowCount);
}
