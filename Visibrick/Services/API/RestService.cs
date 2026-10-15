using System.Diagnostics;
using System.Text.Json;
using Visibrick.Data;
using Visibrick.Core.Models;

namespace Visibrick.Services.API;

public class RestService
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly LegoMinifigRepository _repo;

    public RestService(LegoMinifigRepository repo)
    {
        _client = new HttpClient();
        _repo = repo;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<List<LegoMinifig>> GetLegoMinifigsAsync()
    {
        var allMinifigs = new List<LegoMinifig>();

        // 👇 get latest modified from DB
        var lastSync = await _repo.GetLatestModifiedAsync();

        string? url = "https://rebrickable.com/api/v3/lego/minifigs/?key=2d2ec2792fd89d2b412f795f5705ac5f&page_size=5000";

        while (!string.IsNullOrEmpty(url))
        {
            var response = await _client.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Debug.WriteLine("RATE LIMITED - waiting...");
                await Task.Delay(2000);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LegoMinifigResponse>(json, _serializerOptions);

            if (result != null)
            {
                allMinifigs.AddRange(result.Results);

                url = result.Next != null
                    ? result.Next + "&key=2d2ec2792fd89d2b412f795f5705ac5f"
                    : null;
            }
        }

        return allMinifigs;
    }
}
