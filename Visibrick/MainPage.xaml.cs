namespace VisiBrick;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Maui.Primitives;


public partial class MainPage : ContentPage
{
	private HttpClient _client = new HttpClient();

	private readonly SyncOrchestrator _syncOrchestrator;

	private JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public MainPage(SyncOrchestrator syncOrchestrator)
	{
		InitializeComponent();
    	_syncOrchestrator = syncOrchestrator;
	}

	public async void OnCounterClicked(object? sender, EventArgs e)
	{
		await _syncOrchestrator.FullSyncAsync();
	}

}
