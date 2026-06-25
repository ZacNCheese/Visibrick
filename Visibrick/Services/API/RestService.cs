namespace VisiBrick;
using System.Text.Json;
using System.Diagnostics;
using System.Net;

public class RestService
{
    HttpClient _client;
    JsonSerializerOptions _serializerOptions;
    public List<LegoColor>? LegoColors { get; set; }
	public List<LegoMinifig>? LegoMinifigs { get; set; }
	private readonly LegoMinifigsRepository _repo;

    public RestService(LegoMinifigsRepository repo)
    {
        _client = new HttpClient();
		_repo = repo;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<List<LegoColor>> GetLegoColorsAsync()
		{
			LegoColors = new List<LegoColor>();

			//Uri uri = new Uri(string.Format("https://rebrickable.com/api/v3/lego/colors/?key=2d2ec2792fd89d2b412f795f5705ac5f", string.Empty));
			string url;

			url = "https://rebrickable.com/api/v3/lego/colors/?key=2d2ec2792fd89d2b412f795f5705ac5f&page_size=5000";

			try
			{
				HttpResponseMessage response = await _client.GetAsync(url);
				if (response.IsSuccessStatusCode)
				{
					string content = await response.Content.ReadAsStringAsync();
					var result = JsonSerializer.Deserialize<LegoColorResponse>(content, _serializerOptions);
					if (result != null)
					{
						LegoColors.AddRange(result.Results);

						url = result.Next != null
							? result.Next + "&key=2d2ec2792fd89d2b412f795f5705ac5f"
							: null;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(@"\tERROR {0}", ex.Message);
			}

			return LegoColors;
		}

	public async Task<List<LegoMinifig>> GetLegoMinifigsAsync()
		{
			var allMinifigs = new List<LegoMinifig>();

			// 👇 get latest modified from DB
			var lastSync = await _repo.GetLatestModifiedAsync();

			string url;

			url = "https://rebrickable.com/api/v3/lego/minifigs/?key=2d2ec2792fd89d2b412f795f5705ac5f&page_size=5000";
			

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