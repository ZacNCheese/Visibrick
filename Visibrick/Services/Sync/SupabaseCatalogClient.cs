using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Visibrick.Core.Models;

namespace Visibrick.Services.Sync;

// Reads the catalog from Supabase's REST API (read-only, using the public anon key)
public class SupabaseCatalogClient
{
    // Supabase returns at most 1,000 rows per request by default, so large catalogs are fetched in pages
    private const int PageSize = 1000;

    // Pages downloaded at the same time; parts alone are ~97 pages
    private const int ParallelPages = 4;

    private readonly HttpClient _http;

    public SupabaseCatalogClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(SupabaseConfig.Url + "/rest/v1/") };
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.AnonKey);
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(SupabaseConfig.AnonKey);

    // What Visibrick.Sync last imported for each item type, keyed by type code (M, S, P, G, B)
    public async Task<Dictionary<string, ImportState>> GetImportStateAsync()
    {
        var rows = await GetAsync<List<ImportState>>("import_state?select=kind,file_hash,row_count");
        return rows.ToDictionary(r => r.Kind);
    }

    // Every item of one type. Checks the total against what the import reported, so a partial download
    // (e.g. a lower row limit on the Supabase project) fails loudly instead of saving half a catalog.
    public async Task<List<CatalogItem>> GetItemsAsync(string type, int expectedCount)
    {
        var pageCount = (expectedCount + PageSize - 1) / PageSize;
        var pages = new List<CatalogItem>[pageCount];
        using var gate = new SemaphoreSlim(ParallelPages);

        await Task.WhenAll(Enumerable.Range(0, pageCount).Select(async page =>
        {
            await gate.WaitAsync();
            try
            {
                // Ordered by the primary key so every page is stable
                pages[page] = await GetAsync<List<CatalogItem>>(
                    $"catalog_items?select=*&item_type=eq.{Uri.EscapeDataString(type)}&order=number&limit={PageSize}&offset={page * PageSize}");
            }
            finally
            {
                gate.Release();
            }
        }));

        var all = pages.SelectMany(p => p).ToList();

        if (all.Count != expectedCount)
            throw new InvalidOperationException($"Downloaded {all.Count} of {expectedCount} items of type {type}");

        return all;
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public record ImportState(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("file_hash")] string FileHash,
        [property: JsonPropertyName("row_count")] int RowCount);
}
