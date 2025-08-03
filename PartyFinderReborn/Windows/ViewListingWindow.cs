
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ImGuiNET;
using PartyFinderReborn.Models;
using PartyFinderReborn.Services;
using PartyFinderReborn.Utils;
using static ECommons.ImGuiMethods.ImGuiEx;

namespace PartyFinderReborn.Windows
{
    public class ViewListingWindow : BaseListingWindow
    {
        private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        
        // Failed requirements popup state
        private bool _showFailedRequirementsPopup = false;
        private JoinResult? _failedJoinResult = null;

        public ViewListingWindow(Plugin plugin, PartyListing listing) 
            : base(plugin, listing, $"{plugin.ContentFinderService.GetDutyDisplayName(listing.CfcId)}##view_{listing.Id}")
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(450, 550),
                MaximumSize = new Vector2(800, 1200)
            };
            
            Flags = WindowFlags;

            // Fetch latest data on open to ensure view is up-to-date
            _ = RefreshListingAsync();
            
            // Load prog points for this duty to get friendly names and initialize progress tracking
            if (listing.CfcId > 0)
            {
                _ = Task.Run(async () => 
                {
                    try
                    {
                        
                        // Load allowed prog points first to get friendly names
                        await DutyProgressService.LoadAndCacheAllowedProgPointsAsync(listing.CfcId);
                        
                        // Then load the user's completed prog points for this duty to populate the cache
                        var completedPoints = await DutyProgressService.GetCompletedProgPointsAsync(listing.CfcId);
                        
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Error($"Failed to load progression data for duty {listing.CfcId}: {ex.Message}");
                    }
                });
            }
        }

