using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using ECommons.DalamudServices;
using PartyFinderReborn.Models;

namespace PartyFinderReborn.Services;

/// <summary>
/// Service for communicating with the Party Finder server API
/// </summary>
public class PartyFinderApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Configuration _configuration;
    private static readonly ConcurrentDictionary<string, object> _cache = new();
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    
    public PartyFinderApiService(Configuration configuration)
    {
        _configuration = configuration;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent);
        
        // Set API key if available
        if (!string.IsNullOrEmpty(configuration.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {configuration.ApiKey}");
        }
    }
    
    /// <summary>
    /// Test the API connection
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/auth/plugin/test/");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to test API connection: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get current user profile
    /// </summary>
    public async Task<UserProfile?> GetUserProfileAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/auth/plugin/profile/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                Svc.Log.Debug($"User profile API response: {json}");
                var userProfile = JsonConvert.DeserializeObject<UserProfile>(json);
                if (userProfile != null)
                {
                    Svc.Log.Debug($"Deserialized user profile for {userProfile.DisplayName}");
                }
                return userProfile;
            }
            
            Svc.Log.Warning($"Failed to get user profile: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting user profile: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Get party listings with optional filters and pagination
    /// </summary>
    /// <param name="filters">Optional filters to apply</param>
    /// <param name="pageUrl">Optional specific page URL to fetch</param>
    /// <returns>API response containing listings and pagination info</returns>
    public async Task<ApiResponse<PartyListing>?> GetListingsAsync(ListingFilters? filters = null, string? pageUrl = null)
    {
        try
        {
            string url;
            
            if (!string.IsNullOrEmpty(pageUrl))
            {
                // Use the provided page URL directly
                url = pageUrl;
            }
            else
            {
                // Build URL from base URL and filters
                url = $"{Constants.ApiBaseUrl}/api/v1/listings/";
                
                if (filters != null)
                {
                    var queryParams = filters.ToQueryParameters();
                    if (queryParams.Count > 0)
                    {
                        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                        url += "?" + queryString;
                    }
                }
            }
            
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                
                // Try deserializing as ApiResponse first
                try
                {
                    var apiResponse = JsonConvert.DeserializeObject<ApiResponse<PartyListing>>(json);
                    if (apiResponse != null && apiResponse.Results != null)
                        return apiResponse;
                }
                catch (JsonSerializationException)
                {
                    // Fall back to plain array
                }
                
                // Otherwise, try deserializing as a plain list
                try
                {
                    var plainList = JsonConvert.DeserializeObject<List<PartyListing>>(json);
                    if (plainList != null)
                        return new ApiResponse<PartyListing> { Results = plainList, Count = plainList.Count };
                }
                catch (JsonSerializationException)
                {
                    Svc.Log.Error($"Failed to deserialize listings response: {json}");
                }
            }
            
            Svc.Log.Warning($"Failed to get listings: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting listings: {ex.Message}");
            return null;
        }
    }
    

    /// <summary>
    /// Get listings from a specific page URL (for pagination navigation)
    /// </summary>
    /// <param name="url">The exact URL to fetch (from Next/Previous links)</param>
    /// <returns>API response containing listings and pagination info</returns>
    public async Task<ApiResponse<PartyListing>?> GetListingsPageAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
            
        try
        {
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                
                // Try deserializing as ApiResponse first
                try
                {
                    var apiResponse = JsonConvert.DeserializeObject<ApiResponse<PartyListing>>(json);
                    if (apiResponse != null && apiResponse.Results != null)
                        return apiResponse;
                }
                catch (JsonSerializationException)
                {
                    // Fall back to plain array
                }
                
                // Otherwise, try deserializing as a plain list
                try
                {
                    var plainList = JsonConvert.DeserializeObject<List<PartyListing>>(json);
                    if (plainList != null)
                        return new ApiResponse<PartyListing> { Results = plainList, Count = plainList.Count };
                }
                catch (JsonSerializationException)
                {
                    Svc.Log.Error($"Failed to deserialize listings page response: {json}");
                }
            }
            
            Svc.Log.Warning($"Failed to get listings page: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting listings page: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Get popular tags
    /// </summary>
    public async Task<ApiResponse<PopularItem>?> GetPopularTagsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/v1/tags/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ApiResponse<PopularItem>>(json);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting popular tags: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Get a specific listing by ID
    /// </summary>
    public async Task<PartyListing?> GetListingAsync(string id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<PartyListing>(json);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting listing {id}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Create a new party listing
    /// </summary>
    public async Task<PartyListing?> CreateListingAsync(PartyListing listing)
    {
        try
        {
            var json = JsonConvert.SerializeObject(listing);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<PartyListing>(responseJson);
            }
            
            Svc.Log.Warning($"Failed to create listing: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error creating listing: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Update an existing party listing
    /// </summary>
    public async Task<PartyListing?> UpdateListingAsync(string id, PartyListing listing)
    {
        try
        {
            var json = JsonConvert.SerializeObject(listing);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PutAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<PartyListing>(responseJson);
            }
            
            Svc.Log.Warning($"Failed to update listing {id}: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error updating listing {id}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Delete a party listing
    /// </summary>
    public async Task<bool> DeleteListingAsync(string id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error deleting listing {id}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Join a party listing
    /// For now, this is mocked to return success = true for UI testing
    /// </summary>
    public async Task<JoinResult?> JoinListingAsync(string id)
    {
        try
        {
            // TODO: Replace with actual API call when endpoint is available
            // var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/join/", null);
            
            // For now, mock a successful response to test the UI workflow
            await Task.Delay(1000); // Simulate network delay
            
            var mockResult = new JoinResult
            {
                Success = true,
                Message = "Successfully joined the party!",
                PfCode = "1234", // Mock PF code for clipboard testing
                PartyFull = false // Could be randomized for testing
            };
            
            Svc.Log.Info($"Mock: Successfully 'joined' party listing {id}");
            return mockResult;
            
            // TODO: Uncomment when real endpoint is available
            /*
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<JoinResult>(json);
                return result;
            }
            
            Svc.Log.Warning($"Failed to join listing {id}: {response.StatusCode}");
            return null;
            */
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error joining listing {id}: {ex.Message}");
            return null;
        }
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Checks if a duty is marked as completed.
    /// </summary>
    public async Task<bool?> IsDutyCompletedAsync(uint dutyId)
    {
        var cacheKey = $"duty-{dutyId}-completed";
        if (_cache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is bool cachedResult)
        {
            return cachedResult;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/completed/");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                if (result != null && result.TryGetValue("completed", out var isCompleted))
                {
                    _cache.TryAdd(cacheKey, isCompleted);
                    return isCompleted;
                }
            }
            Svc.Log.Warning($"Failed to get duty completion status for {dutyId}: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error checking duty completion status for {dutyId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a specific progression point for a duty is marked as completed.
    /// </summary>
    public async Task<bool?> IsProgPointCompletedAsync(uint dutyId, uint actionId)
    {
        var cacheKey = $"duty-{dutyId}-point-{actionId}-completed";
        if (_cache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is bool cachedResult)
        {
            return cachedResult;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/point/{actionId}/completed/");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                if (result != null && result.TryGetValue("completed", out var isCompleted))
                {
                    _cache.TryAdd(cacheKey, isCompleted);
                    return isCompleted;
                }
            }
            Svc.Log.Warning($"Failed to get progression point completion status for duty {dutyId}, action {actionId}: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error checking progression point completion status for duty {dutyId}, action {actionId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets a list of completed progression points for a duty.
    /// </summary>
    public async Task<List<uint>?> GetCompletedProgPointsAsync(uint dutyId)
    {
        var cacheKey = $"duty-{dutyId}-points";
        if (_cache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is List<uint> cachedResult)
        {
            return cachedResult;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/points/");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<Dictionary<string, List<uint>>>(json);
                if (result != null && result.TryGetValue("points", out var points))
                {
                    _cache.TryAdd(cacheKey, points);
                    return points;
                }
            }
            Svc.Log.Warning($"Failed to get completed progression points for {dutyId}: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting completed progression points for {dutyId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Marks a duty as completed on the server.
    /// </summary>
    public async Task<bool> MarkDutyCompletedAsync(uint dutyId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/complete/", null);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                // Clear related cache entries
                var cacheKey = $"duty-{dutyId}-completed";
                _cache.TryRemove(cacheKey, out _);
                return true;
            }
            
            Svc.Log.Warning($"Failed to mark duty {dutyId} as completed: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error marking duty {dutyId} as completed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Marks a specific progression point for a duty as completed on the server.
    /// </summary>
    public async Task<bool> MarkProgPointCompletedAsync(uint dutyId, uint actionId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/point/{actionId}/complete/", null);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                // Clear related cache entries
                var completedCacheKey = $"duty-{dutyId}-point-{actionId}-completed";
                var pointsCacheKey = $"duty-{dutyId}-points";
                _cache.TryRemove(completedCacheKey, out _);
                _cache.TryRemove(pointsCacheKey, out _);
                return true;
            }
            
            Svc.Log.Warning($"Failed to mark progression point for duty {dutyId}, action {actionId} as completed: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error marking progression point for duty {dutyId}, action {actionId} as completed: {ex.Message}");
            return false;
        }
    }

}
