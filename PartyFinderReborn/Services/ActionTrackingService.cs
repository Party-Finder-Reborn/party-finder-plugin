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
/// Service for tracking action effects and forwarding relevant progress points to DutyProgressService
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
        
        Svc.Log.Info("ActionTrackingService initialized");
    }
    
    public void Enable()
    {
        if (_configuration.EnableActionTracking)
        {
            ActionEffect.ActionEffectEvent += OnActionEffect;
            Svc.Log.Debug("ActionEffect hook enabled");
        }
    }
    
    public void Disable()
    {
        ActionEffect.ActionEffectEvent -= OnActionEffect;
        Svc.Log.Debug("ActionEffect hook disabled");
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

            // Apply boss/trash mob filtering
            if (!ShouldTrackSourceByType(sourceId))
                return;

            // Get current territory and map to CFC ID
            var territoryType = Svc.ClientState.TerritoryType;
            var cfcId = _contentFinderService.GetCfcIdByTerritory((ushort)territoryType);
            
            if (cfcId.HasValue)
            {
                // Get the proper action ID from the ActionEffectSet
                var actionId = set.Action.Value.RowId;
                
                // Avoid duplicates within single session
                var key = (cfcId.Value, actionId);
                if (_seenProgPoints.Contains(key))
                {
                    Svc.Log.Debug($"Already tracked action {actionId} for duty {cfcId.Value}, skipping");
                    return;
                }

                _seenProgPoints.Add(key);
                _progPointTimestamps[key] = DateTime.Now;
                
                // Add to DutyProgressService immediately
                _ = Task.Run(async () => await _dutyProgressService.MarkProgPointSeenAsync(cfcId.Value, actionId));
                
                Svc.Log.Info($"🎯 NEW PROGRESS POINT: Action {actionId} ({set.Action.Value.Name}) tracked for duty {cfcId.Value} in territory {territoryType}");
            }
            else
            {
                Svc.Log.Debug($"No CFC ID found for territory {territoryType}, ignoring action {set.Action.Value.RowId}");
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
    
    private bool ShouldTrackSourceByType(uint sourceId)
    {
        try
        {
            var source = Svc.Objects.SearchById(sourceId) as IBattleNpc;
            if (source == null)
            {
                // If both boss and trash tracking are disabled, don't track anything
                if (!_configuration.TrackBossActionsOnly && !_configuration.TrackTrashMobs)
                    return false;
                
                // If it's not a BattleNpc, assume it's acceptable if either option is enabled
                return _configuration.TrackBossActionsOnly || _configuration.TrackTrashMobs;
            }
            
            // Estimate if this is a boss or trash mob based on available properties
            var isBoss = IsBossEnemy(source);
            var isTrashMob = !isBoss; // Simplification: non-boss enemies are trash mobs
            
            // Apply configuration filters
            if (_configuration.TrackBossActionsOnly && !isBoss)
                return false;

            if (!_configuration.TrackBossActionsOnly && _configuration.TrackTrashMobs && !isTrashMob)
                return false;

            // If TrackBossActionsOnly is false and TrackTrashMobs is false, track nothing
            if (!_configuration.TrackBossActionsOnly && !_configuration.TrackTrashMobs)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"Error checking source type for {sourceId}: {ex.Message}");
            return true; // Default to tracking if we can't determine type
        }
    }
    
    private bool IsBossEnemy(IBattleNpc npc)
    {
        try
        {
            // Bosses typically have higher max HP
            if (npc.MaxHp > 1000000) // 1M+ HP is likely a boss
                return true;
                
            // Bosses often have special status effects or are marked as important
            if (npc.StatusFlags.HasFlag(StatusFlags.Hostile) && npc.MaxHp > 100000)
                return true;
                
            // Check if the name contains boss indicators (simplified heuristic)
            var name = npc.Name.TextValue?.ToLowerInvariant() ?? "";
            if (name.Contains("ultima") || name.Contains("primal") || name.Contains("savage") || 
                name.Contains("extreme") || name.Contains("unreal"))
                return true;
                
            return false;
        }
        catch
        {
            return false;
        }
    }
    

    
    private void OnTerritoryChanged(ushort territoryType)
    {
        try
        {
            // Check if we should reset the cache on instance leave
            if (_configuration.ResetOnInstanceLeave)
            {
                // Determine if we're leaving an instance
                var prevCfcId = _contentFinderService.GetCfcIdByTerritory(_lastTerritoryType);
                var currentCfcId = _contentFinderService.GetCfcIdByTerritory(territoryType);
                
                // If we had a CFC before but don't now, we likely left an instance
                if (prevCfcId.HasValue && !currentCfcId.HasValue)
                {
                    var clearedCount = _seenProgPoints.Count;
                    _seenProgPoints.Clear();
                    _progPointTimestamps.Clear();
                    Svc.Log.Debug($"Reset {clearedCount} tracked progress points on instance leave (territory {_lastTerritoryType} -> {territoryType})");
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
        Svc.Log.Info($"Manually cleared {clearedCount} tracked progress points");
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
        
        Svc.Log.Info("ActionTrackingService disposed");
    }
}
