namespace Visibrick.Web.Services;

// The Supabase project the website talks to (the same one Visibrick.Sync imports the catalog into).
//
// The anon key is designed to be public and ship inside apps and websites. Row Level Security on every
// table (see supabase/migrations) limits what it can do. Never put the database password or the
// "service_role"/secret key here: everything in this project is downloaded to visitors' browsers.
public static class SupabaseConfig
{
    public const string Url = "https://akzjgaqiummkclplizga.supabase.co";

    public const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImFrempnYXFpdW1ta2NscGxpemdhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTIwMTYwMDksImV4cCI6MjEwNzU5MjAwOX0._WFed4nFy_iqAvG_oU9bFh8NDvvd6AZoGM2Fdf45pGE";
}