public override void Draw()
    {
        DrawListingCard();
        DrawActionButtonsFooter();

        // Draw job selection popup from base class
        DrawJobSelectionPopup();
        
        // Draw failed requirements popup
        DrawFailedRequirementsPopup();

        // Draw loading spinner overlay if any async operation is in progress
        if (IsRefreshing || IsJoining || IsLeaving)
        {
            LoadingHelper.DrawLoadingSpinner();
        }
    }

    private async void JoinPartyWithJob(string job)
    {
        _isJoining = true;
        try
        {
            // Validate required plugins client-side before attempting to join
            if (!ValidateRequiredPlugins(Listing.RequiredPlugins))
            {
                // Validation failed, error message already shown in ValidateRequiredPlugins
                return;
            }
            
            var joinResult = await Plugin.ApiService.JoinListingWithJobAsync(Listing.Id, job);
            if (joinResult != null && joinResult.Success)
            {
                Svc.Chat.Print($"[Party Finder Reborn] {joinResult.Message}");
                await RefreshListingAsync();
            }
            else if (joinResult != null && joinResult.PfCode == "requirements_not_met" && joinResult.FailedRequirements.Any())
            {
                // Show failed requirements popup instead of just chat message
                _failedJoinResult = joinResult;
                _showFailedRequirementsPopup = true;
            }
            else
            {
                Svc.Chat.PrintError($"[Party Finder Reborn] Failed to join party: {joinResult?.Message ?? "Unknown error"}");
            }
        }
        catch (Exception ex)
        {
            Svc.Chat.PrintError($"[Party Finder Reborn] Error joining party: {ex.Message}");
        }
        finally
        {
            _isJoining = false;
        }
        }

        /// <summary>
        /// Kick a participant from the party
        /// </summary>
        private async Task KickParticipantAsync(string participantDiscordId)
        {
            try
            {
                // Use the participant's Discord ID for the kick request
                var kickResult = await Plugin.ApiService.KickParticipantAsync(Listing.Id, participantDiscordId);
                
                if (kickResult != null && kickResult.Success)
                {
                    Svc.Chat.Print($"[Party Finder Reborn] {kickResult.Message}");
                    await RefreshListingAsync();
                }
                else
                {
                    Svc.Chat.PrintError($"[Party Finder Reborn] Failed to kick participant: {kickResult?.Message ?? "Unknown error"}");
                }
            }
            catch (Exception ex)
            {
                Svc.Chat.PrintError($"[Party Finder Reborn] Error kicking participant: {ex.Message}");
            }
        }

        private void DrawListingCard()
        {
            var dutyName = ContentFinderService.GetDutyDisplayName(Listing.CfcId);

            // Header Section
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 8));
            ImGui.BeginGroup();
            {
                // Use default font for now - game font API may not be available
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
                ImGui.Text(dutyName);
                ImGui.PopStyleVar();
                
                var creatorText = !string.IsNullOrEmpty(Listing.CreatorJob) 
                    ? $"Created by {Listing.CreatorDisplay} ({Listing.CreatorJob}) in {Listing.LocationDisplay}"
                    : $"Created by {Listing.CreatorDisplay} in {Listing.LocationDisplay}";
                ImGui.TextColored(ImGuiColors.DalamudGrey, creatorText);
            }
            ImGui.EndGroup();
            ImGui.PopStyleVar();

            ImGui.SameLine(ImGui.GetWindowWidth() - 100);
            DrawStatusBadge();
            
            ImGui.Separator();
            ImGui.Spacing();

            // Main content area
            ImGui.BeginChild("MainContent", new Vector2(0, ImGui.GetContentRegionAvail().Y - 50), false);
            {
                // Description
                if (!string.IsNullOrWhiteSpace(Listing.Description))
                {
                    DrawSection("Description", () => {
                        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                        ImGui.TextWrapped(Listing.Description);
                        ImGui.PopTextWrapPos();
                    });
                }
                
                // Roster
                DrawSection("Party Roster", DrawRosterTable, ImGuiTreeNodeFlags.DefaultOpen);

                // Requirements
                DrawSection("Requirements", DrawRequirements);
                
                // Tags & Strategies
                DrawSection("Tags & Strategies", DrawTagsAndStrategies);

            }
            ImGui.EndChild();
        }
        
        private void DrawStatusBadge()
        {
            var (text, color) = GetStatusInfo();
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);
            ImGui.Button(text, new Vector2(80, 24));
            ImGui.PopStyleColor(3);
        }

        private (string, Vector4) GetStatusInfo()
        {
            return Listing.Status.ToLower() switch
            {
"active" => ("Active", new Vector4(0.2f, 0.6f, 0.2f, 1.0f)),
                "full" => ("Full", Red),
                "draft" => ("Draft", ImGuiColors.DalamudGrey),
                "completed" => ("Completed", ImGuiColors.ParsedGreen),
                "cancelled" => ("Cancelled", ImGuiColors.DalamudRed),
                _ => (Listing.StatusDisplay, ImGuiColors.DalamudGrey)
            };
        }

        private void DrawRosterTable()
        {
            var localPlayerName = Svc.ClientState.LocalPlayer?.Name.TextValue ?? string.Empty;

            if (ImGui.BeginTable("rosterTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableHeadersRow();
                
                for (var i = 0; i < Listing.MaxSize; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    
                    if (i < Listing.Participants.Count)
                    {
                        var participant = Listing.Participants[i];
                        var isLocalPlayer = !string.IsNullOrEmpty(localPlayerName) && participant.Name.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase);
                        
                        // Check if this participant has a pending invitation
                        var pendingInvitation = Listing.IsOwner ? Plugin.GetPendingInvitationForParticipant(Listing.Id, participant.DiscordId) : null;
                        var hasPendingInvite = pendingInvitation != null;

                        // Display participant job name (improved from generic role)
                        ImGui.Text(!string.IsNullOrEmpty(participant.Job) ? participant.Job : "Any Job");

                        ImGui.TableNextColumn();
                        
                        // Display participant name with special effects for pending invites
                        // Wrap name text with Selectable to enable context menu only on name
                        if (hasPendingInvite)
                        {
                            // Rainbow effect for participants with pending invitations
                            var time = (float)ImGui.GetTime();
                            var hue = (time * 0.5f) % 1.0f; // Cycle through hues
                            ImGui.ColorConvertHSVtoRGB(hue, 0.8f, 1.0f, out var r, out var g, out var b);
                            var rainbowVec4 = new Vector4(r, g, b, 1.0f);
                            
                            ImGui.PushStyleColor(ImGuiCol.Text, rainbowVec4);
                            if (Listing.IsOwner && ImGui.Selectable(participant.Name, false, ImGuiSelectableFlags.None))
                            {
                                // Selectable clicked, but we don't need to do anything special
                            }
                            ImGui.PopStyleColor();
                        }
                        else if (isLocalPlayer)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, Yellow);
                            if (Listing.IsOwner && ImGui.Selectable($"{participant.Name} (You)", false, ImGuiSelectableFlags.None))
                            {
                                // Selectable clicked, but we don't need to do anything special
                            }
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            if (Listing.IsOwner && ImGui.Selectable(participant.Name, false, ImGuiSelectableFlags.None))
                            {
                                // Selectable clicked, but we don't need to do anything special
                            }
                            else if (!Listing.IsOwner)
                            {
                                ImGui.Text(participant.Name);
                            }
                        }
                        
                        // Context menu attached to the name (only if this user is the owner)
                        if (Listing.IsOwner && ImGui.BeginPopupContextItem($"participant_context_{i}"))
                        {
                            // Only show kick option for other players (not the creator themselves)
                            if (!isLocalPlayer)
                            {
                                if (ImGui.MenuItem("Kick from Party"))
                                {
                                    _ = KickParticipantAsync(participant.DiscordId);
                                }
                            }
                            ImGui.EndPopup();
                        }
                        
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Green, "Filled");
                        
                        ImGui.TableNextColumn();
                        // Show invite button for participants with pending invitations
                        if (hasPendingInvite && Listing.IsOwner && pendingInvitation != null)
                        {
                            if (ImGui.Button($"Invite##{i}", new Vector2(70, 0)))
                            {
                                // Use public wrapper method to invite player
                                Plugin.InvitePlayerToPartyFromUI(pendingInvitation.CharacterName, pendingInvitation.CharacterWorld, pendingInvitation.Id);
                            }
                        }
                        else
                        {
                            // Empty space for alignment
                            ImGui.Text("");
                        }
                    }
                    else
                    {
                        // Improved display for open slots - show "Any Job" instead of generic "-"
                        ImGui.TextDisabled("Any Job");
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Open Slot");
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Open");
                        ImGui.TableNextColumn();
                        ImGui.Text(""); // Empty action column
                    }
                }
                ImGui.EndTable();
            }
            
            // Party size progress bar
            ImGui.Spacing();
            DrawProgressBar((float)Listing.CurrentSize / Listing.MaxSize, $"Party Size: {Listing.CurrentSize} / {Listing.MaxSize}");
        }
        
        private void DrawRequirements()
        {
            CollapsingGroup("Item Level & Experience", () =>
            {
                ImGui.Text($"Experience: {Listing.ExperienceLevelDisplay}");
                if (Listing.MinItemLevel > 0 || Listing.MaxItemLevel > 0)
                {
                    var ilvlText = Listing.MaxItemLevel > 0
                        ? $"Item Level: {Listing.MinItemLevel} - {Listing.MaxItemLevel}"
                        : $"Minimum Item Level: {Listing.MinItemLevel}";
                    ImGui.Text(ilvlText);
                }
            });
            
            if (Listing.RequiredClears.Any())
            {
                CollapsingGroup("Required Clears", () =>
                {
                    DrawRequiredClearsStatus(Listing.RequiredClears);
                });
            }

            if (Listing.ProgPoint.Any())
            {
                CollapsingGroup("Progression Point", () =>
                {
                    var progPoints = Listing.ProgPoint;
                    if (progPoints.Any())
                    {
                        // Use ProgPointDetails if available, otherwise fallback to action names
                        var progPointNames = Listing.ProgPointDetails.Any() 
                            ? Listing.ProgPointDetails.Select(p => p.FriendlyName)
                            : progPoints.Select(id => 
                            {
                                var friendlyName = DutyProgressService.GetProgPointFriendlyName(Listing.CfcId, id);
                                return friendlyName != $"Action #{id}" ? friendlyName : ActionNameService.Get(id);
                            });
                        DrawProgressionStatus(progPoints.ToList());
                    }
                });
            }
            
            // Dedicated Required Plugins section
            if (Listing.RequiredPlugins.Any())
            {
                CollapsingGroup("Required Plugins", () =>
                {
                    DrawRequiredPlugins();
                });
            }
            
            CollapsingGroup("Other Requirements", () =>
            {
                ImGui.Text($"Loot Rules: {Listing.LootRulesDisplay}");
                if (Listing.ParseRequirement != "none")
                {
                    ImGui.Text($"Parse: {Listing.ParseRequirementDisplay}");
                }
                if (Listing.VoiceChatRequired)
                {
                    ImGui.Text("Voice Chat: Required");
                }
            });
        }

        private void DrawTagsAndStrategies()
        {
            if (Listing.UserTags.Any())
            {
                CollapsingGroup("Tags", () =>
                {
                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                    ImGui.TextWrapped(string.Join(", ", Listing.UserTags));
                    ImGui.PopTextWrapPos();
                });
            }

            if (Listing.UserStrategies.Any())
            {
                CollapsingGroup("Strategies", () =>
                {
                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                    ImGui.TextWrapped(string.Join(", ", Listing.UserStrategies));
                    ImGui.PopTextWrapPos();
                });
            }
        }

        private void DrawActionButtonsFooter()
        {
            ImGui.Separator();
            ImGui.BeginChild("Footer", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar);
            {
                // Always visible buttons
                double refreshWait = 0;
                var refreshDisabled = IsRefreshing || !Plugin.DebounceService.CanExecute(ApiOperationType.Read, out refreshWait);
                if (refreshDisabled && !IsRefreshing)
                {
                    refreshWait = Plugin.DebounceService.SecondsRemaining(ApiOperationType.Read);
                }

                ImGui.BeginDisabled(refreshDisabled);
                if (ImGui.Button(IsRefreshing ? "Refreshing..." : "Refresh", new Vector2(80, 0)))
                {
                    _ = RefreshListingAsync();
                }
                ImGui.EndDisabled();

                if (refreshDisabled && !IsRefreshing && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Please wait {refreshWait:F1}s");
                }

                ImGui.SameLine();
                if (ImGui.Button("Close", new Vector2(60, 0)))
                {
                    IsOpen = false;
                }

                // Owner-specific buttons
                if (Listing.IsOwner)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Edit", new Vector2(60, 0)))
                    {
                        var editWindow = new CreateEditListingWindow(Plugin, Listing, false);
                        Plugin.WindowSystem.AddWindow(editWindow);
                        editWindow.IsOpen = true;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Close Listing", new Vector2(100, 0)))
                    {
                        _ = CloseListingAsync();
                    }
                }
                else
                {
                    var userDatacenter = Plugin.WorldService.GetCurrentPlayerCurrentDataCenter();
                    var creatorDatacenter = Listing.Datacenter;
                    var inSameDatacenter = string.Equals(userDatacenter, creatorDatacenter, StringComparison.OrdinalIgnoreCase);

                    var isInParty = false;
                    Svc.Framework.RunOnFrameworkThread(() =>
                    {
                        isInParty = Svc.Party.Length > 1;
                    });

                    if (Listing.HasJoined)
                    {
                        ImGui.SameLine();
                        var canJoinInGame = inSameDatacenter && !isInParty;
                        
                        ImGui.BeginDisabled(!canJoinInGame);
                        if (ImGui.Button("Join In-Game Party", new Vector2(150, 0)) && canJoinInGame)
                        {
                            SendInGamePartyJoinRequest();
                        }
                        ImGui.EndDisabled();

                        ImGui.SameLine();
                        if (ImGui.Button("Leave Listing", new Vector2(100, 0)))
                        {
                            _ = LeavePartyAsync();
                        }
                        
                        // Display any warnings below the buttons for joined users
                        if (!inSameDatacenter)
                        {
                            ImGui.TextColored(ImGuiColors.DalamudRed, "Must be in the same datacenter to join in-game party");
                        }
                        else if (isInParty)
                        {
                            ImGui.TextColored(ImGuiColors.DalamudRed, "Must leave current party to join in-game party");
                        }
                    }
                    else
                    {
                        ImGui.SameLine();
                        if (ImGui.Button("Join Listing", new Vector2(120, 0)))
                        {
                            ShowJobSelectionPopup(JoinPartyWithJob);
                        }
                    }
                }
            }
            ImGui.EndChild();
        }
        
        // Helper to draw a styled section
        private void DrawSection(string title, Action content, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 6));
            
            if (ImGui.CollapsingHeader(title, flags))
            {
                ImGui.Indent();
                ImGui.Spacing();
                content();
                ImGui.Unindent();
                ImGui.Spacing();
            }
            
            ImGui.PopStyleVar();
        }

        // Helper for simpler collapsible groups
        private bool CollapsingGroup(string title, Action content)
        {
            bool visible = ImGui.TreeNodeEx(title, ImGuiTreeNodeFlags.FramePadding);
            if (visible)
            {
                ImGui.Indent();
                content();
                ImGui.Unindent();
                ImGui.TreePop();
            }
            return visible;
        }
        
        private void DrawProgressBar(float fraction, string text)
        {
            ImGui.ProgressBar(fraction, new Vector2(-1, 0), text);
        }
        
        private void DrawRequiredPlugins()
        {
            ImGui.Text("This party requires the following plugins:");
            ImGui.Spacing();
            
            foreach (var pluginName in Listing.RequiredPlugins)
            {
                // Try to find the plugin by friendly name first, then fallback to internal name
                var installedPlugin = Plugin.PluginService.GetInstalled()
                    .FirstOrDefault(p => p.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase) ||
                                       p.InternalName.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
                
                var pluginInfo = installedPlugin != null 
                    ? new RequiredPluginInfo(installedPlugin.InternalName, installedPlugin.Name)
                    : new RequiredPluginInfo(pluginName, pluginName); // Fallback when plugin not found
                
                // Draw bullet point with plugin name
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.Text(pluginName);
                
                // Draw individual availability indicator for this plugin
                ImGuiEx.PluginAvailabilityIndicator(new[] { pluginInfo });
            }
        }
