using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for working with Action data from the game's Excel sheets
/// Provides cached lookup for Action ID → Name mappings
/// </summary>
public class ActionNameService : IDisposable
{
    private ConcurrentDictionary<uint, string>? _actionNameCache;
    private readonly object _initLock = new();
    private bool _isInitialized = false;
    
    public ActionNameService()
    {
        InitializeCache();
    }
    
    public void Dispose()
    {
        _actionNameCache?.Clear();
        _actionNameCache = null;
    }
    
    private void InitializeCache()
    {
        if (_isInitialized) return;
        
        lock (_initLock)
        {
            if (_isInitialized) return;
            
            try
            {
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                if (sheet == null)
                {
                    Svc.Log.Error("Failed to load Action sheet from Lumina");
                    _actionNameCache = new ConcurrentDictionary<uint, string>();
                    return;
                }

                _actionNameCache = new ConcurrentDictionary<uint, string>();
                
                foreach (var action in sheet)
                {
                    if (action.RowId == 0) continue;
                    
                    // Skip invalid or empty entries
                    if (action.Name.IsEmpty) continue;
                    
                    var actionName = action.Name.ExtractText();
                    if (string.IsNullOrEmpty(actionName)) continue;
                    
                    _actionNameCache[action.RowId] = actionName;
                }
                
                Svc.Log.Info($"Loaded {_actionNameCache.Count} Action names");
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to initialize ActionNameService: {ex.Message}");
                _actionNameCache = new ConcurrentDictionary<uint, string>();
                _isInitialized = true;
            }
        }
    }
    
    /// <summary>
    /// Get Action name by ID with fallback to "Action #id" format
    /// </summary>
    /// <param name="id">Action ID</param>
    /// <returns>Action name or fallback string</returns>
    public string Get(uint id)
    {
        if (_actionNameCache == null)
        {
            InitializeCache();
        }
        
        return _actionNameCache?.GetValueOrDefault(id, $"Action #{id}") ?? $"Action #{id}";
    }
    
    /// <summary>
    /// Get multiple Action names by IDs - convenience method
    /// </summary>
    /// <param name="ids">Collection of Action IDs</param>
    /// <returns>Enumerable of (id, name) tuples</returns>
    public IEnumerable<(uint id, string name)> GetMany(IEnumerable<uint> ids)
    {
        if (_actionNameCache == null)
        {
            InitializeCache();
        }
        
        return ids.Select(id => (id, Get(id)));
    }
    
    /// <summary>
    /// Placeholder method for future CFC → Action mapping if discovered
    /// Currently returns empty enumerable as mapping sheet is not yet known
    /// </summary>
    /// <param name="cfcId">Content Finder Condition ID</param>
    /// <returns>Actions associated with the CFC (currently empty)</returns>
    public IEnumerable<(uint id, string name)> GetActionsForCfc(uint cfcId)
    {
        // TODO: Implement when CFC → Action mapping sheet is discovered
        // This might involve looking at Instance Content sheets or other related data
        Svc.Log.Debug($"GetActionsForCfc({cfcId}) called - mapping not yet implemented");
        return Enumerable.Empty<(uint id, string name)>();
    }
    
    /// <summary>
    /// Check if an Action ID exists in the cache
    /// </summary>
    /// <param name="id">Action ID</param>
    /// <returns>True if the action exists, false otherwise</returns>
    public bool IsValidAction(uint id)
    {
        if (_actionNameCache == null)
        {
            InitializeCache();
        }
        
        return _actionNameCache?.ContainsKey(id) ?? false;
    }
    
    /// <summary>
    /// Get the total count of cached actions
    /// </summary>
    /// <returns>Number of actions in cache</returns>
    public int GetActionCount()
    {
        return _actionNameCache?.Count ?? 0;
    }
    
    /// <summary>
    /// Get all action IDs that are currently cached
    /// </summary>
    /// <returns>Collection of all cached action IDs</returns>
    public IEnumerable<uint> GetAllActionIds()
    {
        if (_actionNameCache == null)
        {
            InitializeCache();
        }
        
        return _actionNameCache?.Keys ?? Enumerable.Empty<uint>();
    }
    
    /// <summary>
    /// Search for actions by name (case-insensitive partial match)
    /// </summary>
    /// <param name="searchTerm">Search term to match against action names</param>
    /// <returns>Matching actions as (id, name) tuples</returns>
    public IEnumerable<(uint id, string name)> SearchByName(string searchTerm)
    {
        if (_actionNameCache == null)
        {
            InitializeCache();
        }
        
        if (string.IsNullOrWhiteSpace(searchTerm) || _actionNameCache == null)
            return Enumerable.Empty<(uint id, string name)>();
        
        var lowerSearch = searchTerm.ToLowerInvariant();
        return _actionNameCache
            .Where(kvp => kvp.Value.ToLowerInvariant().Contains(lowerSearch))
            .Select(kvp => (kvp.Key, kvp.Value))
            .OrderBy(pair => pair.Value);
    }
}
