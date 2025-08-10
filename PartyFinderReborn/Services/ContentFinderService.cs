using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using PartyFinderReborn.Models;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for working with Content Finder Condition data from the game's Excel sheets
/// </summary>
public class ContentFinderService : IDisposable
{
    private List<IDutyInfo> _list;
    private Dictionary<uint, IDutyInfo> _idLookup;
    private Dictionary<ushort, uint> _territoryToId;
    
    public ContentFinderService()
    {
        _list = new List<IDutyInfo>();
        _idLookup = new Dictionary<uint, IDutyInfo>();
        _territoryToId = new Dictionary<ushort, uint>();
        InitializeCache();
    }
    
    public void Dispose()
    {
        _list.Clear();
        _idLookup.Clear();
        _territoryToId.Clear();
    }
    
    private void InitializeCache()
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();

            _list.Clear();
            _idLookup.Clear();
            _territoryToId.Clear();

            foreach (var cfc in sheet)
            {
                if (cfc.RowId == 0) continue;

                // Skip invalid or empty entries
                if (cfc.Name.IsEmpty || string.IsNullOrEmpty(cfc.Name.ExtractText())) continue;

                var realDutyInfo = new RealDutyInfo(cfc);
                _list.Add(realDutyInfo);
                _idLookup[cfc.RowId] = realDutyInfo;

                // Build territory to ID mapping
                var territoryType = (ushort)cfc.TerritoryType.RowId;
                if (territoryType != 0 && !_territoryToId.ContainsKey(territoryType))
                {
                    // Store the first matching RowId for each territory
                    _territoryToId[territoryType] = cfc.RowId;
                }
            }

            // Add custom duties
            var huntDuty = new CustomDutyInfo { RowId = 9999, NameText = "Hunt", ContentTypeId = 0, ClassJobLevelRequired = 1, ItemLevelRequired = 0 };
            var fateDuty = new CustomDutyInfo { RowId = 9998, NameText = "FATE", ContentTypeId = 0, ClassJobLevelRequired = 1, ItemLevelRequired = 0 };
            var rolePlayingDuty = new CustomDutyInfo { RowId = 9997, NameText = "Role Playing", ContentTypeId = 0, ClassJobLevelRequired = 1, ItemLevelRequired = 0 };
            var duelDuty = new CustomDutyInfo { RowId = 9996, NameText = "Duel", ContentTypeId = 0, ClassJobLevelRequired = 1, ItemLevelRequired = 0 };
            
            _list.Add(huntDuty);
            _list.Add(fateDuty);
            _list.Add(rolePlayingDuty);
            _list.Add(duelDuty);

            _idLookup[9999] = huntDuty;
            _idLookup[9998] = fateDuty;
            _idLookup[9997] = rolePlayingDuty;
            _idLookup[9996] = duelDuty;

            // Sort the list by NameText
            _list = _list.OrderBy(d => d.NameText).ToList();
            
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to initialize ContentFinderService: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get Content Finder Condition by ID
    /// </summary>
    public ContentFinderCondition? GetContentFinderCondition(uint cfcId)
    {
        var dutyInfo = _idLookup?.GetValueOrDefault(cfcId);
        return dutyInfo is RealDutyInfo realDuty ? realDuty._contentFinderCondition : null;
    }
    
    /// <summary>
    /// Helper method for external code that expects real ContentFinderCondition objects.
    /// Returns ContentFinderCondition for real duties, null for custom duties.
    /// Use this when external logic relies on ContentType/territory data.
    /// </summary>
    /// <param name="id">Duty ID</param>
    /// <returns>ContentFinderCondition if duty is real, null if custom</returns>
    public ContentFinderCondition? GetRealDuty(uint id)
    {
        var dutyInfo = _idLookup?.GetValueOrDefault(id);
        return dutyInfo is RealDutyInfo realDuty ? realDuty._contentFinderCondition : null;
    }
    
    /// <summary>
    /// Get all duties for dropdown/search purposes
    /// </summary>
    public List<IDutyInfo> GetAllDuties()
    {
        if (_list == null)
            return new List<IDutyInfo>();
            
        // Filter out duties with duplicate names, keeping only the first occurrence
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return _list.Where(duty => seenNames.Add(duty.NameText)).ToList();
    }

    // Legacy overload
    public List<ContentFinderCondition> GetAllDutiesLegacy()
    {
        return _list?.Where(d => d is RealDutyInfo).Cast<RealDutyInfo>().Select(d => d._contentFinderCondition).ToList() ?? new List<ContentFinderCondition>();
    }
    
    /// <summary>
    /// Search duties by name
    /// </summary>
    public List<IDutyInfo> SearchDuties(string searchTerm)
    {
        if (_list == null || string.IsNullOrWhiteSpace(searchTerm))
            return GetAllDuties();
        
        var lowerSearch = searchTerm.ToLowerInvariant();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return _list
            .Where(d => d.NameText.ToLowerInvariant().Contains(lowerSearch) && seenNames.Add(d.NameText))
            .ToList();
    }

    // Legacy overload
    public List<ContentFinderCondition> SearchDutiesLegacy(string searchTerm)
    {
        if (_list == null || string.IsNullOrWhiteSpace(searchTerm))
            return GetAllDutiesLegacy();
        
        var lowerSearch = searchTerm.ToLowerInvariant();
        return _list
            .Where(d => d is RealDutyInfo && ((RealDutyInfo)d)._contentFinderCondition.Name.ExtractText().ToLowerInvariant().Contains(lowerSearch))
            .Cast<RealDutyInfo>().Select(d => d._contentFinderCondition).ToList();
    }
    
