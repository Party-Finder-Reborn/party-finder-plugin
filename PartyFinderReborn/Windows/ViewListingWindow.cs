
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
using PartyFinderReborn.Utils;
using static ECommons.ImGuiMethods.ImGuiEx;

namespace PartyFinderReborn.Windows
{
    public class ViewListingWindow : BaseListingWindow
    {
        private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

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
        }

public override void Draw()
    {
        DrawListingCard();
        DrawActionButtonsFooter();

        // Draw role selection popup from base class
        DrawRoleSelectionPopup();

        // Draw loading spinner overlay if any async operation is in progress
        if (IsRefreshing || IsJoining || IsLeaving)
        {
            LoadingHelper.DrawLoadingSpinner();
        }
    }

    private async void JoinPartyWithRole(string role)
    {
        _isJoining = true;
        try
        {
            var joinResult = await Plugin.ApiService.JoinListingWithRoleAsync(Listing.Id, role);
            if (joinResult != null && joinResult.Success)
            {
                Svc.Chat.Print($"[Party Finder Reborn] {joinResult.Message}");
                await RefreshListingAsync();
                IsOpen = false;
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
                
                ImGui.TextColored(ImGuiColors.DalamudGrey, $"Created by {Listing.CreatorDisplay} in {Listing.LocationDisplay}");
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
                "active" => ("Active", Green),
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

            if (ImGui.BeginTable("rosterTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 30);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                var jobRoles = new[] { "Tank", "Healer", "DPS" };
                
                for (var i = 0; i < Listing.MaxSize; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    
                    if (i < Listing.Participants.Count)
                    {
                        var participant = Listing.Participants[i];
                        var isLocalPlayer = !string.IsNullOrEmpty(localPlayerName) && participant.Name.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase);

                        // Display participant role
                        ImGui.Text(participant.Role);

                        ImGui.TableNextColumn();
                        if (isLocalPlayer)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, Yellow);
                            ImGui.Text(participant.Name);
                            ImGui.PopStyleColor();
                            ImGui.SameLine();
                            ImGui.TextColored(ImGuiColors.DalamudGrey, "(You)");
                        }
                        else
                        {
                            ImGui.Text(participant.Name);
                        }
                        
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Green, "Filled");
                    }
                    else
                    {
                        ImGui.Text("-"); // Job icon
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Open Slot");
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled("Open");
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

            if (!string.IsNullOrWhiteSpace(Listing.ProgPoint))
            {
                CollapsingGroup("Progression Point", () =>
                {
                    var progPoints = ParseProgPointFromString(Listing.ProgPoint);
                    if (progPoints.Any())
                    {
                        var progPointNames = progPoints.Select(id => ActionNameService.Get(id));
                        ImGui.TextWrapped($"Required: {string.Join(", ", progPointNames)}");
                        ImGui.Separator();
                        DrawProgressionStatus(progPoints);
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
            var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y;
            ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - footerHeight);
            
            ImGui.BeginChild("Footer", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar);
            {
                // Join/Leave/Close buttons
                if (Listing.IsActive)
                {
                    if (Listing.IsOwner)
                    {
                        if (ImGui.Button("Close Listing", new Vector2(100, 0))) { _ = CloseListingAsync(); }
                    }
                    else if (Listing.HasJoined)
                    {
                        ImGui.BeginDisabled(IsLeaving);
                        if (ImGui.Button(IsLeaving ? "Leaving..." : "Leave Party", new Vector2(100, 0))) { _ = LeavePartyAsync(); }
                        ImGui.EndDisabled();
                        
                        // Add Join In-Game Party button when user has joined the service party
                        ImGui.SameLine();
                        DrawJoinInGamePartyButton();
                    }
                    else if (Listing.CurrentSize < Listing.MaxSize)
                    {
                        ImGui.BeginDisabled(IsJoining);
if (ImGui.Button(IsJoining ? "Joining..." : "Join Party", new Vector2(100, 0))) { ShowRoleSelectionPopup(JoinPartyWithRole); }
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        ImGui.BeginDisabled(true);
                        ImGui.Button("Party Full", new Vector2(100, 0));
                        ImGui.EndDisabled();
                    }
                }
                
// Refresh button
                ImGui.SameLine(ImGui.GetContentRegionMax().X - 160);
                ImGui.BeginDisabled(IsRefreshing);
                if (ImGui.Button(IsRefreshing ? "Refreshing..." : "Refresh", new Vector2(80, 0))) { _ = RefreshListingAsync(); }
                ImGui.EndDisabled();
                
                // Edit button (if owner)
                if (Listing.IsOwner)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Edit", new Vector2(60, 0)))
                    {
                        var editWindow = new CreateEditListingWindow(Plugin, Listing, false);
                        Plugin.WindowSystem.AddWindow(editWindow);
                        editWindow.IsOpen = true;
                        IsOpen = false;
                    }
                }

                // Close button
                ImGui.SameLine(ImGui.GetContentRegionMax().X - 40);
                if (ImGui.Button("Close", new Vector2(60, 0)))
                {
                    IsOpen = false;
                }
            }
            ImGui.EndChild();
        }
        private void DrawJoinInGamePartyButton()
        {
            var userDatacenter = Plugin.WorldService.GetCurrentPlayerHomeDataCenter();
            var creatorDatacenter = Listing.Datacenter;
            var inSameDatacenter = string.Equals(userDatacenter, creatorDatacenter, StringComparison.OrdinalIgnoreCase);

            ImGui.BeginDisabled(!inSameDatacenter);
            if (ImGui.Button("Join In-Game Party", new Vector2(150, 0)) && inSameDatacenter)
            {
                SendInGamePartyJoinRequest();
            }
            ImGui.EndDisabled();

            if (!inSameDatacenter && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("You must be in the same datacenter as the party creator to join the in-game party.");
            }
        }
        
        // Helper to draw a styled section
        private void DrawSection(string title, Action content, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 6));
            ImGui.PushStyleColor(ImGuiCol.Header, ImGuiColors.DalamudGrey3);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ImGuiColors.DalamudGrey2);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, ImGuiColors.DalamudGrey);
            
            if (ImGui.CollapsingHeader(title, flags))
            {
                ImGui.Indent();
                ImGui.Spacing();
                content();
                ImGui.Unindent();
                ImGui.Spacing();
            }
            
            ImGui.PopStyleColor(3);
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
            string characterName = null;
            string worldName = null;

            // Fetch character information on framework thread
            Svc.Framework.RunOnFrameworkThread(() =>
            {
                characterName = Svc.ClientState.LocalPlayer?.Name.TextValue;
                worldName = Svc.ClientState.LocalPlayer?.CurrentWorld.Value.Name.ExtractText();
            });

            if (characterName == null || worldName == null)
            {
                Svc.Chat.PrintError("Failed to get character information for party request.");
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

    }
}
