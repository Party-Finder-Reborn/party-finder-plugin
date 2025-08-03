using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using PartyFinderReborn.Models;
using PartyFinderReborn.Services;

namespace PartyFinderReborn.Tests;

/// <summary>
/// Unit tests for ContentFinderService, specifically testing custom duties functionality
/// </summary>
public static class ContentFinderServiceTests
{
    /// <summary>
    /// Run all tests and return results. Can be called from plugin commands for debugging.
    /// </summary>
    /// <param name="contentFinderService">The ContentFinderService instance to test</param>
    /// <returns>Test results summary</returns>
    public static string RunAllTests(ContentFinderService contentFinderService)
    {
        var results = new List<string>();
        
        try
        {
            // Test 1: GetAllDuties contains custom duties 9999-9997
            var testResult1 = TestGetAllDutiesContainsCustomDuties(contentFinderService);
            results.Add($"Test 1 - GetAllDuties contains custom duties: {(testResult1.success ? "PASS" : "FAIL")}");
            if (!testResult1.success)
                results.Add($"  Error: {testResult1.error}");
            
            // Test 2: SearchDuties("hunt") returns id 9999
            var testResult2 = TestSearchDutiesHunt(contentFinderService);
            results.Add($"Test 2 - SearchDuties('hunt') returns Hunt duty: {(testResult2.success ? "PASS" : "FAIL")}");
            if (!testResult2.success)
                results.Add($"  Error: {testResult2.error}");
            
            // Test 3: Dropdown display strings format identically
            var testResult3 = TestDropdownDisplayFormat(contentFinderService);
            results.Add($"Test 3 - Dropdown display format consistency: {(testResult3.success ? "PASS" : "FAIL")}");
            if (!testResult3.success)
                results.Add($"  Error: {testResult3.error}");
            
            // Test 4: Alphabetical ordering
            var testResult4 = TestAlphabeticalOrdering(contentFinderService);
            results.Add($"Test 4 - Alphabetical ordering: {(testResult4.success ? "PASS" : "FAIL")}");
            if (!testResult4.success)
                results.Add($"  Error: {testResult4.error}");
            
            // Test 5: Custom duties are selectable
            var testResult5 = TestCustomDutiesAreSelectable(contentFinderService);
            results.Add($"Test 5 - Custom duties are selectable: {(testResult5.success ? "PASS" : "FAIL")}");
            if (!testResult5.success)
                results.Add($"  Error: {testResult5.error}");
            
        }
        catch (Exception ex)
        {
            results.Add($"CRITICAL ERROR: {ex.Message}");
        }
        
        return string.Join("\n", results);
    }
    
