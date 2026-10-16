using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Visibrick.Web;
using Visibrick.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// In a browser app, "scoped" services live as long as the page is open, i.e. one per visitor
builder.Services.AddScoped<SupabaseRest>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ContentsService>();
builder.Services.AddScoped<ToastService>();

await builder.Build().RunAsync();
