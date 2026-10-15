using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Visibrick.Data;
using Visibrick.Services.API;
using Visibrick.Services.Sync;

namespace Visibrick;

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
		builder.Services.AddSingleton<LegoMinifigRepository>();
		builder.Services.AddSingleton<RestService>();
		builder.Services.AddSingleton<LegoColorSyncService>();
		builder.Services.AddSingleton<LegoMinifigSyncService>();
		builder.Services.AddSingleton<SyncOrchestrator>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<MinifigsPage>();
		return builder.Build();
	}
}
