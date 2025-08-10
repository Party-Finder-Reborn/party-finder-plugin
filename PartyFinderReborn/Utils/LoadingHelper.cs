using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PartyFinderReborn.Utils;

public static class LoadingHelper
{
    /// <summary>
    /// Draws an animated loading spinner centered in the current window.
    /// </summary>
    public static void DrawLoadingSpinner()
    {
        // Get the current window's position and size
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var center = windowPos + (windowSize / 2);
        
        var drawList = ImGui.GetForegroundDrawList(); // Use foreground draw list to appear on top
        var spinnerColor = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
        var backgroundColor = ImGui.GetColorU32(ImGuiCol.FrameBg, 0.7f);
        var radius = 30;
        var thickness = 5;
        
        // Add semi-transparent background circle
        drawList.AddCircleFilled(center, radius + 5, backgroundColor);
        
        // Animate the spinner by calculating rotation based on time
        var time = Environment.TickCount / 100.0f;
        var startAngle = (float)(Math.PI * 0.5f * (time % 4));
        var endAngle = startAngle + (float)(Math.PI * 1.5f);
        
        // Draw the animated arc
        drawList.PathArcTo(center, radius, startAngle, endAngle, 32);
        drawList.PathStroke(spinnerColor, ImDrawFlags.None, thickness);
    }
    
    /// <summary>
    /// Draws a loading spinner with a custom message below it.
    /// </summary>
    /// <param name="message">The message to display below the spinner</param>
    public static void DrawLoadingSpinnerWithMessage(string message)
    {
        DrawLoadingSpinner();
        
        // Add text below the spinner
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var textSize = ImGui.CalcTextSize(message);
        var textPos = windowPos + new Vector2((windowSize.X - textSize.X) / 2, (windowSize.Y / 2) + 50);
        
        var drawList = ImGui.GetForegroundDrawList();
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        drawList.AddText(textPos, textColor, message);
    }
}
