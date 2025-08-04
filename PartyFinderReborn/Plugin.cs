using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using PartyFinderReborn.Windows;
using PartyFinderReborn.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using PartyFinderReborn.Models;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.ImGuiMethods;
using PartyFinderReborn.Tests;

namespace PartyFinderReborn;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pfreborn";
    private const string ConfigCommandName = "/pfreborn config";
    private const string RefreshCommandName = "/pfrefresh";
    private const string DebugCommandName = "/pfdebug";

    public Configuration Configuration { get; init; }
    public ApiDebounceService DebounceService { get; init; }
    public PartyFinderApiService ApiService { get; init; }
    public ContentFinderService ContentFinderService { get; init; }
    public DutyProgressService DutyProgressService { get; init; }
    public ActionNameService ActionNameService { get; init; }
    public ActionTrackingService ActionTrackingService { get; init; }
    public WorldService WorldService { get; init; }
    public PluginService PluginService { get; init; }

    public readonly WindowSystem WindowSystem = new("PartyFinderReborn");
    public ConfigWindow ConfigWindow { get; init; }
    public MainWindow MainWindow { get; init; }

    private DateTime lastFrameworkUpdate = DateTime.Now;
    private DateTime lastBackgroundRefresh = DateTime.Now;
    private readonly Dictionary<string, CancellationTokenSource> _notificationWorkers = new();
    private readonly Dictionary<string, long> _lastNotificationTimestamps = new();
    private readonly Dictionary<uint, Action<uint, SeString>> _chatLinkHandlers = new();
    private readonly HashSet<string> _notifiedInvitations = new(); // Track which invitations we've already shown to user
    private readonly Dictionary<string, List<InvitationNotification>> _pendingInvitationsByListing = new(); // Track pending invitations by listing ID
    
    // Event for listing creation
    public event Action? OnListingCreated;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Initialize ECommons
        ECommonsMain.Init(pluginInterface, this);

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        
        // Initialize services
        DebounceService = new ApiDebounceService();
        ApiService = new PartyFinderApiService(Configuration, DebounceService);
        ContentFinderService = new ContentFinderService();
        DutyProgressService = new DutyProgressService(ContentFinderService, ApiService, Configuration);
        ActionNameService = new ActionNameService();
        ActionTrackingService = new ActionTrackingService(DutyProgressService, ContentFinderService, Configuration);
        WorldService = new WorldService();
        PluginService = new PluginService();

        // Initialize windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register commands
        Svc.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Party Finder Reborn interface"
        });
        
        // Add alias for easier command access
        const string AliasCommandName = "/pfr";
        Svc.Commands.AddHandler(AliasCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Party Finder Reborn interface"
        });
        
        // Add debug command
        Svc.Commands.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand)
        {
            HelpMessage = "Run ContentFinderService debug tests"
        });
        
        // Add test command for custom duties
        const string TestCommandName = "/pftest";
        Svc.Commands.AddHandler(TestCommandName, new CommandInfo(OnTestCommand)
        {
            HelpMessage = "Run unit tests for custom duties functionality"
        });
        
        // Handling config through main cmd
        Svc.Commands.AddHandler(ConfigCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Party Finder Reborn configuration window"
        });

        // Don't initialize duty progress tracking automatically - wait for API key validation

        // Hook into UI events
        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        // Subscribe to login and logout events
        Svc.ClientState.Login += OnLogin;
        Svc.Framework.Update += OnFrameworkUpdate;
        
        // Subscribe to configuration changes
        Configuration.ConfigUpdated += OnConfigurationUpdated;
        
        // Subscribe MainWindow to listing creation events
        OnListingCreated += MainWindow.OnListingCreated;
        
        // Validate API key on startup if one exists
        if (!string.IsNullOrEmpty(Configuration.ApiKey))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ConfigWindow.ValidateApiKeyOnStartupAsync();
                    // Duty initialization is now handled by ConfigWindow after validation
                }
                catch (Exception ex)
                {
                    Svc.Log.Warning($"Failed during API key validation on startup: {ex.Message}");
                }
            });
        }

    }

    private void OnLogin()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Only sync duties if API key is valid
                if (ConfigWindow.ShouldAllowApiRequests)
                {
                    await DutyProgressService.SyncDutiesOnLoginAsync();
                }
                else
                {
                    Svc.Log.Debug("Skipping duty sync on login - API key validation required");
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to sync duties on login: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow?.Dispose();
        MainWindow?.Dispose();
        ActionTrackingService?.Dispose();
        ActionNameService?.Dispose();
        DutyProgressService?.Dispose();
        ApiService?.Dispose();
        ContentFinderService?.Dispose();
        WorldService?.Dispose();
        PluginService?.Dispose();

        Svc.Commands.RemoveHandler(CommandName);
        Svc.Commands.RemoveHandler("/pfr"); // Remove alias
        Svc.Commands.RemoveHandler(ConfigCommandName);
        Svc.Commands.RemoveHandler(DebugCommandName);
        Svc.Commands.RemoveHandler("/pftest");

        Svc.Framework.Update -= OnFrameworkUpdate;
        
        // Unsubscribe from configuration changes
        Configuration.ConfigUpdated -= OnConfigurationUpdated;
        
        // Unsubscribe from listing creation events
        OnListingCreated -= MainWindow.OnListingCreated;
        
        // Cancel all notification workers
        foreach (var (listingId, cts) in _notificationWorkers)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _notificationWorkers.Clear();
        
        // Remove all chat link handlers
        foreach (var commandId in _chatLinkHandlers.Keys)
        {
            Svc.PluginInterface.RemoveChatLinkHandler(commandId);
        }
        _chatLinkHandlers.Clear();

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // Check if this is a config command or if args contain "config"
        if (command == ConfigCommandName || (!string.IsNullOrWhiteSpace(args) && args.Trim().ToLowerInvariant() == "config"))
        {
            ToggleConfigUI();
        }
        else
        {
            // Toggle main window on command
            ToggleMainUI();
        }
    }
    
    private void OnRefreshCommand(string command, string args)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DutyProgressService.RefreshProgressData();
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to refresh duty progress data: {ex.Message}");
            }
        });
    }
    
    private void OnConfigCommand(string command, string args)
    {
        ToggleConfigUI();
    }
    
    private void OnDebugCommand(string command, string args)
    {
        try
        {
            var completedCount = DutyProgressService.GetCompletedDutiesCount();
            var progPointsDutiesCount = DutyProgressService.GetProgPointsDutiesCount();
            var totalProgPointsCount = DutyProgressService.GetTotalProgPointsCount();
            var sessionProgPointsCount = ActionTrackingService.GetSeenProgPointsCount();
            
            Svc.Chat.Print($"[Debug] Completed duties: {completedCount}");
            Svc.Chat.Print($"[Debug] Prog points duties: {progPointsDutiesCount}");
            Svc.Chat.Print($"[Debug] Total prog points: {totalProgPointsCount}");
            Svc.Chat.Print($"[Debug] Session prog points: {sessionProgPointsCount}");
            
            if (completedCount > 0)
            {
                var completedDuties = DutyProgressService.GetCompletedDuties();
                var firstCompleted = completedDuties.FirstOrDefault();
                Svc.Chat.Print($"[Debug] First completed duty: {firstCompleted}");
            }
            
            // Show custom duties summary
            var customDutiesSummary = ContentFinderServiceTests.GetCustomDutiesSummary(ContentFinderService);
            var lines = customDutiesSummary.Split('\n');
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Svc.Chat.Print($"[Debug] {line}");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to get debug info: {ex.Message}");
            Svc.Chat.PrintError($"Debug command failed: {ex.Message}");
        }
    }
    
    private void OnTestCommand(string command, string args)
    {
        try
        {
            Svc.Chat.Print("[Test] Running ContentFinderService unit tests...");
            
            var testResults = ContentFinderServiceTests.RunAllTests(ContentFinderService);
            var lines = testResults.Split('\n');
            
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (line.Contains("PASS"))
                        Svc.Chat.Print($"[Test] {line}");
                    else if (line.Contains("FAIL") || line.Contains("ERROR"))
                        Svc.Chat.PrintError($"[Test] {line}");
                    else
                        Svc.Chat.Print($"[Test] {line}");
                }
            }
            
            Svc.Chat.Print("[Test] Unit tests completed.");
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to run unit tests: {ex.Message}");
            Svc.Chat.PrintError($"Test command failed: {ex.Message}");
        }
    }
    
    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.Now;
        var elapsed = now - lastFrameworkUpdate;
        var backgroundElapsed = now - lastBackgroundRefresh;

        // Auto-refresh every 60 seconds (hardcoded)
        if (elapsed.TotalSeconds >= 60)
        {
            lastFrameworkUpdate = now;

            // Always auto-refresh when main window is open - use background method to bypass debouncing
            if (MainWindow.IsOpen)
            {
                _ = MainWindow.LoadPartyListingsAsync_Background();
            }
        }
        
        // Background refresh every 5 minutes to keep online count accurate even when windows are closed
        if (backgroundElapsed.TotalMinutes >= 5)
        {
            lastBackgroundRefresh = now;
            
            // Perform a lightweight user profile refresh to maintain API activity
            _ = PerformBackgroundRefreshAsync();
        }
    }
    
    /// <summary>
    /// Performs a lightweight background refresh to maintain user presence and update online count
    /// </summary>
    private async Task PerformBackgroundRefreshAsync()
    {
        try
        {
            // Check if API key is valid before making request
            if (string.IsNullOrEmpty(Configuration.ApiKey) || !ConfigWindow.ShouldAllowApiRequests)
            {
                return;
            }
            
            // Just fetch the user profile - this is lightweight and maintains our presence in the online count
            // Use the no-debounce version for background operations
            await ApiService.GetUserProfileAsync_NoDebounce();
        }
        catch
        {
            // Silent failure - we don't want to spam logs for background operations
        }
    }
    
    public void StartJoinNotificationWorker(string listingId)
    {
        // Don't start if already running for this listing
        if (_notificationWorkers.ContainsKey(listingId))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _notificationWorkers[listingId] = cts;
        var token = cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var notifications = await ApiService.GetNotificationsAsync(
                        since: _lastNotificationTimestamps.GetValueOrDefault(listingId), 
                        listingId: listingId);

                    if (notifications?.Notifications != null && notifications.Notifications.Any())
                    {
                        // Update pending invitations for this listing
                        _pendingInvitationsByListing[listingId] = notifications.Notifications.ToList();
                        
                        foreach (var notification in notifications.Notifications)
                        {
                            // Store last notification timestamp
                            var timestamp = ((DateTimeOffset)notification.CreatedAt).ToUnixTimeSeconds();
                            _lastNotificationTimestamps[listingId] = timestamp;

                            // Send notification to chat
                            SendJoinNotificationToChat(notification);
                        }
                    }
                    else
                    {
                        // Clear pending invitations if no notifications
                        if (_pendingInvitationsByListing.ContainsKey(listingId))
                        {
                            _pendingInvitationsByListing[listingId].Clear();
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(7), token);
                }
                catch (OperationCanceledException)
                {
                    // Task was canceled, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Svc.Log.Error($"Error polling for join notifications on listing {listingId}: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(10), token); // Wait longer on error
                }
            }
        }, token);
        
    }

    public void StopJoinNotificationWorker(string listingId)
    {
        if (_notificationWorkers.TryGetValue(listingId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _notificationWorkers.Remove(listingId);
            _lastNotificationTimestamps.Remove(listingId);
            
        }
    }

    private void SendJoinNotificationToChat(InvitationNotification notification)
    {
        // Check if we've already notified about this invitation
        if (_notifiedInvitations.Contains(notification.Id))
        {
            return; // Skip - already notified
        }
        
        // Mark this invitation as notified
        _notifiedInvitations.Add(notification.Id);
        
        // Generate a unique command ID for this notification
        var commandId = (uint)(_chatLinkHandlers.Count + 1);
        
        // Create the handler for this specific invite
        Action<uint, SeString> handler = (id, seString) =>
        {
            InvitePlayerToParty(notification.CharacterName, notification.CharacterWorld, notification.Id, commandId);
        };
        
        // Register the handler and store it
        var linkPayload = Svc.PluginInterface.AddChatLinkHandler(commandId, handler);
        _chatLinkHandlers[commandId] = handler;

        // Build the SeString message with clickable link
        var message = new SeStringBuilder()
            .AddText("[PartyFinderReborn] ")
            .AddText($"{notification.CharacterDisplay} wants to join your party! ")
            .Add(linkPayload)
            .AddText("[Click to Invite]")
            .Add(RawPayload.LinkTerminator)
            .BuiltString;

        // Send to chat
        Svc.Chat.Print(message);
        
        // Show toast notification
        try
        {
            Notify.Info($"{notification.CharacterDisplay} wants to join your party!");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"Failed to show toast notification: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send a content moderation message to chat
    /// </summary>
    public void SendModerationMessage(string moderationReason)
    {
        // Clean up the moderation reason by removing redundant prefixes
        var cleanedReason = moderationReason;
        
        // Remove common prefixes that make the message redundant
        if (cleanedReason.StartsWith("Content violates community guidelines: "))
        {
            cleanedReason = cleanedReason.Substring("Content violates community guidelines: ".Length);
        }
        
        // Capitalize first letter for better presentation
        if (!string.IsNullOrEmpty(cleanedReason))
        {
            cleanedReason = char.ToUpper(cleanedReason[0]) + cleanedReason.Substring(1);
        }
        
        var message = new SeStringBuilder()
            .AddText("[Party Finder Reborn] ")
            .AddText("Your listing was rejected: ")
            .AddText(cleanedReason)
            .AddText(". Please modify your content and try again.")
            .BuiltString;

        Svc.Chat.PrintError(message);
    }

    private unsafe void InvitePlayerToParty(string? characterName, string? characterWorld, string? notificationId, uint? commandId = null)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(characterWorld))
        {
            Svc.Chat.PrintError("Invalid character information for party invite.");
            return;
        }

        // Execute the party invite on the framework thread
        Svc.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                // Check if player is busy before sending invite
                if (ECommons.GameHelpers.Player.IsBusy)
                {
                    Svc.Chat.PrintError($"You are currently busy and cannot send a party invitation to {characterName}@{characterWorld}. Please finish what you're doing and try again.");
                    return; // Don't remove handler - user can try again later
                }

                var infoModule = InfoModule.Instance();
                if (infoModule == null)
                {
                    Svc.Chat.PrintError("Failed to access InfoModule.");
                    return; // Don't remove handler - this might be temporary
                }

                var partyInviteProxy = (InfoProxyPartyInvite*)infoModule->GetInfoProxyById(InfoProxyId.PartyInvite);
                if (partyInviteProxy == null)
                {
                    Svc.Chat.PrintError("Failed to access PartyInvite proxy.");
                    return; // Don't remove handler - this might be temporary
                }

                // Try to invite by character name and world
                var worldId = GetWorldIdFromName(characterWorld);
                if (worldId == 0)
                {
                    Svc.Chat.PrintError($"Unknown world: {characterWorld}");
                    return; // Don't remove handler - world should be valid, but might be temporary issue
                }

                var success = partyInviteProxy->InviteToParty(0, characterName, worldId);
                if (success)
                {
                    Svc.Chat.Print($"Party invitation sent to {characterName}@{characterWorld}.");
                    
                    // Remove the chat link handler only on successful invite
                    if (commandId.HasValue && _chatLinkHandlers.ContainsKey(commandId.Value))
                    {
                        Svc.PluginInterface.RemoveChatLinkHandler(commandId.Value);
                        _chatLinkHandlers.Remove(commandId.Value);
                    }
                    
                    // Dismiss the notification from the server after successful invite
                    // We need to do this outside the unsafe context
                    DismissNotificationAfterInvite(notificationId);
                }
                else
                {
                    Svc.Chat.PrintError($"Failed to send party invitation to {characterName}@{characterWorld}.");
                    // Don't remove handler - user can try again later
                }
            }
            catch (Exception ex)
            {
                Svc.Chat.PrintError($"Error sending party invitation: {ex.Message}");
                Svc.Log.Error($"Error in InvitePlayerToParty: {ex}");
                // Don't remove handler - this was an unexpected error, user can try again
            }
        });
    }

    private ushort GetWorldIdFromName(string worldName)
    {
        try
        {
            var world = WorldService.GetWorldByName(worldName);
            return (ushort)(world?.RowId ?? 0);
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error getting world ID for {worldName}: {ex.Message}");
            return 0;
        }
    }
    
    private void DismissNotificationAfterInvite(string? notificationId)
    {
        if (string.IsNullOrEmpty(notificationId))
            return;
            
        _ = Task.Run(async () =>
        {
            try
            {
                var dismissed = await ApiService.DismissInvitationAsync(notificationId);
                if (dismissed)
                {
                }
                else
                {
                    Svc.Log.Warning($"Failed to dismiss notification {notificationId} from server");
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error dismissing notification {notificationId}: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Get pending invitations for a specific listing
    /// </summary>
    public List<InvitationNotification> GetPendingInvitations(string listingId)
    {
        if (_pendingInvitationsByListing.TryGetValue(listingId, out var invitations))
        {
            return invitations.Where(inv => !inv.Expired.GetValueOrDefault()).ToList();
        }
        return new List<InvitationNotification>();
    }
    
    /// <summary>
    /// Get pending invitation by participant Discord ID
    /// </summary>
    public InvitationNotification? GetPendingInvitationForParticipant(string listingId, string participantDiscordId)
    {
        var pendingInvitations = GetPendingInvitations(listingId);
        return pendingInvitations.FirstOrDefault(inv => 
            inv.Requester.DiscordId.Equals(participantDiscordId, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Public wrapper to invite a player to party from UI
    /// </summary>
    public void InvitePlayerToPartyFromUI(string? characterName, string? characterWorld, string? notificationId)
    {
        InvitePlayerToParty(characterName, characterWorld, notificationId);
    }
    
    /// <summary>
    /// Trigger the listing created event
    /// </summary>
    public void TriggerListingCreated()
    {
        OnListingCreated?.Invoke();
    }
    
    private void OnConfigurationUpdated()
    {
        
        // Refresh all authentication-dependent data in the main window
        _ = Task.Run(async () =>
        {
            try
            {
                await MainWindow.RefreshAllAuthenticatedDataAsync();
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to refresh authenticated data after configuration update: {ex.Message}");
            }
        });
        
        // Also refresh duty progress data (only if API key is valid)
        _ = Task.Run(async () =>
        {
            try
            {
                if (ConfigWindow.ShouldAllowApiRequests)
                {
                    await DutyProgressService.RefreshProgressData();
                }
                else
                {
                    Svc.Log.Debug("Skipping duty progress data refresh - API key validation required");
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to refresh duty progress data after configuration update: {ex.Message}");
            }
        });
    }
}
