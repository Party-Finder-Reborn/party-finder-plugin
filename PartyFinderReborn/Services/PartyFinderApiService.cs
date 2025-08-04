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
using PartyFinderReborn.Crypto;

namespace PartyFinderReborn.Services
{
    public enum ApiOperationType
    {
        Read,
        Write,
        QuickAction
    }

    public static class ApiOperationTypeDefaults
    {
        public static readonly TimeSpan Read = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan Write = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan QuickAction = TimeSpan.FromSeconds(2);
    }

/// <summary>
/// Service for communicating with the Party Finder server API
/// </summary>
public class PartyFinderApiService : IDisposable
{
    private readonly SignedHttpClient _httpClient;
    private readonly Configuration _configuration;
    private readonly ApiDebounceService _debounceService;
    private static readonly ConcurrentDictionary<string, object> _cache = new();
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    
    public PartyFinderApiService(Configuration configuration, ApiDebounceService debounceService)
    {
        _configuration = configuration;
        _debounceService = debounceService;
        _httpClient = new SignedHttpClient(configuration);
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
        return await _debounceService.RunIfAllowedAsync(ApiOperationType.Read, async () => 
        {
            return await GetUserProfileInternalAsync();
        });
    }
    
    /// <summary>
    /// Get current user profile without debouncing (for background operations)
    /// </summary>
    public async Task<UserProfile?> GetUserProfileAsync_NoDebounce()
    {
        return await GetUserProfileInternalAsync();
    }
    
    /// <summary>
    /// Internal method for getting user profile
    /// </summary>
    private async Task<UserProfile?> GetUserProfileInternalAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/auth/plugin/profile/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var userProfile = JsonConvert.DeserializeObject<UserProfile>(json);
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
        return await _debounceService.RunIfAllowedAsync(ApiOperationType.Read, async () => 
        {
            return await GetListingsInternalAsync(filters, pageUrl);
        });
    }
    
    /// <summary>
    /// Get party listings without debouncing (for background operations)
    /// </summary>
    /// <param name="filters">Optional filters to apply</param>
    /// <param name="pageUrl">Optional specific page URL to fetch</param>
    /// <returns>API response containing listings and pagination info</returns>
    public async Task<ApiResponse<PartyListing>?> GetListingsAsync_NoDebounce(ListingFilters? filters = null, string? pageUrl = null)
    {
        return await GetListingsInternalAsync(filters, pageUrl);
    }
    