private void SendInGamePartyJoinRequest()
        {
            string? characterName = null;
            string? worldName = null;

            // Fetch character information on framework thread
            Svc.Framework.RunOnFrameworkThread(() =>
            {
                characterName = Svc.ClientState.LocalPlayer?.Name.TextValue;
                worldName = Plugin.WorldService.GetCurrentPlayerHomeWorld();
            });

            if (characterName == null || worldName == null)
            {
                Svc.Chat.PrintError("Failed to get character information for party request.");
                return;
            }

var userDatacenter = Plugin.WorldService.GetCurrentPlayerCurrentDataCenter();
            var creatorDatacenter = Listing.Datacenter;
            var inSameDatacenter = string.Equals(userDatacenter, creatorDatacenter, StringComparison.OrdinalIgnoreCase);

            if (!inSameDatacenter)
            {
                Svc.Chat.PrintError($"You must be on datacenter {creatorDatacenter} to join this party.");
                return;
            }

            // Fire and forget the task
            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await Plugin.ApiService.SendInvitationAsync(
                        Listing.Id,
                        "Request to join in-game party.",
                        characterName,
                        worldName
                    );

                    if (response?.Success ?? false)
                    {
                        Svc.Chat.Print($"Successfully sent join request to the party creator.");
                    }
                    else
                    {
                        Svc.Chat.PrintError($"Failed to send join request: {response?.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Svc.Chat.PrintError($"Error sending join request: {ex.Message}");
                }
            });
        }
        
        private void DrawFailedRequirementsPopup()
        {
            if (!_showFailedRequirementsPopup || _failedJoinResult == null)
                return;

            ImGui.SetNextWindowSize(new Vector2(500, 400), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
            
            if (ImGui.BeginPopupModal("Join Requirements Not Met##ViewListing", ref _showFailedRequirementsPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                // Header with error icon
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextWrapped(_failedJoinResult.Message);
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                // List failed requirements
                ImGui.Text("Missing Requirements:");
                ImGui.Spacing();
                
                foreach (var requirement in _failedJoinResult.FailedRequirements)
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    
                    if (requirement.Type == "duty_completion")
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                        ImGui.PopStyleColor();
                        ImGui.PopFont();
                        ImGui.SameLine();
                        
                        var dutyName = requirement.DutyId.HasValue 
                            ? ContentFinderService.GetDutyDisplayName((uint)requirement.DutyId.Value)
                            : $"Duty #{requirement.DutyId}";
                        ImGui.TextWrapped($"Complete: {dutyName}");
                    }
                    else if (requirement.Type == "progress_point")
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                        ImGui.PopStyleColor();
                        ImGui.PopFont();
                        ImGui.SameLine();
                        
                        var actionName = requirement.ActionId.HasValue 
                            ? ActionNameService.Get((uint)requirement.ActionId.Value)
                            : $"Action #{requirement.ActionId}";
                        var dutyName = requirement.DutyId.HasValue 
                            ? ContentFinderService.GetDutyDisplayName((uint)requirement.DutyId.Value)
                            : $"Duty #{requirement.DutyId}";
                        ImGui.TextWrapped($"See: {actionName} in {dutyName}");
                    }
                    else
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                        ImGui.PopStyleColor();
                        ImGui.PopFont();
                        ImGui.SameLine();
                        ImGui.TextWrapped(requirement.Message);
                    }
                }
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                // Close button
                var buttonSize = new Vector2(80, 0);
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var buttonPos = (availableWidth - buttonSize.X) / 2;
                ImGui.SetCursorPosX(buttonPos);
                
                if (ImGui.Button("OK", buttonSize))
                {
                    _showFailedRequirementsPopup = false;
                    _failedJoinResult = null;
                }
                
                ImGui.EndPopup();
            }
            
            // Open the popup if it should be shown
            if (_showFailedRequirementsPopup && !ImGui.IsPopupOpen("Join Requirements Not Met##ViewListing"))
            {
                ImGui.OpenPopup("Join Requirements Not Met##ViewListing");
            }
        }

    }
}
