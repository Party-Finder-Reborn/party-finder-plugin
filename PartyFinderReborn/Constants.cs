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
    
    // ECDSA Signing Configuration
    // This method will have its string obfuscated by ConfuserEx
    public static string GetPrivateKey()
    {
#if RELEASE_BUILD
        return "INJECTED_PRIVATE_KEY";
#else
        return @"-----BEGIN PRIVATE KEY-----
MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgPLmCaPsaIe6DeKDM
+Wyt4Ja/jmj3Cy1EOqyZF+UyIBahRANCAATIGqD1KTbHLwiCjFjTrhhZpTtp29KH
0JjNLxA1HFpg1qp0+Wd2PzHHkohREq/jwr5XzArNTzrOfQZ21MXVX/Tf
-----END PRIVATE KEY-----";
#endif
    }
    
    // Cached property for backward compatibility
    public static string PrivateKey => GetPrivateKey();
}