    /// <summary>
    /// Internal method for getting listings
    /// </summary>
    private async Task<ApiResponse<PartyListing>?> GetListingsInternalAsync(ListingFilters? filters = null, string? pageUrl = null)
    {
        try
        {
            string url;
            
            if (!string.IsNullOrEmpty(pageUrl))
            {
                url = pageUrl;
            }
            else
            {
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
                
                try
                {
                    var apiResponse = JsonConvert.DeserializeObject<ApiResponse<PartyListing>>(json);
                    if (apiResponse != null && apiResponse.Results != null)
                        return apiResponse;
                }
                catch (JsonSerializationException)
                {
                    
                }
                
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
    public async Task<ListingRefreshResult> GetListingAsync(string id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var listing = JsonConvert.DeserializeObject<PartyListing>(json);
                return new ListingRefreshResult
                {
                    Success = true,
                    Listing = listing
                };
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ListingRefreshResult
                {
                    Success = false,
                    NotFound = true,
                    ErrorMessage = "Listing no longer exists"
                };
            }
            
            return new ListingRefreshResult
            {
                Success = false,
                NotFound = false,
                ErrorMessage = $"Failed to get listing: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting listing {id}: {ex.Message}");
            return new ListingRefreshResult
            {
                Success = false,
                NotFound = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Create a new party listing
    /// </summary>
public async Task<ListingResult> CreateListingAsync(PartyListing listing)
    {
        return await _debounceService.RunIfAllowedAsync(ApiOperationType.Write, async () => 
        {
            try
            {
                var json = JsonConvert.SerializeObject(listing);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PartyListing>(responseJson);
                    return new ListingResult
                    {
                        Success = true,
                        Listing = result
                    };
                }
                
                // Handle content moderation failure (422 Unprocessable Entity)
                if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(errorContent);
                        var moderationMessage = errorResponse?.GetValueOrDefault("content_moderation");
                        
                        if (!string.IsNullOrEmpty(moderationMessage))
                        {
                            return new ListingResult
                            {
                                Success = false,
                                ContentModerationFailed = true,
                                ModerationReason = moderationMessage,
                                ErrorMessage = "Content moderation failed"
                            };
                        }
                    }
                    catch (JsonSerializationException)
                    {
                        // If we can't parse the error response, fall through to generic error handling
                    }
                }
                
                var errorContent2 = await response.Content.ReadAsStringAsync();
                Svc.Log.Warning($"Failed to create listing: {response.StatusCode}");
                Svc.Log.Warning($"Error details: {errorContent2}");
                
                return new ListingResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to create listing: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error creating listing: {ex.Message}");
                return new ListingResult
                {
                    Success = false,
                    ErrorMessage = $"Error creating listing: {ex.Message}"
                };
            }
        }) ?? new ListingResult
        {
            Success = false,
            ErrorMessage = "Request was throttled. Please try again later."
        };
    }
    
    /// <summary>
    /// Update an existing party listing
    /// </summary>
public async Task<ListingResult> UpdateListingAsync(string id, PartyListing listing)
    {
        return await _debounceService.RunIfAllowedAsync(ApiOperationType.Write, async () => 
        {
            try
            {
                var json = JsonConvert.SerializeObject(listing);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PartyListing>(responseJson);
                    return new ListingResult
                    {
                        Success = true,
                        Listing = result
                    };
                }
                
                // Handle content moderation failure (422 Unprocessable Entity)
                if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(errorContent);
                        var moderationMessage = errorResponse?.GetValueOrDefault("content_moderation");
                        
                        if (!string.IsNullOrEmpty(moderationMessage))
                        {
                            return new ListingResult
                            {
                                Success = false,
                                ContentModerationFailed = true,
                                ModerationReason = moderationMessage,
                                ErrorMessage = "Content moderation failed"
                            };
                        }
                    }
                    catch (JsonSerializationException)
                    {
                        // If we can't parse the error response, fall through to generic error handling
                    }
                }
                
                var errorContent2 = await response.Content.ReadAsStringAsync();
                Svc.Log.Warning($"Failed to update listing {id}: {response.StatusCode}");
                Svc.Log.Warning($"Error details: {errorContent2}");
                
                return new ListingResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to update listing: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error updating listing {id}: {ex.Message}");
                return new ListingResult
                {
                    Success = false,
                    ErrorMessage = $"Error updating listing: {ex.Message}"
                };
            }
        }) ?? new ListingResult
        {
            Success = false,
            ErrorMessage = "Request was throttled. Please try again later."
        };
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
    /// Join a party listing.
    /// </summary>
