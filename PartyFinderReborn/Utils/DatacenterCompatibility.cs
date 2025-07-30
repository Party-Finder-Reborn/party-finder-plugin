using PartyFinderReborn.Models;
using PartyFinderReborn.Services;

namespace PartyFinderReborn.Utils;

/// <summary>
/// Utility class for checking datacenter compatibility between local player and party listings
/// </summary>
public static class DatacenterCompatibility
{
    /// <summary>
    /// Checks if the local player's datacenter is compatible with a party listing's creator datacenter.
    /// Returns true if both players are on the same datacenter, allowing them to party together.
    /// </summary>
    /// <param name="worldService">World service instance to get local player datacenter</param>
    /// <param name="partyListing">Party listing containing creator datacenter information</param>
    /// <returns>True if datacenters are compatible (same), false otherwise</returns>
    public static bool IsDatacenterCompatible(WorldService worldService, PartyListing partyListing)
    {
        if (worldService == null || partyListing == null)
        {
            return false;
        }

        // Get local player's current datacenter
        var localPlayerDatacenter = worldService.GetCurrentPlayerCurrentDataCenter();
        
        // Get the party listing's datacenter
        var creatorDatacenter = partyListing.Datacenter;

        // Both must be non-null/non-empty to be compatible
        if (string.IsNullOrEmpty(localPlayerDatacenter) || string.IsNullOrEmpty(creatorDatacenter))
        {
            return false;
        }

        // Simple case-insensitive string comparison
        return localPlayerDatacenter.Equals(creatorDatacenter, System.StringComparison.OrdinalIgnoreCase);
    }
}
