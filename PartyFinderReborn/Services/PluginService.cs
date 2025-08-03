using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Dalamud.Plugin;
using static ECommons.ImGuiMethods.ImGuiEx;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for collecting and managing installed plugin data
/// </summary>
public class PluginService : IDisposable
{
    public PluginService()
    {
    }

    /// <summary>
    /// Gets a list of all installed plugins
    /// </summary>
    /// <returns>An enumerable collection of exposed plugin information</returns>
    public IEnumerable<IExposedPlugin> GetInstalled()
    {
        try
        {
            return Svc.PluginInterface.InstalledPlugins;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting installed plugins: {ex.Message}");
            return Enumerable.Empty<IExposedPlugin>();
        }
    }

    /// <summary>
    /// Converts an exposed plugin to required plugin info format
    /// </summary>
    /// <param name="plugin">The exposed plugin to convert</param>
    /// <returns>RequiredPluginInfo with InternalName, VanityName, and MinVersion set</returns>
    public RequiredPluginInfo ToRequiredInfo(IExposedPlugin plugin)
    {
        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        // Using constructor: RequiredPluginInfo(string internalName, string vanityName)
        // MinVersion is null as specified in the requirements
        return new RequiredPluginInfo(plugin.InternalName, plugin.Name);
    }

    public void Dispose()
    {
    }
}
