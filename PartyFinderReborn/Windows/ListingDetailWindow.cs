using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using PartyFinderReborn.Models;
using System.Linq;
using System.Collections.Generic;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace PartyFinderReborn.Windows;

public class ListingDetailWindow : Window, IDisposable
{
    private PartyListing Listing;
    private Plugin Plugin;
    private bool IsCreateMode;
    private bool IsEditing;
    private bool IsSaving;
    
    // Editable fields
    private string EditName;
    private string EditDescription;
    private string EditStatus;
    private int EditMaxParticipants;
    private DateTime EditEventDate;
    private List<string> EditUserTags;
    private List<string> EditUserStrategies;
    private string NewTag;
    private string NewStrategy;
    private List<PopularItem> PopularTags;

    public ListingDetailWindow(Plugin plugin, PartyListing listing, bool createMode = false) 
        : base(createMode ? $"Create New Party Listing##create_{listing.Id}" : $"Party Listing: {listing.Name}##{listing.Id}")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 500),
            MaximumSize = new Vector2(1000, 800)
        };

        Plugin = plugin;
        Listing = listing;
        IsCreateMode = createMode;
        IsEditing = createMode;
        IsSaving = false;
        
        // Initialize editable fields
        EditName = listing.Name;
        EditDescription = listing.Description;
        EditStatus = listing.Status;
        EditMaxParticipants = listing.MaxParticipants;
        EditEventDate = listing.EventDate ?? DateTime.Now.AddHours(1);
        EditUserTags = new List<string>(listing.UserTags);
        EditUserStrategies = new List<string>(listing.UserStrategies);
        NewTag = "";
        NewStrategy = "";
        PopularTags = new List<PopularItem>();
        
        // Load popular tags
        _ = LoadPopularTagsAsync();
    }

    public void Dispose() 
    {
        // Make sure the window removes itself from the WindowSystem when disposed
        // But only if it's actually registered
        try
        {
            if (Plugin.WindowSystem.Windows.Contains(this))
            {
                Plugin.WindowSystem.RemoveWindow(this);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error removing window from system: {ex.Message}");
        }
    }
    
    private async Task LoadPopularTagsAsync()
    {
        try
        {
            var response = await Plugin.ApiService.GetPopularTagsAsync();
            if (response != null)
            {
                PopularTags = response.Results.Take(10).ToList(); // Limit to top 10 for the detail window
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load popular tags: {ex.Message}");
        }
    }

    public override void Draw()
    {
        if (IsEditing)
        {
            DrawEditForm();
        }
        else
        {
            DrawViewMode();
        }
        
        ImGui.Separator();
        DrawActionButtons();
    }
    
    private void DrawViewMode()
    {
        // Header with basic information
        ImGui.Text($"Party Name: {Listing.Name}");
        ImGui.Text($"Status: {Listing.StatusDisplay}");
        ImGui.Text($"Participants: {Listing.ParticipantsDisplay}");
        
        if (Listing.EventDate.HasValue)
        {
            ImGui.Text($"Event Date: {Listing.EventDateDisplay}");
        }

        ImGui.Separator();

        // Description
        if (!string.IsNullOrEmpty(Listing.Description))
        {
            ImGui.Text("Description:");
            ImGui.TextWrapped(Listing.Description);
            ImGui.Separator();
        }

        // Venue Information
        if (Listing.Venue != null)
        {
            ImGui.Text("Venue Information:");
            ImGui.Text($"Name: {Listing.Venue.Name}");
            ImGui.Text($"Location: {Listing.VenueDisplay}");
            if (!string.IsNullOrEmpty(Listing.Venue.Address))
            {
                ImGui.Text($"Address: {Listing.Venue.Address}");
            }
            if (Listing.Venue.Capacity.HasValue)
            {
                ImGui.Text($"Venue Capacity: {Listing.Venue.Capacity}");
            }
            ImGui.Separator();
        }

        // Tags and Strategies
        if (Listing.UserTags.Count > 0)
        {
            ImGui.Text("Tags:");
            ImGui.TextWrapped(Listing.TagsDisplay);
        }

        if (Listing.UserStrategies.Count > 0)
        {
            ImGui.Text("Strategies:");
            ImGui.TextWrapped(Listing.StrategiesDisplay);
        }

        if (Listing.UserTags.Count > 0 || Listing.UserStrategies.Count > 0)
        {
            ImGui.Separator();
        }

        // Creator Information
        if (Listing.Creator != null)
        {
            ImGui.Text("Created by:");
            ImGui.Text($"Name: {Listing.CreatorDisplay}");
            ImGui.Text($"Created: {Listing.CreatedAt:yyyy-MM-dd HH:mm}");
            ImGui.Text($"Updated: {Listing.UpdatedAt:yyyy-MM-dd HH:mm}");
        }
    }
    
    private void DrawEditForm()
    {
        ImGui.Text(IsCreateMode ? "Create New Party Listing" : "Edit Party Listing");
        ImGui.Separator();
        
        // Name
        ImGui.Text("Party Name *");
        ImGui.InputText("##name", ref EditName, 100);
        
        // Description  
        ImGui.Text("Description");
        ImGui.InputTextMultiline("##description", ref EditDescription, 1000, new Vector2(-1, 100));
        
        // Max Participants
        ImGui.Text("Max Participants");
        ImGui.SliderInt("##maxparticipants", ref EditMaxParticipants, 2, 24);
        
        // Status (only for existing listings)
        if (!IsCreateMode)
        {
            ImGui.Text("Status");
            var statusItems = PartyStatuses.All.Values.ToArray();
            var currentStatusIndex = Array.IndexOf(statusItems, PartyStatuses.All.GetValueOrDefault(EditStatus, EditStatus));
            if (currentStatusIndex == -1) currentStatusIndex = 0;
            
            if (ImGui.Combo("##status", ref currentStatusIndex, statusItems, statusItems.Length))
            {
                EditStatus = PartyStatuses.All.FirstOrDefault(kvp => kvp.Value == statusItems[currentStatusIndex]).Key;
            }
        }
        
        // Event Date
        ImGui.Text("Event Date");
        var eventDateStr = EditEventDate.ToString("yyyy-MM-dd HH:mm");
        if (ImGui.InputText("##eventdate", ref eventDateStr, 20))
        {
            if (DateTime.TryParse(eventDateStr, out var newDate))
            {
                EditEventDate = newDate;
            }
        }
        
        ImGui.Separator();
        
        // Tags
        ImGui.Text("Tags");
        for (int i = EditUserTags.Count - 1; i >= 0; i--)
        {
            ImGui.Text($"• {EditUserTags[i]}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##{i}"))
            {
                EditUserTags.RemoveAt(i);
            }
        }
        
        ImGui.Text("Popular Tags");
        if (ImGui.BeginListBox("##popularlisttags", new Vector2(0, 100)))
        {
            foreach (var tag in PopularTags.Select(t => t.Name))
            {
                if (ImGui.Selectable(tag, tag == NewTag))
                {
                    NewTag = tag;
                }
            }
            ImGui.EndListBox();
        }

        ImGui.InputText("Add Tag", ref NewTag, 50);
        ImGui.SameLine();
        if (ImGui.Button("Add Tag") && !string.IsNullOrWhiteSpace(NewTag) && !EditUserTags.Contains(NewTag))
        {
            EditUserTags.Add(NewTag.Trim());
            NewTag = "";
        }
        
        ImGui.Separator();
        
        // Strategies
        ImGui.Text("Strategies");
        for (int i = EditUserStrategies.Count - 1; i >= 0; i--)
        {
            ImGui.Text($"• {EditUserStrategies[i]}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##strategy{i}"))
            {
                EditUserStrategies.RemoveAt(i);
            }
        }
        
        ImGui.Text("Popular Strategies/Tags");
        if (ImGui.BeginListBox("##popularstrategies", new Vector2(0, 80)))
        {
            foreach (var tag in PopularTags.Select(t => t.Name))
            {
                if (ImGui.Selectable(tag, tag == NewStrategy))
                {
                    NewStrategy = tag;
                }
            }
            ImGui.EndListBox();
        }
        
        ImGui.InputText("Add Strategy", ref NewStrategy, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add Strategy") && !string.IsNullOrWhiteSpace(NewStrategy) && !EditUserStrategies.Contains(NewStrategy))
        {
            EditUserStrategies.Add(NewStrategy.Trim());
            NewStrategy = "";
        }
    }
    
    private void DrawActionButtons()
    {
        if (IsEditing)
        {
            // Save/Cancel buttons for edit mode
            if (IsSaving)
            {
                ImGui.Button("Saving...");
            }
            else if (ImGui.Button(IsCreateMode ? "Create Listing" : "Save Changes"))
            {
                _ = SaveListingAsync();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                if (IsCreateMode)
                {
                    IsOpen = false;
                }
                else
                {
                    IsEditing = false;
                    // Reset fields
                    EditName = Listing.Name;
                    EditDescription = Listing.Description;
                    EditStatus = Listing.Status;
                    EditMaxParticipants = Listing.MaxParticipants;
                    EditEventDate = Listing.EventDate ?? DateTime.Now.AddHours(1);
                    EditUserTags = new List<string>(Listing.UserTags);
                    EditUserStrategies = new List<string>(Listing.UserStrategies);
                }
            }
        }
        else
        {
            // View mode buttons
            if (!IsCreateMode && ImGui.Button("Join Party") && !Listing.IsFull && Listing.IsActive)
            {
                // Handle join party action
                // Plugin.JoinParty(Listing.Id);
            }
            
            if (!IsCreateMode)
            {
                ImGui.SameLine();
                if (ImGui.Button("Edit"))
                {
                    IsEditing = true;
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Refresh") && !IsCreateMode)
            {
                // Handle refresh action
                // Plugin.RefreshListing(Listing.Id);
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Close"))
            {
                IsOpen = false;
            }
        }
    }
    
    private async Task SaveListingAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            Svc.Log.Warning("Party name is required");
            return;
        }
        
        IsSaving = true;
        
        try
        {
            // Update listing with edited values
            var updatedListing = new PartyListing
            {
                Id = Listing.Id,
                Name = EditName.Trim(),
                Description = EditDescription.Trim(),
                Status = IsCreateMode ? "active" : EditStatus,
                EventDate = EditEventDate,
                MaxParticipants = EditMaxParticipants,
                CurrentParticipants = Listing.CurrentParticipants,
                UserTags = new List<string>(EditUserTags),
                UserStrategies = new List<string>(EditUserStrategies),
                Venue = Listing.Venue,
                Creator = Listing.Creator,
                CreatedAt = Listing.CreatedAt,
                UpdatedAt = DateTime.Now
            };
            
            PartyListing? result;
            if (IsCreateMode)
            {
                result = await Plugin.ApiService.CreateListingAsync(updatedListing);
            }
            else
            {
                result = await Plugin.ApiService.UpdateListingAsync(Listing.Id, updatedListing);
            }
            
            if (result != null)
            {
                // Update the current listing with the server response
                Listing = result;
                WindowName = $"Party Listing: {Listing.Name}##{Listing.Id}";
                IsEditing = false;
                IsCreateMode = false;
                
                Svc.Log.Info($"Successfully {(IsCreateMode ? "created" : "updated")} listing: {Listing.Name}");
            }
            else
            {
                Svc.Log.Error($"Failed to {(IsCreateMode ? "create" : "update")} listing");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error saving listing: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }
}
