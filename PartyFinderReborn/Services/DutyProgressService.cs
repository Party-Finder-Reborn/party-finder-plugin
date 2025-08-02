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
    private readonly HashSet<uint> _completedDutiesMirror = new();
    private readonly Dictionary<uint, HashSet<uint>> _seenProgPointsMirror = new();
    
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
            Svc.Log.Info("Running initial duty sync from game state...");
            await SyncDutiesOnLoginAsync();
            
            Svc.Log.Info("DutyProgressService initialized and initial sync completed.");
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
            
            Svc.Log.Info("Cleared local mirrors. Repopulating from game state...");
            
            // Immediately repopulate the completed duties mirror from game state
            await PopulateCompletedDutiesFromGameAsync();
            
            Svc.Log.Info($"Local mirror refresh completed. {_completedDutiesMirror.Count} completed duties loaded.");
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
            var completedDutiesFromGame = new List<uint>();

            // Check each duty against the game state using UIState.IsInstanceContentCompleted
            foreach (var duty in allDuties)
            {
                try
                {
                    if (UIState.IsInstanceContentCompleted(duty.Content.RowId))
                    {
                        completedDutiesFromGame.Add(duty.Content.RowId);
                    }
                }
                catch (Exception ex)
                {
                    Svc.Log.Warning($"Failed to check completion for duty {duty.Content.RowId}: {ex.Message}");
                }
            }

            if (completedDutiesFromGame.Count > 0)
            {
                Svc.Log.Info($"Found {completedDutiesFromGame.Count} completed duties in game state. Syncing them individually.");
                var successCount = 0;
                
                foreach (var dutyId in completedDutiesFromGame)
                {
                    // Only sync if not already in our local mirror.
                    if (_completedDutiesMirror.Contains(dutyId)) continue;

                    var success = await _apiService.MarkDutyCompletedAsync(dutyId);
                    if (success)
                    {
                        _completedDutiesMirror.Add(dutyId);
                        successCount++;
                    }
                }
                
                Svc.Log.Info($"Successfully synced {successCount} new completed duties to the server.");
            }
            else
            {
                Svc.Log.Info("No completed duties found in game state to sync.");
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
            Svc.Log.Info($"Marked duty {dutyId} as completed on server.");
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
                Svc.Log.Info($"Marked prog point {actionId} for duty {dutyId} as seen on server.");
            }
        }
        finally
        {
            _progPointsSemaphore.Release();
        }
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
        return _seenProgPointsMirror.TryGetValue(dutyId, out var points) && points.Contains(actionId);
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
            var completedDutiesFromGame = new List<uint>();

            // Check each duty against the game state using UIState.IsInstanceContentCompleted
            foreach (var duty in allDuties)
            {
                try
                {
                    if (UIState.IsInstanceContentCompleted(duty.Content.RowId))
                    {
                        completedDutiesFromGame.Add(duty.Content.RowId);
                        _completedDutiesMirror.Add(duty.Content.RowId);
                    }
                }
                catch (Exception ex)
                {
                    Svc.Log.Warning($"Failed to check completion for duty {duty.Content.RowId}: {ex.Message}");
                }
            }

            Svc.Log.Info($"Populated local mirror with {completedDutiesFromGame.Count} completed duties from game state.");
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
            
            Svc.Log.Debug($"Built achievement mapping with {mapping.Count} entries");
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
            
            Svc.Log.Debug($"Built quest mapping with {mapping.Count} entries");
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
