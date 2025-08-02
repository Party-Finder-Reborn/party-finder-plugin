using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using PartyFinderReborn.Services;

namespace PartyFinderReborn.Windows;

/// <summary>
/// Wrapper for IExposedPlugin to make it compatible with GenericSelectorModal
/// </summary>
public class PluginSelectableItem : ISelectableItem<IExposedPlugin>
{
    public string DisplayText { get; }
    public string TooltipText { get; }
    public IExposedPlugin Item { get; }
    
    public PluginSelectableItem(IExposedPlugin plugin)
    {
        Item = plugin;
        DisplayText = plugin.Name; // Friendly name
        TooltipText = $"Internal Name: {plugin.InternalName}\nVersion: {plugin.Version}\nLoaded: {plugin.IsLoaded}";
    }
}

/// <summary>
/// A reusable modal window for selecting plugins
/// </summary>
public class PluginSelectorModal
{
    private readonly PluginService _pluginService;
    private readonly GenericSelectorModal<IExposedPlugin> _genericModal;
    
    public PluginSelectorModal(PluginService pluginService)
    {
        _pluginService = pluginService;
        
        var config = new GenericSelectorModal<IExposedPlugin>.Config
        {
            ModalTitle = "Select Plugin",
            SearchPlaceholder = "Type to filter plugins...",
            ModalSize = new Vector2(700, 500),
            MaxDisplayedItems = 150,
            FilterButtons = new List<GenericSelectorModal<IExposedPlugin>.FilterButton>
            {
                new("All Plugins", () => WrapPlugins(_pluginService.GetInstalled())),
                new("Loaded Only", () => WrapPlugins(_pluginService.GetInstalled().Where(p => p.IsLoaded))),
                new("Dev Plugins", () => WrapPlugins(_pluginService.GetInstalled().Where(p => p.IsDev))),
                new("Third Party", () => WrapPlugins(_pluginService.GetInstalled().Where(p => p.IsThirdParty)))
            },
            CustomSearchFunc = (searchText, allItems) => 
            {
                var lowerSearch = searchText.ToLowerInvariant();
                return allItems.Where(item => 
                    item.DisplayText.ToLowerInvariant().Contains(lowerSearch) ||
                    item.Item.InternalName.ToLowerInvariant().Contains(lowerSearch) ||
                    item.TooltipText.ToLowerInvariant().Contains(lowerSearch))
                .ToList();
            }
        };
        
        _genericModal = new GenericSelectorModal<IExposedPlugin>(config);
    }
    
    private List<ISelectableItem<IExposedPlugin>> WrapPlugins(IEnumerable<IExposedPlugin> plugins)
    {
        return plugins
            .Select(plugin => new PluginSelectableItem(plugin) as ISelectableItem<IExposedPlugin>)
            .OrderBy(item => item.DisplayText)
            .ToList();
    }
    
    /// <summary>
    /// Opens the plugin selector modal
    /// </summary>
    /// <param name="currentPlugin">The currently selected plugin (if any)</param>
    /// <param name="onPluginSelected">Callback when a plugin is selected or modal is closed</param>
    public void Open(IExposedPlugin? currentPlugin, Action<IExposedPlugin?> onPluginSelected)
    {
        var allPlugins = WrapPlugins(_pluginService.GetInstalled());
        var defaultPlugin = currentPlugin ?? allPlugins.FirstOrDefault()?.Item; // Use first plugin as default if null
        if (defaultPlugin != null)
        {
            _genericModal.Open(allPlugins, defaultPlugin, (selectedPlugin) => 
            {
                // Pass through as-is since IExposedPlugin is a reference type
                onPluginSelected?.Invoke(selectedPlugin);
            });
        }
    }
    
    /// <summary>
    /// Opens the plugin selector modal with string return (friendly name)
    /// </summary>
    /// <param name="currentPlugin">The currently selected plugin (if any)</param>
    /// <param name="onPluginNameSelected">Callback when a plugin is selected (returns friendly name) or modal is closed</param>
    public void OpenForName(IExposedPlugin? currentPlugin, Action<string?> onPluginNameSelected)
    {
        Open(currentPlugin, (selectedPlugin) =>
        {
            onPluginNameSelected?.Invoke(selectedPlugin?.Name);
        });
    }
    
    /// <summary>
    /// Draws the modal window. Call this from your main Draw() method.
    /// </summary>
    public void Draw()
    {
        _genericModal.Draw();
    }
    
    /// <summary>
    /// Check if the modal is currently open
    /// </summary>
    public bool IsOpen => _genericModal.IsOpen;
}
