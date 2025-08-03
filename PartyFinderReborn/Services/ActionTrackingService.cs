using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using ECommons.Hooks;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.Hooks.ActionEffectTypes;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for tracking action effects and forwarding relevant progress points to DutyProgressService.
/// Now filters actions based on allowed progress points from DutyProgressService instead of boss/trash heuristics.
/// </summary>
public class ActionTrackingService : IDisposable
{
    private readonly DutyProgressService _dutyProgressService;
    private readonly ContentFinderService _contentFinderService;
    private readonly Configuration _configuration;
    private readonly HashSet<(uint cfc, uint action)> _seenProgPoints;
    private readonly Dictionary<(uint cfc, uint action), DateTime> _progPointTimestamps;
    
    private ushort _lastTerritoryType = 0;
    
    public ActionTrackingService(DutyProgressService dutyProgressService, ContentFinderService contentFinderService, Configuration configuration)
    {
        _dutyProgressService = dutyProgressService;
        _contentFinderService = contentFinderService;
        _configuration = configuration;
        _seenProgPoints = new HashSet<(uint cfc, uint action)>();
        _progPointTimestamps = new Dictionary<(uint cfc, uint action), DateTime>();
        
        // Hook into territory changes for instance leave detection
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        
        Enable();
        
    }
    
    public void Enable()
    {
        if (_configuration.EnableActionTracking)
        {
            ActionEffect.ActionEffectEvent += OnActionEffect;
        }
    }
    
    public void Disable()
    {
        ActionEffect.ActionEffectEvent -= OnActionEffect;
    }

    private void OnActionEffect(ActionEffectSet set)
    {
        try
        {
            if (!_configuration.EnableActionTracking)
                return;

            // Check if we have a valid action
            if (set.Action == null || !set.Action.HasValue)
                return;
                
            var sourceId = (uint)(set.Source?.GameObjectId ?? 0);
            if (sourceId == 0)
                return;

// Check if source is player/party and should be filtered
            if (ShouldFilterSource(sourceId))
                return;

            // Get current territory and map to CFC ID
            var territoryType = Svc.ClientState.TerritoryType;
            var cfcId = _contentFinderService.GetCfcIdByTerritory((ushort)territoryType);
            
            if (cfcId.HasValue)
            {
                // Get the proper action ID from the ActionEffectSet
                var actionId = set.Action.Value.RowId;
                
                // Apply allowed progress points filtering - this replaces the old boss/trash heuristics
                // Only track actions that are in the allowed set provided by DutyProgressService
                var allowedProgPoints = _dutyProgressService.GetActiveAllowedProgPoints();
                if (allowedProgPoints == null || !allowedProgPoints.Contains(actionId))
                {
                    return; // Silently skip non-allowed progression points
                }
                
                // Avoid duplicates within single session
                var key = (cfcId.Value, actionId);
                if (_seenProgPoints.Contains(key))
                {
                    return;
                }

                _seenProgPoints.Add(key);
                _progPointTimestamps[key] = DateTime.Now;
                
                // Add to DutyProgressService immediately
                _ = Task.Run(async () => await _dutyProgressService.MarkProgPointSeenAsync(cfcId.Value, actionId));
                
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error in ActionTrackingService.OnActionEffect: {ex.Message}");
        }
    }
    
    private bool ShouldFilterSource(uint sourceId)
    {
        try
        {
            var source = Svc.Objects.SearchById(sourceId);
            if (source == null)
                return false;
            
            // Filter player actions if enabled
            if (_configuration.FilterPlayerActions && source.ObjectKind == ObjectKind.Player)
            {
                // Check if it's the local player
                if (source.GameObjectId == Svc.ClientState.LocalPlayer?.GameObjectId)
                    return true;
            }
            
            // Filter party member actions if enabled
            if (_configuration.FilterPartyActions && source.ObjectKind == ObjectKind.Player)
            {
                // Check if the source is in the party
                var partyList = Svc.Party;
                if (partyList != null)
                {
                    foreach (var partyMember in partyList)
                    {
                        if (partyMember.GameObject?.GameObjectId == source.GameObjectId)
                            return true;
                    }
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"Error checking source filter for {sourceId}: {ex.Message}");
            return false;
        }
    }
    
    

    
    private void OnTerritoryChanged(ushort territoryType)
    {
        try
        {
            var prevCfcId = _contentFinderService.GetCfcIdByTerritory(_lastTerritoryType);
            var currentCfcId = _contentFinderService.GetCfcIdByTerritory(territoryType);
            
            // Load allowed progression points when entering a duty
            if (currentCfcId.HasValue)
            {
                _ = Task.Run(async () => await _dutyProgressService.LoadAndCacheAllowedProgPointsAsync(currentCfcId.Value));
            }
            
            // Check if we should reset the cache on instance leave
            if (_configuration.ResetOnInstanceLeave)
            {
                // If we had a CFC before but don't now, we likely left an instance
                if (prevCfcId.HasValue && !currentCfcId.HasValue)
                {
                    var clearedCount = _seenProgPoints.Count;
                    _seenProgPoints.Clear();
                    _progPointTimestamps.Clear();
                }
            }
            
            _lastTerritoryType = territoryType;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error in OnTerritoryChanged: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Manually clear the seen progress points (for debugging or configuration changes)
    /// </summary>
    public void ClearSeenProgPoints()
    {
        var clearedCount = _seenProgPoints.Count;
        _seenProgPoints.Clear();
        _progPointTimestamps.Clear();
    }
    
    /// <summary>
    /// Get the count of currently tracked progress points in this session
    /// </summary>
    public int GetSeenProgPointsCount()
    {
        return _seenProgPoints.Count;
    }
    
    /// <summary>
    /// Get all currently tracked progress points
    /// </summary>
    public IReadOnlySet<(uint cfc, uint action)> GetSeenProgPoints()
    {
        return _seenProgPoints.ToHashSet();
    }

    public void Dispose()
    {
        Disable();
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        _seenProgPoints.Clear();
        _progPointTimestamps.Clear();
        
    }
}
