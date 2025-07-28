using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace PartyFinderReborn.Models;

/// <summary>
/// User profile information from the server
/// </summary>
public class UserProfile
{
    [JsonProperty("discord_id")]
    public string DiscordId { get; set; } = string.Empty;
    
    [JsonProperty("discord_username")]
    public string DiscordUsername { get; set; } = string.Empty;
    
    [JsonProperty("discord_global_name")]
    public string? DiscordGlobalName { get; set; }
    
    [JsonProperty("discord_avatar_url")]
    public string DiscordAvatarUrl { get; set; } = string.Empty;
    
    [JsonProperty("is_authenticated")]
    public bool IsAuthenticated { get; set; }
    
    [JsonProperty("has_api_key")]
    public bool HasApiKey { get; set; }
    
    [JsonProperty("completed_duties")]
    public List<uint> CompletedDuties { get; set; } = new();
    
    [JsonProperty("seen_prog_points")]
    public Dictionary<string, List<uint>> SeenProgPoints { get; set; } = new();
    
    public string DisplayName => DiscordGlobalName ?? DiscordUsername ?? DiscordId;
    
    /// <summary>
    /// Check if a duty has been completed
    /// </summary>
    public bool HasCompletedDuty(uint dutyId) => CompletedDuties.Contains(dutyId);
    
    /// <summary>
    /// Get seen progress points for a specific duty
    /// </summary>
    public List<uint> GetSeenProgPoints(uint dutyId)
    {
        var key = dutyId.ToString();
        return SeenProgPoints.TryGetValue(key, out var points) ? points : new List<uint>();
    }
    
    /// <summary>
    /// Check if a specific progress point has been seen for a duty
    /// </summary>
    public bool HasSeenProgPoint(uint dutyId, uint actionId)
    {
        return GetSeenProgPoints(dutyId).Contains(actionId);
    }
}


/// <summary>
/// Party listing information
/// </summary>
public class PartyListing
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("cfc_id")]
    public uint CfcId { get; set; }
    
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonProperty("user_tags")]
    public List<string> UserTags { get; set; } = new();
    
    [JsonProperty("user_strategies")]
    public List<string> UserStrategies { get; set; } = new();
    
    // === REQUIREMENT FIELDS ===
    
    [JsonProperty("min_item_level")]
    public int MinItemLevel { get; set; }
    
    [JsonProperty("max_item_level")]
    public int MaxItemLevel { get; set; }
    
    [JsonProperty("required_clears")]
    public List<uint> RequiredClears { get; set; } = new();
    
    [JsonProperty("prog_point")]
    public string ProgPoint { get; set; } = string.Empty;
    
    [JsonProperty("experience_level")]
    public string ExperienceLevel { get; set; } = string.Empty;
    
    [JsonProperty("required_plugins")]
    public List<string> RequiredPlugins { get; set; } = new();
    
    [JsonProperty("voice_chat_required")]
    public bool VoiceChatRequired { get; set; }
    
    [JsonProperty("job_requirements")]
    public Dictionary<string, object> JobRequirements { get; set; } = new();
    
    [JsonProperty("loot_rules")]
    public string LootRules { get; set; } = string.Empty;
    
    [JsonProperty("parse_requirement")]
    public string ParseRequirement { get; set; } = string.Empty;
    
    [JsonProperty("datacenter")]
    public string Datacenter { get; set; } = string.Empty;
    
    [JsonProperty("world")]
    public string World { get; set; } = string.Empty;
    
    [JsonProperty("pf_code")]
    public string PfCode { get; set; } = string.Empty;
    
    [JsonProperty("creator")]
    public UserProfile? Creator { get; set; }
    
    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; set; }
    
    [JsonProperty("updated_at")]
    public DateTime UpdatedAt { get; set; }
    
    // Computed properties
    public bool IsActive => Status == "active";
    public string TagsDisplay => string.Join(", ", UserTags);
    public string StrategiesDisplay => string.Join(", ", UserStrategies);
    public bool IsRoleplay => UserTags.Any(tag => tag.ToLowerInvariant().Contains("rp") || tag.ToLowerInvariant().Contains("roleplay"));
    
    public string StatusDisplay => Status switch
    {
        "draft" => "Draft",
        "active" => "Active",
        "full" => "Full",
        "completed" => "Completed",
        "cancelled" => "Cancelled",
        _ => Status
    };
    
    public string ExperienceLevelDisplay => ExperienceLevel switch
    {
        "fresh" => "Fresh/Learning",
        "some_exp" => "Some Experience",
        "experienced" => "Experienced",
        "farm" => "Farm/Clear",
        "reclear" => "Weekly Reclear",
        _ => ExperienceLevel
    };
    
    public string LootRulesDisplay => LootRules switch
    {
        "ffa" => "Free for All",
        "need_greed" => "Need/Greed",
        "master_loot" => "Master Loot",
        "reserved" => "Reserved Items",
        "discuss" => "Discuss Before",
        _ => LootRules
    };
    
    public string ParseRequirementDisplay => ParseRequirement switch
    {
        "none" => "No Parse Requirement",
        "grey" => "Grey+ (1-24th percentile)",
        "green" => "Green+ (25-49th percentile)",
        "blue" => "Blue+ (50-74th percentile)",
        "purple" => "Purple+ (75-94th percentile)",
        "orange" => "Orange+ (95-98th percentile)",
        "gold" => "Gold+ (99th percentile)",
        _ => ParseRequirement
    };
    
    public string LocationDisplay => $"{World} ({DatacenterDisplay})";
    
    public string DatacenterDisplay => Datacenter switch
    {
        "aether" => "Aether",
        "crystal" => "Crystal",
        "dynamis" => "Dynamis",
        "primal" => "Primal",
        "chaos" => "Chaos",
        "light" => "Light",
        "materia" => "Materia",
        "elemental" => "Elemental",
        "gaia" => "Gaia",
        "mana" => "Mana",
        "meteor" => "Meteor",
        _ => Datacenter
    };
    
    public string CreatorDisplay => Creator?.DisplayName ?? "Unknown";
    
    public string RequirementsDisplay
    {
        get
        {
            var requirements = new List<string>();
            
            if (MinItemLevel > 0)
            {
                if (MaxItemLevel > 0)
                    requirements.Add($"ilvl {MinItemLevel}-{MaxItemLevel}");
                else
                    requirements.Add($"ilvl {MinItemLevel}+");
            }
            
            if (!string.IsNullOrEmpty(ProgPoint))
                requirements.Add($"Prog: {ProgPoint}");
            
            if (ExperienceLevel != "fresh")
                requirements.Add(ExperienceLevelDisplay);
            
            if (ParseRequirement != "none")
                requirements.Add(ParseRequirementDisplay);
            
            if (VoiceChatRequired)
                requirements.Add("Voice required");
            
            return requirements.Count > 0 ? string.Join(", ", requirements) : "No special requirements";
        }
    }
}

