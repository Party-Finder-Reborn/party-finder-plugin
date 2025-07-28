using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for working with Content Finder Condition data from the game's Excel sheets
/// </summary>
public class ContentFinderService : IDisposable
{
    private Dictionary<uint, ContentFinderCondition>? _contentFinderCache;
    private List<ContentFinderCondition>? _sortedDuties;
    
    public ContentFinderService()
    {
        InitializeCache();
    }
    
    public void Dispose()
    {
        _contentFinderCache?.Clear();
        _sortedDuties?.Clear();
    }
    
    private void InitializeCache()
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();

            _contentFinderCache = new Dictionary<uint, ContentFinderCondition>();
            var duties = new List<ContentFinderCondition>();
            
            foreach (var cfc in sheet)
            {
                if (cfc.RowId == 0) continue;
                
                // Skip invalid or empty entries
                if (cfc.Name.IsEmpty || string.IsNullOrEmpty(cfc.Name.ExtractText())) continue;
                
                _contentFinderCache[cfc.RowId] = cfc;
                duties.Add(cfc);
            }
            
            // Sort by name for better user experience
            _sortedDuties = duties.OrderBy(d => d.Name.ExtractText()).ToList();
            
            Svc.Log.Info($"Loaded {_contentFinderCache.Count} Content Finder Conditions");
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
        return _contentFinderCache?.GetValueOrDefault(cfcId);
    }
    
    /// <summary>
    /// Get all duties for dropdown/search purposes
    /// </summary>
    public List<ContentFinderCondition> GetAllDuties()
    {
        return _sortedDuties ?? new List<ContentFinderCondition>();
    }
    
    /// <summary>
    /// Search duties by name
    /// </summary>
    public List<ContentFinderCondition> SearchDuties(string searchTerm)
    {
        if (_sortedDuties == null || string.IsNullOrWhiteSpace(searchTerm))
            return GetAllDuties();
        
        var lowerSearch = searchTerm.ToLowerInvariant();
        return _sortedDuties
            .Where(d => (!d.Name.IsEmpty && d.Name.ExtractText().ToLowerInvariant().Contains(lowerSearch)) || 
                       GetContentTypeName(d).ToLowerInvariant().Contains(lowerSearch))
            .ToList();
    }
    
    /// <summary>
    /// Get duties filtered by content type
    /// </summary>
    public List<ContentFinderCondition> GetDutiesByContentType(string contentType)
    {
        if (_sortedDuties == null)
            return new List<ContentFinderCondition>();
        
        return _sortedDuties
            .Where(d => string.Equals(GetContentTypeName(d), contentType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    
    /// <summary>
    /// Get duties suitable for high-end content (raids, extremes, etc.)
    /// </summary>
    public List<ContentFinderCondition> GetHighEndDuties()
    {
        if (_sortedDuties == null)
            return new List<ContentFinderCondition>();
        
        return _sortedDuties
            .Where(d => d.HighEndDuty ||
                       GetContentTypeName(d).Contains("Savage") ||
                       GetContentTypeName(d).Contains("Ultimate"))
            .ToList();
    }
    
    /// <summary>
    /// Get a display-friendly name for the duty
    /// </summary>
    public string GetDutyDisplayName(uint cfcId)
    {
        var duty = GetContentFinderCondition(cfcId);
        if (duty == null)
            return $"Unknown Duty (#{cfcId})";
        
        if (duty.Value.Name.IsEmpty)
            return $"Unknown Duty (#{cfcId})";
            
        return duty.Value.Name.ExtractText();
    }
    
    /// <summary>
    /// Get a detailed display name including content type and level info
    /// </summary>
    public string GetDutyDetailedDisplayName(uint cfcId)
    {
        var duty = GetContentFinderCondition(cfcId);
        if (duty == null)
            return $"Unknown Duty (#{cfcId})";
        
        if (duty.Value.Name.IsEmpty)
            return $"Unknown Duty (#{cfcId})";
            
        var contentType = GetContentTypeName(duty.Value);
        return $"{duty.Value.Name.ExtractText()} ({contentType}) - Lv.{duty.Value.ClassJobLevelRequired}, ilvl {duty.Value.ItemLevelRequired}";
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
        return _contentFinderCache?.ContainsKey(cfcId) ?? false;
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
    /// Get the first valid CfcId for use as a default
    /// </summary>
    public uint GetFirstValidCfcId()
    {
        if (_sortedDuties == null || _sortedDuties.Count == 0)
            return 1; // Fallback to 1 if no duties are loaded
            
        return _sortedDuties.First().RowId;
    }
    
    /// <summary>
    /// Get all unique content types for filtering
    /// </summary>
    public List<string> GetContentTypes()
    {
        if (_sortedDuties == null)
            return new List<string>();
        
        return _sortedDuties
            .Select(GetContentTypeName)
            .Where(ct => !string.IsNullOrEmpty(ct) && ct != "Unknown")
            .Distinct()
            .OrderBy(ct => ct)
            .ToList();
    }
}