    /// <summary>
    /// Test that GetAllDuties contains the custom duty IDs 9999, 9998, 9997
    /// </summary>
    private static (bool success, string error) TestGetAllDutiesContainsCustomDuties(ContentFinderService service)
    {
        try
        {
            var allDuties = service.GetAllDuties();
            
            var huntDuty = allDuties.FirstOrDefault(d => d.RowId == 9999);
            var fateDuty = allDuties.FirstOrDefault(d => d.RowId == 9998);
            var rpDuty = allDuties.FirstOrDefault(d => d.RowId == 9997);
            
            if (huntDuty == null)
                return (false, "Hunt duty (ID 9999) not found in GetAllDuties()");
            if (fateDuty == null)
                return (false, "FATE duty (ID 9998) not found in GetAllDuties()");
            if (rpDuty == null)
                return (false, "Role Playing duty (ID 9997) not found in GetAllDuties()");
            
            // Verify names
            if (huntDuty.NameText != "Hunt")
                return (false, $"Hunt duty has incorrect name: '{huntDuty.NameText}', expected 'Hunt'");
            if (fateDuty.NameText != "FATE")
                return (false, $"FATE duty has incorrect name: '{fateDuty.NameText}', expected 'FATE'");
            if (rpDuty.NameText != "Role Playing")
                return (false, $"Role Playing duty has incorrect name: '{rpDuty.NameText}', expected 'Role Playing'");
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    
    /// <summary>
    /// Test that SearchDuties("hunt") returns the Hunt duty with ID 9999
    /// </summary>
    private static (bool success, string error) TestSearchDutiesHunt(ContentFinderService service)
    {
        try
        {
            var searchResults = service.SearchDuties("hunt");
            
            var huntDuty = searchResults.FirstOrDefault(d => d.RowId == 9999);
            if (huntDuty == null)
                return (false, "Hunt duty (ID 9999) not found in SearchDuties('hunt') results");
            
            if (huntDuty.NameText != "Hunt")
                return (false, $"Found Hunt duty has incorrect name: '{huntDuty.NameText}', expected 'Hunt'");
            
            // Test case insensitive search
            var searchResultsUpper = service.SearchDuties("HUNT");
            var huntDutyUpper = searchResultsUpper.FirstOrDefault(d => d.RowId == 9999);
            if (huntDutyUpper == null)
                return (false, "Hunt duty not found in SearchDuties('HUNT') - case insensitive search failed");
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    
    /// <summary>
    /// Test that dropdown display strings for custom duties format identically to real ones
    /// </summary>
    private static (bool success, string error) TestDropdownDisplayFormat(ContentFinderService service)
    {
        try
        {
            var allDuties = service.GetAllDuties();
            
            // Get a custom duty
            var huntDuty = allDuties.FirstOrDefault(d => d.RowId == 9999);
            if (huntDuty == null)
                return (false, "Hunt duty not found for display format test");
            
            // Get a real duty for comparison
            var realDuty = allDuties.FirstOrDefault(d => d is RealDutyInfo);
            if (realDuty == null)
                return (false, "No real duty found for display format comparison");
            
            // Create DutySelectableItem instances to test display formatting
            var huntSelectableItem = new Windows.DutySelectableItem(huntDuty, service);
            var realSelectableItem = new Windows.DutySelectableItem(realDuty, service);
            
            // Check format consistency: "Name (ContentType)"
            var huntDisplayText = huntSelectableItem.DisplayText;
            var realDisplayText = realSelectableItem.DisplayText;
            
            // Both should contain parentheses for content type
            if (!huntDisplayText.Contains("(") || !huntDisplayText.Contains(")"))
                return (false, $"Hunt duty display text lacks proper format: '{huntDisplayText}'");
            
            if (!realDisplayText.Contains("(") || !realDisplayText.Contains(")"))
                return (false, $"Real duty display text lacks proper format: '{realDisplayText}'");
            
            // Hunt duty should show "Hunt (Unspecified)"
            if (!huntDisplayText.StartsWith("Hunt ("))
                return (false, $"Hunt duty display text format incorrect: '{huntDisplayText}', expected to start with 'Hunt ('");
            
            // Test tooltip format
            var huntTooltip = huntSelectableItem.TooltipText;
            if (!huntTooltip.Contains("ID: 9999"))
                return (false, $"Hunt duty tooltip missing ID: '{huntTooltip}'");
            if (!huntTooltip.Contains("Level:"))
                return (false, $"Hunt duty tooltip missing Level: '{huntTooltip}'");
            if (!huntTooltip.Contains("Item Level:"))
                return (false, $"Hunt duty tooltip missing Item Level: '{huntTooltip}'");
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    
    /// <summary>
    /// Test that duties are returned in alphabetical order
    /// </summary>
    private static (bool success, string error) TestAlphabeticalOrdering(ContentFinderService service)
    {
        try
        {
            var allDuties = service.GetAllDuties();
            
            if (allDuties.Count < 2)
                return (false, "Not enough duties to test alphabetical ordering");
            
            for (int i = 1; i < allDuties.Count; i++)
            {
                var current = allDuties[i].NameText;
                var previous = allDuties[i - 1].NameText;
                
                if (string.Compare(previous, current, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return (false, $"Duties not in alphabetical order: '{previous}' comes before '{current}'");
                }
            }
            
            // Specifically check that custom duties are in the right alphabetical positions
            var huntIndex = allDuties.FindIndex(d => d.RowId == 9999);
            var fateIndex = allDuties.FindIndex(d => d.RowId == 9998);
            var rpIndex = allDuties.FindIndex(d => d.RowId == 9997);
            
            if (huntIndex == -1 || fateIndex == -1 || rpIndex == -1)
                return (false, "One or more custom duties not found in the list");
            
            // FATE should come before Hunt, which should come before Role Playing
            if (fateIndex >= huntIndex)
                return (false, $"FATE (index {fateIndex}) should come before Hunt (index {huntIndex}) alphabetically");
            
            if (huntIndex >= rpIndex)
                return (false, $"Hunt (index {huntIndex}) should come before Role Playing (index {rpIndex}) alphabetically");
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    
    /// <summary>
    /// Test that custom duties can be selected and return proper data
    /// </summary>
    private static (bool success, string error) TestCustomDutiesAreSelectable(ContentFinderService service)
    {
        try
        {
            // Test that custom duties have proper properties
            var customDutyIds = new uint[] { 9999, 9998, 9997 };
            var expectedNames = new string[] { "Hunt", "FATE", "Role Playing" };
            
            for (int i = 0; i < customDutyIds.Length; i++)
            {
                var dutyId = customDutyIds[i];
                var expectedName = expectedNames[i];
                
                // Test that the duty is valid
                if (!service.IsValidDuty(dutyId))
                    return (false, $"Custom duty {dutyId} is not valid according to IsValidDuty()");
                
                // Test display name
                var displayName = service.GetDutyDisplayName(dutyId);
                if (displayName != expectedName)
                    return (false, $"Custom duty {dutyId} display name is '{displayName}', expected '{expectedName}'");
                
                // Test detailed display name
                var detailedName = service.GetDutyDetailedDisplayName(dutyId);
                if (!detailedName.Contains(expectedName))
                    return (false, $"Custom duty {dutyId} detailed display name '{detailedName}' doesn't contain '{expectedName}'");
                
                // Test that GetRealDuty returns null for custom duties (as expected)
                var realDuty = service.GetRealDuty(dutyId);
                if (realDuty != null)
                    return (false, $"Custom duty {dutyId} returned non-null from GetRealDuty() - should return null for custom duties");
            }
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    
    /// <summary>
    /// Get a quick summary of custom duties for debug purposes
    /// </summary>
    public static string GetCustomDutiesSummary(ContentFinderService service)
    {
        try
        {
            var allDuties = service.GetAllDuties();
            var customDuties = allDuties.Where(d => d is CustomDutyInfo).ToList();
            
            var summary = $"Total duties: {allDuties.Count}\n";
            summary += $"Custom duties: {customDuties.Count}\n\n";
            
            foreach (var duty in customDuties)
            {
                summary += $"ID: {duty.RowId}, Name: '{duty.NameText}', Type: {service.GetContentTypeName(duty)}\n";
            }
            
            return summary;
        }
        catch (Exception ex)
        {
            return $"Error getting custom duties summary: {ex.Message}";
        }
    }
}
