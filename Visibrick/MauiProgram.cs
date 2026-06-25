using Microsoft.Extensions.Logging;
using SQLite;
using System.IO;
using System.Diagnostics;
using Microsoft.Maui.Storage;

namespace VisiBrick;

public static class MauiProgram
{

	
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif
		// 🟢 DATABASE PATH
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "lego.db");
		Debug.WriteLine(dbPath);
		

        // 🟢 REGISTER SERVICES
        builder.Services.AddSingleton(new AppDatabase(dbPath));
        builder.Services.AddSingleton<LegoColorRepository>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<RestService>();
		builder.Services.AddSingleton<LegoColorSyncService>();
		builder.Services.AddSingleton<LegoMinifigSyncService>();
		builder.Services.AddSingleton<LegoMinifigsRepository>();
		builder.Services.AddSingleton<SyncOrchestrator>();
		return builder.Build();
	}
}