public async Task<JoinResult?> JoinListingAsync(string id)
    {
        return await _debounceService.RunIfAllowedAsync(ApiOperationType.QuickAction, async () => 
        {
            try
            {
                var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/join/", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<JoinResult>(json);
                    return result;
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<JoinResult>(json);
                    if (result != null) {
                        return result;
                    }
                }
                
                Svc.Log.Warning($"Failed to join listing {id}: {response.StatusCode}");
                return new JoinResult { Success = false, Message = $"Failed to join party. Status code: {response.StatusCode}" };
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error joining listing {id}: {ex.Message}");
                return new JoinResult { Success = false, Message = "An unexpected error occurred." };
            }
        });
    }
    
    /// <summary>
    /// Join a party listing with a specified job.
    /// </summary>
    public async Task<JoinResult?> JoinListingWithJobAsync(string id, string job)
    {
        try
        {
            var requestData = new { job = job };
            var json = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/join/", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<JoinResult>(responseJson);
                return result;
            }
            
            // Handle both Conflict (409) and BadRequest (400) - both can contain detailed error info
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict || 
                response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<JoinResult>(responseJson);
                if (result != null) {
                    return result;
                }
            }

            Svc.Log.Warning($"Failed to join listing {id} with job {job}: {response.StatusCode}");
            return new JoinResult { Success = false, Message = $"Failed to join party. Status code: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error joining listing {id} with job {job}: {ex.Message}");
            return new JoinResult { Success = false, Message = "An unexpected error occurred." };
        }
    }
    
    /// <summary>
    /// Leave a party listing.
    /// </summary>
    public async Task<JoinResult?> LeaveListingAsync(string id)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{id}/leave/", null);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<JoinResult>(json);
                return result;
            }

            Svc.Log.Warning($"Failed to leave listing {id}: {response.StatusCode}");
            return new JoinResult { Success = false, Message = $"Failed to leave party. Status code: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error leaving listing {id}: {ex.Message}");
            return new JoinResult { Success = false, Message = "An unexpected error occurred." };
        }
    }
    
    /// <summary>
    /// Kick a participant from a party listing.
    /// </summary>
    public async Task<JoinResult?> KickParticipantAsync(string listingId, string userId)
    {
        try
        {
            var requestData = new { user_id = userId };
            var json = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/listings/{listingId}/kick/", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                // The kick endpoint returns {"detail": "Kicked.", "listing": {...}}
                // We'll adapt this to JoinResult format for consistency
                var kickResponse = JsonConvert.DeserializeObject<dynamic>(responseJson);
                
                var result = new JoinResult
                {
                    Success = true,
                    Message = kickResponse?.detail?.ToString() ?? "Participant kicked successfully."
                };
                
                // Extract listing data if available
                if (kickResponse?.listing != null)
                {
                    var listing = kickResponse.listing;
                    result.CurrentSize = listing.current_size ?? 0;
                    result.MaxSize = listing.max_size ?? 8;
                    result.PartyFull = listing.party_full ?? false;
                    result.PfCode = listing.pf_code?.ToString();
                }
                
                return result;
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                var errorResponse = JsonConvert.DeserializeObject<dynamic>(errorJson);
                return new JoinResult 
                { 
                    Success = false, 
                    Message = errorResponse?.detail?.ToString() ?? "You don't have permission to kick participants from this party." 
                };
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                var errorResponse = JsonConvert.DeserializeObject<dynamic>(errorJson);
                return new JoinResult 
                { 
                    Success = false, 
                    Message = errorResponse?.detail?.ToString() ?? "User not found or not in this party." 
                };
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                var errorResponse = JsonConvert.DeserializeObject<dynamic>(errorJson);
                return new JoinResult 
                { 
                    Success = false, 
                    Message = errorResponse?.detail?.ToString() ?? "Invalid request parameters." 
                };
            }

            Svc.Log.Warning($"Failed to kick participant from listing {listingId}: {response.StatusCode}");
            return new JoinResult { Success = false, Message = $"Failed to kick participant. Status code: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error kicking participant from listing {listingId}: {ex.Message}");
            return new JoinResult { Success = false, Message = "An unexpected error occurred." };
        }
    }
    
    /// <summary>
    /// Send an invitation request to join a party listing.
    /// </summary>
    public async Task<InvitationResponse?> SendInvitationAsync(string listingId, string message, string characterName, string characterWorld)
    {
        try
        {
            var requestData = new
            {
                listing_id = listingId,
                message = message,
                character_name = characterName,
                character_world = characterWorld
            };
            
            var json = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}/api/v1/invitations/send/", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<InvitationResponse>(responseJson);
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            Svc.Log.Warning($"Failed to send invitation: {response.StatusCode} - {errorContent}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error sending invitation: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Get pending invitation notifications for party creators.
    /// Note: This method intentionally bypasses debouncing since it's used by background notification workers
    /// </summary>
    public async Task<NotificationsResponse?> GetNotificationsAsync(long? since = null, string? listingId = null)
    {
        try
        {
            var url = $"{Constants.ApiBaseUrl}/api/v1/invitations/notifications/";
            var queryParams = new List<string>();
            
            if (since.HasValue)
                queryParams.Add($"since={since.Value}");
            
            if (!string.IsNullOrEmpty(listingId))
                queryParams.Add($"listing_id={listingId}");
            
            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);
            
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<NotificationsResponse>(json);
            }
            
            Svc.Log.Warning($"Failed to get notifications: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting notifications: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Dismiss an invitation.
    /// </summary>
    public async Task<bool> DismissInvitationAsync(string invitationId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{Constants.ApiBaseUrl}/api/v1/invitations/{invitationId}/dismiss/");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error dismissing invitation {invitationId}: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Get the current count of online users
    /// </summary>
    public async Task<int?> GetOnlineUserCountAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}/api/core/online-users/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (result != null && result.TryGetValue("online_users", out var onlineUsers))
                {
                    return onlineUsers;
                }
            }
            
            Svc.Log.Warning($"Failed to get online user count: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting online user count: {ex.Message}");
            return null;
        }
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
    /// Gets a list of progression points with completion status for a duty.
    /// </summary>
    public async Task<List<ProgPointStatus>?> GetProgPointsStatusAsync(uint dutyId)
    {
        var cacheKey = $"duty-{dutyId}-points-status";
        if (_cache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is List<ProgPointStatus> cachedResult)
        {
            return cachedResult;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/points/");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<DutyPointsResponse>(json);
                if (result?.Points != null)
                {
                    _cache.TryAdd(cacheKey, result.Points);
                    return result.Points;
                }
            }
            Svc.Log.Warning($"Failed to get progression points status for {dutyId}: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting progression points status for {dutyId}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Gets a list of completed progression point action IDs for a duty.
    /// </summary>
    public async Task<List<uint>?> GetCompletedProgPointsAsync(uint dutyId)
    {
        var progPointsStatus = await GetProgPointsStatusAsync(dutyId);
        return progPointsStatus?.Where(p => p.Completed).Select(p => p.ActionId).ToList();
    }

    /// <summary>
    /// Marks a duty as completed on the server.
    /// </summary>
    public async Task<bool> MarkDutyCompletedAsync(uint dutyId)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            Svc.Log.Debug($"[DutyCompletion] Marking duty {dutyId} as completed");
            
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duty/{dutyId}/complete/", null);
            
            stopwatch.Stop();
            
            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                // Clear related cache entries
                var cacheKey = $"duty-{dutyId}-completed";
                _cache.TryRemove(cacheKey, out _);
                
                Svc.Log.Info($"[DutyCompletion] Successfully marked duty {dutyId} as completed (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
                return true;
            }
            
            Svc.Log.Warning($"[DutyCompletion] Failed to mark duty {dutyId} as completed: {response.StatusCode} (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
            return false;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Svc.Log.Error($"[DutyCompletion] Exception marking duty {dutyId} as completed: {ex.Message} (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
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
                var progPointsCacheKey = $"prog-points-{dutyId}";
                _cache.TryRemove(completedCacheKey, out _);
                _cache.TryRemove(pointsCacheKey, out _);
                _cache.TryRemove(progPointsCacheKey, out _);
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

    /// <summary>
    /// Gets progression points for a specific duty with caching per duty.
    /// Results are cached for 5 minutes to reduce server load.
    /// </summary>
    /// <param name="cfcId">The duty/CFC ID to get progression points for</param>
    /// <returns>List of progression point data or null if request fails</returns>
    public async Task<List<Dictionary<string, object>>?> GetProgPointsAsync(uint cfcId)
    {
        var cacheKey = $"prog-points-{cfcId}";
        
        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is List<Dictionary<string, object>> cachedResult)
        {
            return cachedResult;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/prog-points/{cfcId}/");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                
                if (result != null)
                {
                    // Cache the result for 5 minutes
                    _cache.TryAdd(cacheKey, result);
                    
                    // Set up cache expiration (remove after _cacheDuration)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(_cacheDuration);
                        _cache.TryRemove(cacheKey, out _);
                    });
                    
                    return result;
                }
            }
            
            Svc.Log.Warning($"Failed to get progression points for duty {cfcId}: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting progression points for duty {cfcId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Marks multiple duties as completed in a single bulk operation.
    /// </summary>
    /// <param name="dutyIds">Collection of duty IDs to mark as completed</param>
    /// <returns>True if the bulk operation succeeded, false otherwise</returns>
    public async Task<bool> MarkDutiesCompletedBulkAsync(IEnumerable<uint> dutyIds)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Return false immediately if list is empty
            var dutyIdsList = dutyIds?.ToList();
            if (dutyIdsList == null || dutyIdsList.Count == 0)
            {
                return false;
            }

            Svc.Log.Info($"[BulkDutyCompletion] Starting bulk operation for {dutyIdsList.Count} duties");

            // Serialize body: { "duty_ids": dutyIds }
            var requestBody = new { duty_ids = dutyIdsList };
            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // POST to Constants.ApiBaseUrl + Constants.ProgressBase + "/duties/complete/"
            var response = await _httpClient.PostAsync($"{Constants.ApiBaseUrl}{Constants.ProgressBase}/duties/complete/", content);
            
            stopwatch.Stop();
            
            // Success == HttpStatusCode.Created
            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                // On success, invalidate _cache keys: duty-{id}-completed, duty-{id}-points-status for each id
                foreach (var dutyId in dutyIdsList)
                {
                    var completedCacheKey = $"duty-{dutyId}-completed";
                    var pointsStatusCacheKey = $"duty-{dutyId}-points-status";
                    _cache.TryRemove(completedCacheKey, out _);
                    _cache.TryRemove(pointsStatusCacheKey, out _);
                }
                
                // Log created/existing counts (parse JSON response)
                try
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);
                    
                    if (responseData != null)
                    {
                        var createdCount = responseData.TryGetValue("created", out var created) ? created : 0;
                        var existingCount = responseData.TryGetValue("existing", out var existing) ? existing : 0;
                        
                        Svc.Log.Info($"[BulkDutyCompletion] Success: {createdCount} created, {existingCount} existing (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
                    }
                }
                catch (Exception parseEx)
                {
                    Svc.Log.Warning($"[BulkDutyCompletion] Failed to parse bulk completion response: {parseEx.Message}");
                }
                
                return true;
            }
            
            // Handle 207 Multi-Status (partial failure)
            if (response.StatusCode == (System.Net.HttpStatusCode)207)
            {
                Svc.Log.Warning($"[BulkDutyCompletion] Partial failure (207 Multi-Status) (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
                
                try
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);
                    
                    if (responseData != null)
                    {
                        var createdCount = responseData.TryGetValue("created", out var created) ? created : 0;
                        var existingCount = responseData.TryGetValue("existing", out var existing) ? existing : 0;
                        var message = responseData.TryGetValue("message", out var msg) ? msg.ToString() : "Partial success";
                        
                        Svc.Log.Info($"[BulkDutyCompletion] Partial success: {createdCount} created, {existingCount} existing - {message}");
                        
                        // Handle invalid IDs - retry individually or warn user
                        if (responseData.TryGetValue("invalid_ids", out var invalidIdsObj) && invalidIdsObj != null)
                        {
                            try
                            {
                                var invalidIds = JsonConvert.DeserializeObject<List<uint>>(invalidIdsObj.ToString());
                                if (invalidIds != null && invalidIds.Count > 0)
                                {
                                    Svc.Log.Warning($"[BulkDutyCompletion] Found {invalidIds.Count} invalid duty IDs: [{string.Join(", ", invalidIds)}]");
                                    
                                    // Strategy: Warn user about invalid IDs instead of retrying since they're invalid
                                    // In a real application, you might want to filter these out beforehand
                                    await HandleInvalidDutyIdsAsync(invalidIds);
                                }
                            }
                            catch (Exception invalidEx)
                            {
                                Svc.Log.Error($"[BulkDutyCompletion] Failed to parse invalid_ids: {invalidEx.Message}");
                            }
                        }
                        
                        // Clear cache for successfully processed IDs
                        var validIds = dutyIdsList.Where(id => !responseData.ContainsKey("invalid_ids") || 
                            !JsonConvert.DeserializeObject<List<uint>>(responseData["invalid_ids"].ToString()).Contains(id)).ToList();
                        
                        foreach (var dutyId in validIds)
                        {
                            var completedCacheKey = $"duty-{dutyId}-completed";
                            var pointsStatusCacheKey = $"duty-{dutyId}-points-status";
                            _cache.TryRemove(completedCacheKey, out _);
                            _cache.TryRemove(pointsStatusCacheKey, out _);
                        }
                    }
                    
                    return true; // Partial success is still considered success
                }
                catch (Exception parseEx)
                {
                    Svc.Log.Error($"[BulkDutyCompletion] Failed to parse 207 response: {parseEx.Message}");
                    return false;
                }
            }
            
            Svc.Log.Warning($"[BulkDutyCompletion] Failed with status: {response.StatusCode} (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
            return false;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Svc.Log.Error($"[BulkDutyCompletion] Exception occurred: {ex.Message} (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
            return false;
        }
    }
    
    /// <summary>
    /// Handles invalid duty IDs by logging warnings or potentially implementing retry logic.
    /// </summary>
    /// <param name="invalidIds">List of invalid duty IDs</param>
    private async Task HandleInvalidDutyIdsAsync(List<uint> invalidIds)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            Svc.Log.Warning($"[InvalidDutyHandler] Processing {invalidIds.Count} invalid duty IDs");
            
            // Strategy 1: Log and warn user about invalid IDs
            foreach (var invalidId in invalidIds)
            {
                Svc.Log.Warning($"[InvalidDutyHandler] Invalid duty ID detected: {invalidId} (non-positive integer)");
            }
            
            // Strategy 2: In the future, we could try to validate and retry with corrected IDs
            // For now, we just log the issue since invalid IDs are likely data corruption or client bugs
            
            stopwatch.Stop();
            Svc.Log.Info($"[InvalidDutyHandler] Completed processing invalid IDs (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Svc.Log.Error($"[InvalidDutyHandler] Exception occurred: {ex.Message} (elapsed: {stopwatch.ElapsedMilliseconds}ms)");
        }
    }

}
}
