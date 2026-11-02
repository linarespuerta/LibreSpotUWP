// Minimal stand-ins for app types that are not part of what is being type-checked.
namespace LibreSpotUWP
{
    internal static class App { internal static LibreSpotUWP.Services.HomeAssistantLinkService HomeAssistantLink { get; set; } }
}
namespace LibreSpotUWP.Services
{
    internal static class LogService
    {
        public static void Info(string message) { }
        public static void Warn(string message) { }
        public static void Telemetry(string key, string message, bool warning = false) { }
    }
}