    /// <summary>
    /// Get duties filtered by content type
    /// </summary>
    public List<IDutyInfo> GetDutiesByContentType(string contentType)
    {
        if (_list == null)
            return new List<IDutyInfo>();
        
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return _list
            .Where(d => string.Equals(GetContentTypeName(d), contentType, StringComparison.OrdinalIgnoreCase) && seenNames.Add(d.NameText))
            .ToList();
    }
    
    // Legacy overload
    public List<ContentFinderCondition> GetDutiesByContentTypeLegacy(string contentType)
    {
        if (_list == null)
            return new List<ContentFinderCondition>();
        
        return _list
            .Where(d => d is RealDutyInfo && string.Equals(GetContentTypeName(((RealDutyInfo)d)._contentFinderCondition), contentType, StringComparison.OrdinalIgnoreCase))
            .Cast<RealDutyInfo>().Select(d => d._contentFinderCondition).ToList();
    }
    
    /// <summary>
    /// Get duties suitable for high-end content (raids, extremes, etc.)
    /// </summary>
    public List<IDutyInfo> GetHighEndDuties()
    {
        if (_list == null)
            return new List<IDutyInfo>();
        
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return _list
            .Where(d => (d.HighEndDuty || GetContentTypeName(d).Contains("Savage") || GetContentTypeName(d).Contains("Ultimate")) && seenNames.Add(d.NameText))
            .ToList();
    }
    
    // Legacy overload
    public List<ContentFinderCondition> GetHighEndDutiesLegacy()
    {
        if (_list == null)
            return new List<ContentFinderCondition>();
        
        return _list
            .Where(d => d is RealDutyInfo && (d.HighEndDuty || GetContentTypeName(((RealDutyInfo)d)._contentFinderCondition).Contains("Savage") || GetContentTypeName(((RealDutyInfo)d)._contentFinderCondition).Contains("Ultimate")))
            .Cast<RealDutyInfo>().Select(d => d._contentFinderCondition).ToList();
    }
    
    /// <summary>
    /// Get a display-friendly name for the duty
    /// </summary>
    public string GetDutyDisplayName(uint cfcId)
    {
        var dutyInfo = _idLookup?.GetValueOrDefault(cfcId);
        if (dutyInfo == null)
            return $"Unknown Duty (#{cfcId})";
            
        return dutyInfo.NameText;
    }
    
    /// <summary>
    /// Get a detailed display name including content type and level info
    /// </summary>
    public string GetDutyDetailedDisplayName(uint cfcId)
    {
        var dutyInfo = _idLookup?.GetValueOrDefault(cfcId);
        if (dutyInfo == null)
            return $"Unknown Duty (#{cfcId})";
            
        var contentType = GetContentTypeName(dutyInfo);
        return $"{dutyInfo.NameText} ({contentType}) - Lv.{dutyInfo.ClassJobLevelRequired}, ilvl {dutyInfo.ItemLevelRequired}";
    }
    
    /// <summary>
    /// Get display name for dropdown use (name + content type)
    /// </summary>
    public string GetDutyDropdownDisplayName(ContentFinderCondition duty)
    {
        if (duty.Name.IsEmpty)
            return $"Unknown Duty (#{duty.RowId})";
            
        var contentType = GetContentTypeName(duty);
        return $"{duty.Name.ExtractText()} ({contentType})";
    }
    
    /// <summary>
    /// Validate if a Content Finder ID exists
    /// </summary>
    public bool IsValidDuty(uint cfcId)
    {
        return _idLookup?.ContainsKey(cfcId) ?? false;
    }
    
    /// <summary>
    /// Get the content type name for a duty
    /// </summary>
    public string GetContentTypeName(ContentFinderCondition duty)
    {
        try
        {
            var contentTypeSheet = Svc.Data.GetExcelSheet<ContentType>();
            var contentType = contentTypeSheet?.GetRow(duty.ContentType.RowId);
            return contentType != null ? contentType.Value.Name.ExtractText() : "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
    
    /// <summary>
    /// Get the content type name for a duty info
    /// </summary>
    public string GetContentTypeName(IDutyInfo duty)
    {
        if (duty is RealDutyInfo realDuty)
        {
            return GetContentTypeName(realDuty._contentFinderCondition);
        }
        
        // For custom duties
        return duty.ContentTypeId == 0 ? "Unspecified" : "Unknown";
    }
    
    /// <summary>
    /// Get the first valid CfcId for use as a default
    /// </summary>
    public uint GetFirstValidCfcId()
    {
        if (_list == null || _list.Count == 0)
            return 1; // Fallback to 1 if no duties are loaded
            
        return _list.First().RowId;
    }
    
    /// <summary>
    /// Get all unique content types for filtering
    /// </summary>
    public List<string> GetContentTypes()
    {
        if (_list == null)
            return new List<string>();
        
        return _list
            .OfType<RealDutyInfo>()
            .Select(d => GetContentTypeName(d._contentFinderCondition))
            .Where(ct => !string.IsNullOrEmpty(ct) && ct != "Unknown")
            .Distinct()
            .OrderBy(ct => ct)
            .ToList();
    }
    
    /// <summary>
    /// Get Content Finder Condition ID by territory type
    /// </summary>
    /// <param name="territory">Territory type ID</param>
    /// <returns>CFC ID if found, null otherwise</returns>
    public uint? GetCfcIdByTerritory(ushort territory)
    {
        try
        {
            if (_territoryToId.TryGetValue(territory, out var cfcId))
            {
                return cfcId;
            }
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to get CFC ID by territory {territory}: {ex.Message}");
            return null;
        }
    }
}
