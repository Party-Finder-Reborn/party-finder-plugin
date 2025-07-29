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
    
    // Cached completion data
    private HashSet<uint> _completedDuties = new();
    private Dictionary<uint, List<uint>> _seenProgPoints = new();
    
    // Tracking state
    private bool _isInitialized = false;
    private DateTime _lastSync = DateTime.MinValue;
    private TimeSpan SyncInterval => TimeSpan.FromSeconds(_configuration.SyncDebounceSeconds);
    
    // Thread-safety for external prog-point pushes
    private readonly SemaphoreSlim _progPointsSemaphore = new(1, 1);
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    
    public DutyProgressService(ContentFinderService contentFinderService, PartyFinderApiService apiService, Configuration configuration)
    {
        _contentFinderService = contentFinderService;
        _apiService = apiService;
        _configuration = configuration;
        
        // Hook into relevant game events
        //Svc.DutyState.DutyCompleted += OnDutyCompleted;
        //Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        
        _ = Task.Run(InitializeAsync);
    }
    
    public void Dispose()
    {
        // Unhook events
        //Svc.DutyState.DutyCompleted -= OnDutyCompleted;
        //Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        
        // Clean up thread-safety resources
        _progPointsSemaphore?.Dispose();
        _syncSemaphore?.Dispose();
    }
    
    /// <summary>
    /// Initialize the service and load existing progress data
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            if (!_isInitialized)
            {
                await LoadProgressFromGame();
                await SyncWithServer();
                _isInitialized = true;
                
                Svc.Log.Info("DutyProgressService initialized successfully");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to initialize DutyProgressService: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load progress data from the game client using UIState methods
    /// </summary>
    private async Task LoadProgressFromGame()
    {
        try
        {
            // Clear existing completed duties data but preserve progress points
            _completedDuties.Clear();
            // NOTE: We intentionally do NOT clear _seenProgPoints here - they are a permanent record
            
            // Use UIState methods to check completion status
            await LoadCompletedDutiesFromUIState();
            
            Svc.Log.Info($"Loaded {_completedDuties.Count} completed duties from game data");
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load progress from game: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load completed duties using UIState methods
    /// </summary>
    private Task LoadCompletedDutiesFromUIState()
    {
        return Task.Run(() =>
        {
            try
            {
                var contentFinderConditionSheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
                if (contentFinderConditionSheet == null) return;
                
                foreach (var cfc in contentFinderConditionSheet)
                {
                    if (cfc.RowId == 0) continue;
                    
                    // Get the InstanceContent ID from ContentFinderCondition
                    var instanceContentId = GetInstanceContentId(cfc);
                    if (instanceContentId == 0) continue;
                    
                    // Check if this instance content is completed using UIState
                    if (UIState.IsInstanceContentCompleted(instanceContentId))
                    {
                        _completedDuties.Add(cfc.RowId);
                    }
                }
                
                Svc.Log.Debug($"Loaded {_completedDuties.Count} completed duties using UIState");
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to load duties from UIState: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Get the InstanceContent ID from a ContentFinderCondition
    /// </summary>
    private uint GetInstanceContentId(ContentFinderCondition cfc)
    {
        try
        {
            // ContentFinderCondition has a Content field that links to InstanceContent
            // This might be direct or need mapping - we'll need to investigate the structure
            return cfc.Content.RowId;
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Load completed duties based on achievement data (deprecated - kept for fallback)
    /// </summary>
    private Task LoadCompletedDutiesFromAchievements()
    {
        return Task.Run(() =>
        {
            try
            {
                var achievementSheet = Svc.Data.GetExcelSheet<LuminaAchievement>();
                if (achievementSheet == null) return;
                
                unsafe
                {
                    // Get the achievement manager
                    var achievementManager = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
                    if (achievementManager == null) return;
                    
                    // Dynamically build achievement to duty mapping
                    var achievementToDutyMap = BuildAchievementToDutyMapping();
                    
                    foreach (var (achievementId, dutyIds) in achievementToDutyMap)
                    {
                        if (achievementManager->IsComplete((int)achievementId))
                        {
                            foreach (var dutyId in dutyIds)
                            {
                                _completedDuties.Add(dutyId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to load achievements: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Load completed duties based on quest completion flags
    /// </summary>
    private Task LoadCompletedDutiesFromQuests()
    {
        return Task.Run(() =>
        {
            try
            {
                var questSheet = Svc.Data.GetExcelSheet<Quest>();
                if (questSheet == null) return;
                
                // Dynamically build quest to duty mapping
                var questToDutyMap = BuildQuestToDutyMapping();
                
                foreach (var (questId, dutyIds) in questToDutyMap)
                {
                    if (QuestManager.IsQuestComplete((ushort)questId))
                    {
                        foreach (var dutyId in dutyIds)
                        {
                            _completedDuties.Add(dutyId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to load quest completions: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Load progress data from UI agents
    /// </summary>
    private unsafe void LoadProgressFromUIAgents()
    {
        try
        {
            // Try to get data from ContentsFinder agent
            var contentsFinderAgent = AgentContentsFinder.Instance();
            if (contentsFinderAgent != null)
            {
                // This would need more investigation into the agent structure
                // to extract completion data
            }
            
            // Try to get data from other relevant agents
            // This is where we'd hook into the party finder data, achievement progress, etc.
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load from UI agents: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sync local progress data with the server
    /// </summary>
    public async Task SyncWithServer()
    {
        // Use semaphore to prevent multiple simultaneous syncs (debouncing)
        if (!await _syncSemaphore.WaitAsync(100)) // Quick timeout for debouncing
            return;
        
        try
        {
            if (DateTime.Now - _lastSync < SyncInterval)
                return; // Don't sync too frequently
            
            // Convert to the format expected by the API
            var completedDutiesList = _completedDuties.ToList();
            var seenProgPointsDict = _seenProgPoints.ToDictionary(
                kvp => kvp.Key.ToString(), 
                kvp => kvp.Value
            );
            
            // Update server with our local data
            var completedSuccess = await _apiService.UpdateCompletedDutiesAsync(completedDutiesList);
            var progPointsSuccess = await _apiService.UpdateSeenProgPointsAsync(seenProgPointsDict);
            
            if (completedSuccess && progPointsSuccess)
            {
                _lastSync = DateTime.Now;
                Svc.Log.Debug($"Successfully synced progress data with server");
            }
            else
            {
                Svc.Log.Warning("Failed to sync some progress data with server");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to sync with server: {ex.Message}");
        }
        finally
        {
            _syncSemaphore.Release();
        }
    }
    
    /// <summary>
    /// Mark a duty as completed locally and sync with server
    /// </summary>
    public async Task MarkDutyCompleted(uint dutyId)
    {
        if (_completedDuties.Add(dutyId))
        {
            Svc.Log.Info($"Marked duty {dutyId} as completed");
            await SyncWithServer();
        }
    }
    
    /// <summary>
    /// Add a seen progress point for a duty - Thread-safe for external prog-point pushes
    /// </summary>
    public async Task AddSeenProgPoint(uint dutyId, uint actionId)
    {
        await _progPointsSemaphore.WaitAsync();
        
        try
        {
            bool wasAdded = false;
            
            if (!_seenProgPoints.ContainsKey(dutyId))
                _seenProgPoints[dutyId] = new List<uint>();
            
            if (!_seenProgPoints[dutyId].Contains(actionId))
            {
                _seenProgPoints[dutyId].Add(actionId);
                wasAdded = true;
                Svc.Log.Info($"📝 SAVED PROGRESS POINT: Action {actionId} for duty {dutyId} (Total: {_seenProgPoints[dutyId].Count} for this duty)");
            }
            else
            {
                Svc.Log.Debug($"Progress point {actionId} already exists for duty {dutyId}, skipping");
            }
            
            // Only sync if we actually added something new
            if (wasAdded)
            {
                // Fire and forget sync - don't await to prevent blocking multiple rapid calls
                _ = Task.Run(async () => await SyncWithServer());
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error adding progress point {actionId} for duty {dutyId}: {ex.Message}");
        }
        finally
        {
            _progPointsSemaphore.Release();
        }
    }
    
    /// <summary>
    /// Check if a duty has been completed using real-time UIState check
    /// </summary>
    public bool IsDutyCompleted(uint dutyId)
    {
        try
        {
            // First check cached data for performance
            if (_completedDuties.Contains(dutyId))
                return true;
            
            // Real-time check using UIState
            var cfc = _contentFinderService.GetContentFinderCondition(dutyId);
            if (cfc == null) return false;
            
            var instanceContentId = GetInstanceContentId(cfc.Value);
            if (instanceContentId == 0) return false;
            
            var isCompleted = UIState.IsInstanceContentCompleted(instanceContentId);
            
            // Update cache if we found it's completed
            if (isCompleted)
            {
                _completedDuties.Add(dutyId);
            }
            
            return isCompleted;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to check duty completion for {dutyId}: {ex.Message}");
            // Fallback to cached data
            return _completedDuties.Contains(dutyId);
        }
    }
    
    /// <summary>
    /// Check if a duty is unlocked using UIState
    /// </summary>
    public bool IsDutyUnlocked(uint dutyId)
    {
        try
        {
            var cfc = _contentFinderService.GetContentFinderCondition(dutyId);
            if (cfc == null) return false;
            
            var instanceContentId = GetInstanceContentId(cfc.Value);
            if (instanceContentId == 0) return false;
            
            return UIState.IsInstanceContentUnlocked(instanceContentId);
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to check duty unlock status for {dutyId}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get seen progress points for a duty
    /// </summary>
    public List<uint> GetSeenProgPoints(uint dutyId)
    {
        return _seenProgPoints.GetValueOrDefault(dutyId, new List<uint>());
    }
    
    /// <summary>
    /// Check if a specific progress point has been seen
    /// </summary>
    public bool HasSeenProgPoint(uint dutyId, uint actionId)
    {
        return _seenProgPoints.ContainsKey(dutyId) && _seenProgPoints[dutyId].Contains(actionId);
    }
    
    /// <summary>
    /// Get the total count of completed duties
    /// </summary>
    public int GetCompletedDutiesCount()
    {
        return _completedDuties.Count;
    }
    
    /// <summary>
    /// Get the total count of duties with progress points
    /// </summary>
    public int GetProgPointsDutiesCount()
    {
        return _seenProgPoints.Count;
    }
    
    /// <summary>
    /// Get the total count of all progress points across all duties
    /// </summary>
    public int GetTotalProgPointsCount()
    {
        return _seenProgPoints.Values.Sum(list => list.Count);
    }
    
    /// <summary>
    /// Get all completed duty IDs
    /// </summary>
    public IReadOnlySet<uint> GetCompletedDuties()
    {
        return _completedDuties.ToHashSet();
    }
    
    /// <summary>
    /// Force a refresh of progress data from the game
    /// </summary>
    public async Task RefreshProgressData()
    {
        await LoadProgressFromGame();
        await SyncWithServer();
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
