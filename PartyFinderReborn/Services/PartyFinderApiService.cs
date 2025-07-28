using System;
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
                return JsonConvert.DeserializeObject<UserProfile>(json);
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
    /// Get party listings with optional filters
    /// </summary>
    public async Task<ApiResponse<PartyListing>?> GetListingsAsync(ListingFilters? filters = null)
    {
        try
        {
            var url = $"{Constants.ApiBaseUrl}/api/v1/listings/";
            
            if (filters != null)
            {
                var queryParams = filters.ToQueryParameters();
                if (queryParams.Count > 0)
                {
                    var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                    url += "?" + queryString;
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
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
