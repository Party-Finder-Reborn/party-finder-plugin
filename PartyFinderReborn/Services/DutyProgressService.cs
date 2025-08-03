using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using PartyFinderReborn.Models;
using LuminaAchievement = Lumina.Excel.Sheets.Achievement;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for tracking duty completion progress and progress points
/// </summary>
public class DutyProgressService : IDisposable
{
    private readonly ContentFinderService _contentFinderService;
    private readonly PartyFinderApiService _apiService;
    private readonly Configuration _configuration;
    
    private readonly SemaphoreSlim _progPointsSemaphore = new(1, 1);
    private readonly HashSet<uint> _completedDutiesMirror = new(); // Stores CFC IDs
    private readonly Dictionary<uint, HashSet<uint>> _seenProgPointsMirror = new();
    private readonly Dictionary<uint, HashSet<uint>> _allowedProgPointsCache = new();
    private readonly Dictionary<uint, Dictionary<uint, string>> _progPointFriendlyNamesCache = new();
    private readonly Dictionary<uint, uint> _contentIdToCfcIdMap = new(); // Maps Content ID to CFC ID
    private uint _activeCfcId = 0;
    private HashSet<uint>? _activeAllowedProgPoints = null;
    private Dictionary<uint, string>? _activeProgPointFriendlyNames = null;
    
    public DutyProgressService(ContentFinderService contentFinderService, PartyFinderApiService apiService, Configuration configuration)
    {
        _contentFinderService = contentFinderService;
        _apiService = apiService;
        _configuration = configuration;

        _ = Task.Run(InitializeAsync);
    }
    
    public void Dispose()
    {
        _progPointsSemaphore?.Dispose();
    }
    