/// <summary>
/// Popular tag/item information
/// </summary>
public class PopularItem
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty("usage_count")]
    public int UsageCount { get; set; }
    
    [JsonProperty("first_used")]
    public DateTime FirstUsed { get; set; }
    
    [JsonProperty("last_used")]
    public DateTime LastUsed { get; set; }
}

/// <summary>
/// API response wrapper for paginated results
/// </summary>
public class ApiResponse<T>
{
    [JsonProperty("count")]
    public int Count { get; set; }
    
    [JsonProperty("next")]
    public string? Next { get; set; }
    
    [JsonProperty("previous")]
    public string? Previous { get; set; }
    
    [JsonProperty("results")]
    public List<T> Results { get; set; } = new();
}

/// <summary>
/// Filter criteria for searching party listings
/// </summary>
public class ListingFilters
{
    public string? Search { get; set; }
    public string? Datacenter { get; set; }
    public string? World { get; set; }
    public string? Status { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool? RpFlag { get; set; }
    public string? Ordering { get; set; } = "-created_at";
    
    public Dictionary<string, string> ToQueryParameters()
    {
        var parameters = new Dictionary<string, string>();
        
        if (!string.IsNullOrEmpty(Search))
            parameters["search"] = Search;
        
        if (!string.IsNullOrEmpty(Datacenter))
            parameters["datacenter"] = Datacenter;
        
        if (!string.IsNullOrEmpty(World))
            parameters["world"] = World;
        
        if (!string.IsNullOrEmpty(Status))
            parameters["status"] = Status;
        
        if (RpFlag.HasValue)
            parameters["rp_flag"] = RpFlag.Value.ToString().ToLower();
        
        if (!string.IsNullOrEmpty(Ordering))
            parameters["ordering"] = Ordering;
        
        // Add tags as comma-separated
        if (Tags.Count > 0)
            parameters["tags"] = string.Join(",", Tags);
        
        return parameters;
    }
    
    public void Reset()
    {
        Search = null;
        Datacenter = null;
        World = null;
        Status = null;
        Tags.Clear();
        RpFlag = null;
        Ordering = "-created_at";
    }
}

/// <summary>
/// Error response from the API
/// </summary>
public class ApiError
{
    [JsonProperty("error")]
    public string? Error { get; set; }
    
    [JsonProperty("detail")]
    public string? Detail { get; set; }
    
    public string Message => Error ?? Detail ?? "Unknown error occurred";
}

/// <summary>
/// Available datacenters
/// </summary>
public static class Datacenters
{
    public static readonly Dictionary<string, string> All = new()
    {
        { "aether", "Aether" },
        { "crystal", "Crystal" },
        { "dynamis", "Dynamis" },
        { "primal", "Primal" },
        { "chaos", "Chaos" },
        { "light", "Light" },
        { "materia", "Materia" },
        { "elemental", "Elemental" },
        { "gaia", "Gaia" },
        { "mana", "Mana" },
        { "meteor", "Meteor" }
    };
    
    public static readonly Dictionary<string, List<string>> Worlds = new()
    {
        ["aether"] = new() { "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren" },
        ["crystal"] = new() { "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera" },
        ["dynamis"] = new() { "Halicarnassus", "Maduin", "Marilith", "Seraph" },
        ["primal"] = new() { "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros" },
        ["chaos"] = new() { "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan" },
        ["light"] = new() { "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark" },
        ["materia"] = new() { "Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan" },
        ["elemental"] = new() { "Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Ramuh", "Tonberry", "Typhon", "Unicorn" },
        ["gaia"] = new() { "Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima" },
        ["mana"] = new() { "Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan" },
        ["meteor"] = new() { "Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus" }
    };
}

/// <summary>
/// Available party statuses
/// </summary>
public static class PartyStatuses
{
    public static readonly Dictionary<string, string> All = new()
    {
        { "draft", "Draft" },
        { "active", "Active" },
        { "full", "Full" },
        { "completed", "Completed" },
        { "cancelled", "Cancelled" }
    };
}
