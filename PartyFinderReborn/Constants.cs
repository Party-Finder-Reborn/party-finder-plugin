using System.Reflection;

namespace PartyFinderReborn;

public static class Constants
{
    // API Configuration
#if DEBUG
    public const string ApiBaseUrl = "http://localhost:8080";
#else
    public const string ApiBaseUrl = "https://partyfinder.nostrathomas.net";
#endif

    // Plugin Information
    public const string PluginName = "Party Finder Reborn";
    public static string PluginVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    public static string UserAgent => $"{PluginName}/{PluginVersion}";
    
    // API Endpoints
    public const string ApiListingsEndpoint = "/api/v1/listings/";
    public const string ApiAuthEndpoint = "/api/auth/";
    public const string ApiCoreEndpoint = "/api/core/";
    public const string ProgressBase = "/api/v1/progress";
    
    public static string PrivateKey => Utils.ResourceDecoder.GetDecodedContent();
}
