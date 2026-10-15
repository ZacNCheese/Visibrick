using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Visibrick.Services.Sync;

// Reads the catalog tables from Supabase's REST API (read-only, using the public anon key)
public class SupabaseCatalogClient
{
    // Supabase returns at most 1,000 rows per request by default, so large tables are fetched in pages
    private const int PageSize = 1000;

    private readonly HttpClient _http;

    public SupabaseCatalogClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(SupabaseConfig.Url + "/rest/v1/") };
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.AnonKey);
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(SupabaseConfig.AnonKey);

    // What Visibrick.Sync last imported for each catalog (e.g. "minifigs")
    public async Task<Dictionary<string, ImportState>> GetImportStateAsync()
    {
        var rows = await GetAsync<List<ImportState>>("import_state?select=kind,file_hash,row_count");
        return rows.ToDictionary(r => r.Kind);
    }

    // Every row of a table. Checks the total against what the import reported, so a partial download
    // (e.g. a lower row limit on the Supabase project) fails loudly instead of saving half a catalog.
    public async Task<List<T>> GetAllAsync<T>(string table, string orderBy, int expectedCount)
    {
        var all = new List<T>(expectedCount);

        while (all.Count < expectedCount)
        {
            var page = await GetAsync<List<T>>($"{table}?select=*&order={orderBy}&limit={PageSize}&offset={all.Count}");
            if (page.Count == 0)
                break;

            all.AddRange(page);
        }

        if (all.Count != expectedCount)
            throw new InvalidOperationException($"Downloaded {all.Count} of {expectedCount} rows from {table}");

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
