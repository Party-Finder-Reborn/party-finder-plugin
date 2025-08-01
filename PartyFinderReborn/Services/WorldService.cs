using System;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for handling world and datacenter information using ECommons helpers
/// </summary>
public class WorldService : IDisposable
{
    public WorldService()
    {
        Svc.Log.Info("WorldService initialized using ECommons ExcelWorldHelper");
    }

    /// <summary>
    /// Get the current player's home world name
    /// </summary>
    public string? GetCurrentPlayerHomeWorld()
    {
        return Player.HomeWorld;
    }

    /// <summary>
    /// Get the current player's current world name (where they are now)
    /// </summary>
    public string? GetCurrentPlayerCurrentWorld()
    {
        return Player.CurrentWorld;
    }

    /// <summary>
    /// Get the current player's home datacenter name
    /// </summary>
    public string? GetCurrentPlayerHomeDataCenter()
    {
        return Player.HomeDataCenter;
    }

    /// <summary>
    /// Get the current player's current datacenter name
    /// </summary>
    public string? GetCurrentPlayerCurrentDataCenter()
    {
        return Player.CurrentDataCenter;
    }

    /// <summary>
    /// Get all public datacenters
    /// </summary>
    public WorldDCGroupType[] GetAllDatacenters()
    {
        return ExcelWorldHelper.GetDataCenters(checkForPublicWorlds: true);
    }

    /// <summary>
    /// Get all public worlds for a specific datacenter
    /// </summary>
    public World[] GetWorldsForDatacenter(uint datacenterId)
    {
        return ExcelWorldHelper.GetPublicWorlds(datacenterId);
    }

    /// <summary>
    /// Get world by name
    /// </summary>
    public World? GetWorldByName(string worldName)
    {
        return ExcelWorldHelper.Get(worldName, onlyPublic: true);
    }

    /// <summary>
    /// Get world by ID
    /// </summary>
    public World? GetWorldById(uint worldId)
    {
        return ExcelWorldHelper.Get(worldId, onlyPublic: true);
    }

    /// <summary>
    /// Get datacenter for a world
    /// </summary>
    public WorldDCGroupType? GetDatacenterForWorld(World world)
    {
        return Svc.Data.GetExcelSheet<WorldDCGroupType>()?.GetRow(world.DataCenter.RowId);
    }

    /// <summary>
    /// Get datacenter by name
    /// </summary>
    public WorldDCGroupType? GetDatacenterByName(string datacenterName)
    {
        var datacenters = GetAllDatacenters();
        foreach (var dc in datacenters)
        {
            if (dc.Name.ExtractText().Equals(datacenterName, StringComparison.OrdinalIgnoreCase))
            {
                return dc;
            }
        }
        return null;
    }

    /// <summary>
    /// Convert datacenter name to API format (lowercase)
    /// </summary>
    public string GetApiDatacenterName(string datacenterName)
    {
        return datacenterName.ToLowerInvariant();
    }

    /// <summary>
    /// Convert world name to API format
    /// </summary>
    public string GetApiWorldName(string worldName)
    {
        return worldName;
    }

    public void Dispose()
    {
        Svc.Log.Info("WorldService disposed");
    }
}
