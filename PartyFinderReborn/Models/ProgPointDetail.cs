using Newtonsoft.Json;

namespace PartyFinderReborn.Models;

/// <summary>
/// Represents detailed progression points with friendly names
/// </summary>
public class ProgPointDetail
{
    [JsonProperty("action_id")]
    public uint ActionId { get; set; }

    [JsonProperty("friendly_name")]
    public string FriendlyName { get; set; } = string.Empty;
}

/// <summary>
/// Represents progression point status with completion information
/// </summary>
public class ProgPointStatus
{
    [JsonProperty("action_id")]
    public uint ActionId { get; set; }

    [JsonProperty("friendly_name")]
    public string FriendlyName { get; set; } = string.Empty;

    [JsonProperty("completed")]
    public bool Completed { get; set; }
}

