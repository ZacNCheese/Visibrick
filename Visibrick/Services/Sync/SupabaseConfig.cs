namespace Visibrick.Services.Sync;

// Where the app downloads the catalog from: the Supabase project that Visibrick.Sync imports into.
//
// The anon/publishable key is designed to be public and ship inside apps. Row Level Security on the
// catalog tables (see Visibrick.Sync/Schema.cs) limits it to reading the catalog. Never put the
// database password or the "service_role"/secret key here.
public static class SupabaseConfig
{
    public const string Url = "https://akzjgaqiummkclplizga.supabase.co";

    // Supabase dashboard → Project Settings → API Keys → the "anon public" key (or the "Publishable key")
    public const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImFrempnYXFpdW1ta2NscGxpemdhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTIwMTYwMDksImV4cCI6MjEwNzU5MjAwOX0._WFed4nFy_iqAvG_oU9bFh8NDvvd6AZoGM2Fdf45pGE";
}
