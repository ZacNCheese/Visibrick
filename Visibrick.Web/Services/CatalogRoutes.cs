using Visibrick.Core.Models;

namespace Visibrick.Web.Services;

// Web addresses for the catalog: /catalog/minifigs, /catalog/sets, ... and /item?type=S&number=75192-1
public static class CatalogRoutes
{
    public static string Slug(CatalogType type) => type.Name.ToLowerInvariant();

    public static CatalogType? FromSlug(string? slug)
        => CatalogType.All.FirstOrDefault(t => Slug(t) == slug?.ToLowerInvariant());

    public static string List(CatalogType type) => "catalog/" + Slug(type);

    // Item numbers can contain characters like "." that would confuse a path, so they go in the query
    public static string Item(string type, string number)
        => $"item?type={Uri.EscapeDataString(type)}&number={Uri.EscapeDataString(number)}";

    public static string Icon(CatalogType type) => $"images/icon_{Slug(type)}.svg";

    public static string Singular(CatalogType type) => type.Code switch
    {
        "M" => "Minifig",
        "S" => "Set",
        "P" => "Part",
        "G" => "Gear",
        "B" => "Book",
        _ => type.Name
    };
}
