using Newtonsoft.Json;

namespace PartyFinderReborn.Models;

/// <summary>
/// Response model for the duty points API endpoint
/// </summary>
public class DutyPointsResponse
{
    [JsonProperty("points")]
    public List<ProgPointStatus> Points { get; set; } = new();
}
