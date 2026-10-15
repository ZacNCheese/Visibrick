using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Visibrick.Data;
using Visibrick.Pages;
using Visibrick.Services.Auth;
using Visibrick.Services.Collection;
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
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "visibrick.db");
		Debug.WriteLine(dbPath);

		// 🟢 REGISTER SERVICES
		// Start the SQLite engine (from SQLitePCLRaw.bundle_e_sqlite3) before opening the database
		SQLitePCL.Batteries_V2.Init();
		builder.Services.AddSingleton(new AppDatabase(dbPath));
		builder.Services.AddSingleton<CatalogRepository>();
		builder.Services.AddSingleton<SupabaseCatalogClient>();
		builder.Services.AddSingleton<CatalogSyncService>();
		builder.Services.AddSingleton<SyncOrchestrator>();

		// Accounts (Supabase Auth)
		builder.Services.AddSingleton<SupabaseAuthClient>();
		builder.Services.AddSingleton<AuthService>();
		builder.Services.AddSingleton<ProfileService>();

		// The signed-in user's collection
		builder.Services.AddSingleton<CollectionRepository>();
		builder.Services.AddSingleton<UserDataClient>();
		builder.Services.AddSingleton<CollectionService>();

		// Tab pages live for the whole session; category pages are created each time you open one
		builder.Services.AddSingleton<CollectionPage>();
		builder.Services.AddSingleton<CatalogPage>();
		builder.Services.AddTransient<CatalogItemsPage>();
		builder.Services.AddTransient<SignInPage>();
		builder.Services.AddTransient<ProfilePage>();
		builder.Services.AddTransient<ItemDetailPage>();
		return builder.Build();
	}
}