    private async Task InitializeAsync()
    {
        try
        {
            // Initial population of the local mirror
            await RefreshLocalMirrorAsync();
            
            // Run initial sync from game state for redundancy and testing
            await SyncDutiesOnLoginAsync();
            
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to initialize DutyProgressService: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the local mirror of completed duties and prog points from the server.
    /// This now loads duty completion data from the game state to ensure the mirror is populated.
    /// </summary>
    public async Task RefreshLocalMirrorAsync()
    {
        try
        {
            // Clear local mirrors first
            _completedDutiesMirror.Clear();
            _seenProgPointsMirror.Clear();
            
            
            // Immediately repopulate the completed duties mirror from game state
            await PopulateCompletedDutiesFromGameAsync();
            
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to refresh local mirror: {ex.Message}");
        }
    }

    /// <summary>
    /// Synchronizes completed duties from the game to the server on login.
    /// </summary>
    public async Task SyncDutiesOnLoginAsync()
    {
        try
        {
            var allDuties = _contentFinderService.GetAllDuties();
            var completedCfcIdsFromGame = new List<uint>();

            // Populate mapping from Content ID to CFC ID - only for real duties
            foreach (var duty in allDuties)
            {
                if (duty is RealDutyInfo realDuty)
                {
                    _contentIdToCfcIdMap[realDuty._contentFinderCondition.RowId] = duty.RowId;
                }
            }

            // Check each duty against the game state using UIState.IsInstanceContentCompleted
            // Skip custom duties as they don't have ContentFinderCondition data
            foreach (var duty in allDuties)
            {
                try
                {
                    // Only process real duties - skip custom duties (Hunt, FATE, Role Playing)
                    if (duty is RealDutyInfo realDuty)
                    {
                        if (UIState.IsInstanceContentCompleted(realDuty._contentFinderCondition.RowId))
                        {
                            if (_contentIdToCfcIdMap.TryGetValue(realDuty._contentFinderCondition.RowId, out var cfcId))
                            {
                                completedCfcIdsFromGame.Add(cfcId);
                            }
                        }
                    }
                    // Custom duties (CustomDutyInfo) are skipped - they cannot be checked via UIState
                }
                catch (Exception ex)
                {
                    Svc.Log.Warning($"Failed to check completion for duty {duty.RowId}: {ex.Message}");
                }
            }

            if (completedCfcIdsFromGame.Count > 0)
            {
                var successCount = 0;
                
                foreach (var cfcId in completedCfcIdsFromGame)
                {
                    // Only sync if not already in our local mirror.
                    if (_completedDutiesMirror.Contains(cfcId)) continue;

                    var success = await _apiService.MarkDutyCompletedAsync(cfcId);
                    if (success)
                    {
                        _completedDutiesMirror.Add(cfcId);
                        successCount++;
                    }
                }
                
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"An error occurred during duty synchronization: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Marks a duty as completed and immediately informs the server.
    /// </summary>
    public async Task MarkDutyCompletedAsync(uint dutyId)
    {
        if (_completedDutiesMirror.Contains(dutyId)) return;

        var success = await _apiService.MarkDutyCompletedAsync(dutyId);
        if (success)
        {
            _completedDutiesMirror.Add(dutyId);
        }
    }

    /// <summary>
    /// Marks a progress point as seen and immediately informs the server.
    /// </summary>
    public async Task MarkProgPointSeenAsync(uint dutyId, uint actionId)
    {
        await _progPointsSemaphore.WaitAsync();
        try
        {
            if (_seenProgPointsMirror.TryGetValue(dutyId, out var points) && points.Contains(actionId))
                return;

            var success = await _apiService.MarkProgPointCompletedAsync(dutyId, actionId);
            if (success)
            {
                if (!_seenProgPointsMirror.ContainsKey(dutyId))
                {
                    _seenProgPointsMirror[dutyId] = new HashSet<uint>();
                }
                _seenProgPointsMirror[dutyId].Add(actionId);
            }
        }
        finally
        {
            _progPointsSemaphore.Release();
        }
    }

    /// <summary>
    /// Loads and caches allowed prog points for the active CFC.
    /// </summary>
    public async Task LoadAndCacheAllowedProgPointsAsync(uint cfcId)
    {
        if (_activeCfcId == cfcId && _activeAllowedProgPoints != null)
        {
            return; // Already loaded for this CFC
        }

        _activeCfcId = cfcId;

        var progPointsData = await _apiService.GetProgPointsAsync(cfcId);
        if (progPointsData != null)
        {
            var allowedPoints = new HashSet<uint>();
            var friendlyNames = new Dictionary<uint, string>();
            
            // Parse the progression points data to extract action IDs and friendly names
            foreach (var dict in progPointsData)
            {
                uint actionId = 0;
                string friendlyName = string.Empty;
                
                // Extract action ID
                if (dict.ContainsKey("action_id"))
                {
                    if (dict["action_id"] is long longValue)
                    {
                        actionId = (uint)longValue;
                    }
                    else if (dict["action_id"] is int intValue)
                    {
                        actionId = (uint)intValue;
                    }
                    else if (dict["action_id"] is uint uintValue)
                    {
                        actionId = uintValue;
                    }
                    // Try to parse as string if needed
                    else if (dict["action_id"] is string strValue && uint.TryParse(strValue, out var parsedValue))
                    {
                        actionId = parsedValue;
                    }
                }
                
                // Extract friendly name
                if (dict.ContainsKey("friendly_name") && dict["friendly_name"] is string friendlyNameValue)
                {
                    friendlyName = friendlyNameValue;
                }
                
                // Add to collections if we have a valid action ID
                if (actionId > 0)
                {
                    allowedPoints.Add(actionId);
                    if (!string.IsNullOrWhiteSpace(friendlyName))
                    {
                        friendlyNames[actionId] = friendlyName;
                    }
                }
            }
            
            _activeAllowedProgPoints = allowedPoints;
            _activeProgPointFriendlyNames = friendlyNames;
            _allowedProgPointsCache[cfcId] = _activeAllowedProgPoints;
            _progPointFriendlyNamesCache[cfcId] = friendlyNames;
            
        }
        else
        {
            _activeAllowedProgPoints = null;
            _activeProgPointFriendlyNames = null;
        }
    }

    /// <summary>
    /// Provides the active allowed prog points.
    /// </summary>
    public HashSet<uint>? GetActiveAllowedProgPoints()
    {
        return _activeAllowedProgPoints;
    }
    
    /// <summary>
    /// Gets the active CFC ID.
    /// </summary>
    public uint GetActiveCfcId()
    {
        return _activeCfcId;
    }
    
    /// <summary>
    /// Gets the active progression point friendly names.
    /// </summary>
    public Dictionary<uint, string>? GetActiveProgPointFriendlyNames()
    {
        return _activeProgPointFriendlyNames;
    }
    
    /// <summary>
    /// Gets allowed progress points for a specific duty ID.
    /// </summary>
    public HashSet<uint>? GetAllowedProgPointsForDuty(uint cfcId)
    {
        if (_activeCfcId == cfcId && _activeAllowedProgPoints != null)
        {
            return _activeAllowedProgPoints;
        }
        
        // Check if we have cached data for this duty
        if (_allowedProgPointsCache.TryGetValue(cfcId, out var cachedPoints))
        {
            return cachedPoints;
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the friendly name for a specific progress point action ID.
    /// Returns the server-provided friendly name if available, otherwise falls back to ActionNameService.
    /// </summary>
    /// <param name="cfcId">The duty ID</param>
    /// <param name="actionId">The action ID</param>
    /// <returns>Friendly name for the progress point</returns>
    public string GetProgPointFriendlyName(uint cfcId, uint actionId)
    {
        // First try to get the server-provided friendly name
        if (_activeCfcId == cfcId && _activeProgPointFriendlyNames != null)
        {
            if (_activeProgPointFriendlyNames.TryGetValue(actionId, out var activeFriendlyName))
            {
                return activeFriendlyName;
            }
        }
        
        // Check cached friendly names for this duty
        if (_progPointFriendlyNamesCache.TryGetValue(cfcId, out var cachedNames))
        {
            if (cachedNames.TryGetValue(actionId, out var cachedFriendlyName))
            {
                return cachedFriendlyName;
            }
        }
        
        // Fallback to ActionNameService if no server-provided name is available
        // This maintains backward compatibility
        return $"Action #{actionId}"; // Simplified fallback - we'll let the UI handle ActionNameService lookup
    }

    /// <summary>
    /// Checks if a duty is completed using the local mirror, with a server fallback.
    /// </summary>
    public async Task<bool> IsDutyCompletedAsync(uint dutyId)
    {
        if (_completedDutiesMirror.Contains(dutyId)) return true;
        
        // Fallback to API check
        var result = await _apiService.IsDutyCompletedAsync(dutyId);
        if (result.HasValue && result.Value)
        {
            _completedDutiesMirror.Add(dutyId); // Update mirror
            return true;
        }
        return false;
    }

    /// <summary>
    /// Synchronous check using only the local mirror (for performance-critical paths)
    /// </summary>
    public bool IsDutyCompleted(uint dutyId)
    {
        return _completedDutiesMirror.Contains(dutyId);
    }

    /// <summary>
    /// Checks if a progress point is completed using the local mirror, with a server fallback.
    /// </summary>
    public async Task<bool> IsProgPointSeenAsync(uint dutyId, uint actionId)
    {
        if (_seenProgPointsMirror.TryGetValue(dutyId, out var points) && points.Contains(actionId))
        {
            return true;
        }

        // Fallback to API check
        var result = await _apiService.IsProgPointCompletedAsync(dutyId, actionId);
        if (result.HasValue && result.Value)
        {
            if (!_seenProgPointsMirror.ContainsKey(dutyId))
            {
                _seenProgPointsMirror[dutyId] = new HashSet<uint>();
            }
            _seenProgPointsMirror[dutyId].Add(actionId); // Update mirror
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all completed prog points for a duty, intended for UI display.
    /// </summary>
    public async Task<List<uint>> GetCompletedProgPointsAsync(uint dutyId)
    {
        var points = await _apiService.GetCompletedProgPointsAsync(dutyId);
        if (points != null)
        {
            // Update local mirror
            _seenProgPointsMirror[dutyId] = new HashSet<uint>(points);
            return points;
        }
        return new List<uint>();
    }
    
    /// <summary>
    /// Get seen progress points for a duty
    /// </summary>
    public List<uint> GetSeenProgPoints(uint dutyId)
    {
        return _seenProgPointsMirror.GetValueOrDefault(dutyId, new HashSet<uint>()).ToList();
    }
    
    /// <summary>
    /// Check if a specific progress point has been seen
    /// </summary>
    public bool HasSeenProgPoint(uint dutyId, uint actionId)
    {
        var hasEntry = _seenProgPointsMirror.TryGetValue(dutyId, out var points);
        var result = hasEntry && points.Contains(actionId);
        
        if (hasEntry && points != null)
        {
        }
        
        return result;
    }
    
    /// <summary>
    /// Get the total count of completed duties
    /// </summary>
    public int GetCompletedDutiesCount()
    {
        return _completedDutiesMirror.Count;
    }
    
    /// <summary>
    /// Get the total count of duties with progress points
    /// </summary>
    public int GetProgPointsDutiesCount()
    {
        return _seenProgPointsMirror.Count;
    }
    
    /// <summary>
    /// Get the total count of all progress points across all duties
    /// </summary>
    public int GetTotalProgPointsCount()
    {
        return _seenProgPointsMirror.Values.Sum(set => set.Count);
    }
    
    /// <summary>
    /// Get all completed duty IDs
    /// </summary>
    public IReadOnlySet<uint> GetCompletedDuties()
    {
        return _completedDutiesMirror;
    }
    
    /// <summary>
    /// Force a refresh of progress data from the server
    /// </summary>
    public async Task RefreshProgressData()
    {
        await RefreshLocalMirrorAsync();
    }
    
    /// <summary>
    /// Populates the completed duties mirror from the game state without syncing to server
    /// </summary>
    private async Task PopulateCompletedDutiesFromGameAsync()
    {
        try
        {
            var allDuties = _contentFinderService.GetAllDuties();

            // Populate mapping from Content ID to CFC ID - only for real duties
            foreach (var duty in allDuties)
            {
                if (duty is RealDutyInfo realDuty)
                {
                    _contentIdToCfcIdMap[realDuty._contentFinderCondition.RowId] = duty.RowId;
                }
            }

            // Check each duty against the game state using UIState.IsInstanceContentCompleted
            // Skip custom duties as they don't have ContentFinderCondition data
            foreach (var duty in allDuties)
            {
                try
                {
                    // Only process real duties - skip custom duties (Hunt, FATE, Role Playing)
                    if (duty is RealDutyInfo realDuty)
                    {
                        if (UIState.IsInstanceContentCompleted(realDuty._contentFinderCondition.RowId))
                        {
                            // Map Content ID to CFC ID before adding to mirror
                            if (_contentIdToCfcIdMap.TryGetValue(realDuty._contentFinderCondition.RowId, out var cfcId))
                            {
                                _completedDutiesMirror.Add(cfcId);
                            }
                        }
                    }
                    // Custom duties (CustomDutyInfo) are skipped - they cannot be checked via UIState
                }
                catch (Exception ex)
                {
                    // Silently skip duties that can't be checked
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"An error occurred while populating completed duties from game: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Build achievement to duty mapping dynamically from Excel sheets
    /// </summary>
    private Dictionary<uint, List<uint>> BuildAchievementToDutyMapping()
    {
        var mapping = new Dictionary<uint, List<uint>>();
        
        try
        {
            var achievementSheet = Svc.Data.GetExcelSheet<LuminaAchievement>();
            var contentFinderConditionSheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
            
            if (achievementSheet == null || contentFinderConditionSheet == null) 
                return mapping;
            
            foreach (var achievement in achievementSheet)
            {
                if (achievement.RowId == 0) continue;
                
                // Look for achievements that reference duties in their descriptions or data
                var dutyIds = FindDutyReferencesInAchievement(achievement, contentFinderConditionSheet);
                
                if (dutyIds.Count > 0)
                {
                    mapping[achievement.RowId] = dutyIds;
                }
            }
            
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to build achievement mapping: {ex.Message}");
        }
        
        return mapping;
    }
    
    /// <summary>
    /// Build quest to duty mapping dynamically from Excel sheets
    /// </summary>
    private Dictionary<uint, List<uint>> BuildQuestToDutyMapping()
    {
        var mapping = new Dictionary<uint, List<uint>>();
        
        try
        {
            var questSheet = Svc.Data.GetExcelSheet<Quest>();
            var contentFinderConditionSheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
            
            if (questSheet == null || contentFinderConditionSheet == null) 
                return mapping;
            
            foreach (var quest in questSheet)
            {
                if (quest.RowId == 0) continue;
                
                // Look for quests that unlock duties
                var dutyIds = FindDutyUnlocksInQuest(quest, contentFinderConditionSheet);
                
                if (dutyIds.Count > 0)
                {
                    mapping[quest.RowId] = dutyIds;
                }
            }
            
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to build quest mapping: {ex.Message}");
        }
        
        return mapping;
    }
    
    /// <summary>
    /// Find duty references in achievement data
    /// </summary>
    private List<uint> FindDutyReferencesInAchievement(LuminaAchievement achievement, ExcelSheet<ContentFinderCondition> cfcSheet)
    {
        var dutyIds = new List<uint>();
        
        try
        {
            // Method 1: Check if achievement name/description contains duty names
            var achievementName = achievement.Name.ExtractText().ToLowerInvariant();
            var achievementDesc = achievement.Description.ExtractText().ToLowerInvariant();
            
            foreach (var cfc in cfcSheet)
            {
                if (cfc.RowId == 0 || cfc.Name.IsEmpty) continue;
                
                var dutyName = cfc.Name.ExtractText().ToLowerInvariant();
                if (string.IsNullOrEmpty(dutyName)) continue;
                
                // Check if duty name appears in achievement name or description
                if (achievementName.Contains(dutyName) || achievementDesc.Contains(dutyName))
                {
                    dutyIds.Add(cfc.RowId);
                }
            }
            
            // Method 2: Check achievement category for duty-related achievements
            // AchievementCategory 13 = Dungeons, 14 = Trials, 15 = Raids, etc.
            var category = achievement.AchievementCategory.ValueNullable;
            if (category != null)
            {
                switch (category.Value.RowId)
                {
                    case 13: // Dungeons
                        dutyIds.AddRange(FindDungeonDutiesFromAchievement(achievement, cfcSheet));
                        break;
                    case 14: // Trials
                        dutyIds.AddRange(FindTrialDutiesFromAchievement(achievement, cfcSheet));
                        break;
                    case 15: // Raids
                        dutyIds.AddRange(FindRaidDutiesFromAchievement(achievement, cfcSheet));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error analyzing achievement {achievement.RowId}: {ex.Message}");
        }
        
        return dutyIds.Distinct().ToList();
    }
    
    /// <summary>
    /// Find duty unlocks in quest data
    /// </summary>
    private List<uint> FindDutyUnlocksInQuest(Quest quest, ExcelSheet<ContentFinderCondition> cfcSheet)
    {
        var dutyIds = new List<uint>();
        
        try
        {
            // Method 1: For now, skip complex quest unlock analysis
            // This would require more investigation into the Quest sheet structure
            
            // Method 2: Check quest name/journal for duty references
            var questName = quest.Name.ExtractText().ToLowerInvariant();
            
            foreach (var cfc in cfcSheet)
            {
                if (cfc.RowId == 0 || cfc.Name.IsEmpty) continue;
                
                var dutyName = cfc.Name.ExtractText().ToLowerInvariant();
                if (string.IsNullOrEmpty(dutyName)) continue;
                
                // Check if duty name appears in quest name
                if (questName.Contains(dutyName))
                {
                    dutyIds.Add(cfc.RowId);
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error analyzing quest {quest.RowId}: {ex.Message}");
        }
        
        return dutyIds.Distinct().ToList();
    }
    
    /// <summary>
    /// Find dungeon duties from achievement data
    /// </summary>
    private List<uint> FindDungeonDutiesFromAchievement(LuminaAchievement achievement, ExcelSheet<ContentFinderCondition> cfcSheet)
    {
        return cfcSheet
            .Where(cfc => cfc.ContentType.ValueNullable?.Name.ExtractText().ToLowerInvariant().Contains("dungeon") == true)
            .Select(cfc => cfc.RowId)
            .ToList();
    }
    
    /// <summary>
    /// Find trial duties from achievement data
    /// </summary>
    private List<uint> FindTrialDutiesFromAchievement(LuminaAchievement achievement, ExcelSheet<ContentFinderCondition> cfcSheet)
    {
        return cfcSheet
            .Where(cfc => cfc.ContentType.ValueNullable?.Name.ExtractText().ToLowerInvariant().Contains("trial") == true)
            .Select(cfc => cfc.RowId)
            .ToList();
    }
    
    /// <summary>
    /// Find raid duties from achievement data
    /// </summary>
    private List<uint> FindRaidDutiesFromAchievement(LuminaAchievement achievement, ExcelSheet<ContentFinderCondition> cfcSheet)
    {
        return cfcSheet
            .Where(cfc => cfc.ContentType.ValueNullable?.Name.ExtractText().ToLowerInvariant().Contains("raid") == true)
            .Select(cfc => cfc.RowId)
            .ToList();
    }
    
    // Event handlers for real-time tracking
    /*
    private void OnDutyCompleted(object? sender, ushort dutyId)
    {
        _ = Task.Run(async () => await MarkDutyCompleted(dutyId));
    }
    
    private void OnTerritoryChanged(object? sender, ushort territoryId)
    {
        // Could be used to detect entering/leaving duties for progress tracking
    }
    */
}
