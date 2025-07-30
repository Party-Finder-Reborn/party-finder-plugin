# Party Finder Reborn - Manual Smoke Testing Guide

## Prerequisites

### 1. Build Plugin
```bash
cd "/home/nostrathomas/Projects/Party Finder Reborn/party-finder-plugin"
dotnet build PartyFinderReborn.sln --configuration Debug
```

### 2. Plugin Files Location
- **Main DLL**: `PartyFinderReborn/bin/Debug/PartyFinderReborn.dll`
- **Dependencies**: `PartyFinderReborn/bin/Debug/ECommons.dll`
- **Manifest**: `PartyFinderReborn/bin/Debug/PartyFinderReborn.json`

### 3. Installation in Dalamud
1. Copy plugin files to Dalamud testing directory (typically `%AppData%\XIVLauncher\devPlugins\PartyFinderReborn\`)
2. Start FFXIV with Dalamud
3. Use `/xlplugins` to enable the plugin for testing

## Test Scenarios

### Test 1: Plugin Loading and Basic Functionality
**Objective**: Verify the plugin loads without crashes and basic UI opens

**Steps**:
1. Load plugin in Dalamud test client
2. Execute command: `/pfreborn`
3. Verify main window opens
4. Execute debug command: `/pfdebug`
5. Check Dalamud log for initialization messages

**Expected Results**:
- ✅ Plugin loads successfully
- ✅ Main window opens with title "Party Finder Reborn"
- ✅ Debug info appears in chat/log showing duty progress statistics
- ✅ No exceptions or errors in Dalamud log
- ✅ Status shows "Connected" or appropriate connection state

### Test 2: Pagination Controls
**Objective**: Verify pagination buttons function correctly and fetch different pages

**Steps**:
1. Open main window (`/pfreborn`)
2. Wait for initial party listings to load
3. Check pagination controls at bottom of listings panel
4. Click "Next ▶" button (if available)
5. Verify new listings load
6. Click "◀ Previous" button
7. Verify previous page loads

**Expected Results**:
- ✅ Pagination controls visible with "◀ Previous" and "Next ▶" buttons
- ✅ "Previous" button disabled on first page
- ✅ "Next" button disabled when no more pages
- ✅ Total count displays correctly: "Total: X listings"
- ✅ Clicking buttons loads new data without crashes
- ✅ Loading spinner appears during page transitions
- ✅ Page navigation updates listing content

### Test 3: Automatic Refresh
**Objective**: Verify automatic refresh updates listings without user action

**Steps**:
1. Open main window
2. Note current listings and timestamp
3. Wait for auto-refresh interval (check Configuration.RefreshIntervalSeconds)
4. Observe automatic updates
5. Verify "Last Refresh" timestamp updates

**Expected Results**:
- ✅ Listings refresh automatically at configured intervals
- ✅ "Last Refresh" timestamp updates without user action
- ✅ No flickering or UI disruption during refresh
- ✅ Auto-refresh only occurs when main window is open
- ✅ Loading state handled properly during background refresh

### Test 4: Join Flow - PF Code Copy and UI Updates
**Objective**: Verify join functionality copies PF code and updates UI

**Steps**:
1. Open main window
2. Click on any party listing to open detail window
3. Click "Join Party" button
4. Check for PF code copied to clipboard
5. Verify UI updates after join attempt
6. Check chat messages for join status

**Expected Results**:
- ✅ Detail window opens when clicking listing
- ✅ "Join Party" button present and clickable
- ✅ Loading spinner appears during join process
- ✅ PF code copied to clipboard (if successful)
- ✅ Chat message confirms: "[Party Finder Reborn] Party Finder code 'XXXX' copied to clipboard!"
- ✅ Window closes after successful join
- ✅ Error messages displayed if join fails
- ✅ Party status updates to "full" if applicable

### Test 5: Progress Point Display - Action Names
**Objective**: Verify progress points show proper action names instead of IDs

**Steps**:
1. Open a party listing detail window
2. Check "Progress Points" section
3. Look for boss ability names (not numeric IDs)
4. Check both view mode and edit mode
5. Verify progression status display

**Expected Results**:
- ✅ Progress points display as action names (e.g. "Savage Claw", "Enrage")
- ✅ Not showing raw numeric IDs (e.g. "12345")
- ✅ Progression status shows: "✓ Action Name (Seen)" or "✗ Action Name (Not Seen)"
- ✅ Edit mode allows selecting from dropdown of action names
- ✅ Action names resolve correctly via ActionNameService

### Test 6: Crash Prevention and Exception Handling
**Objective**: Confirm no crashes or exceptions in Dalamud log

**Steps**:
1. Monitor Dalamud log continuously during testing
2. Perform all above test scenarios
3. Try edge cases:
   - Rapid clicking pagination buttons
   - Opening multiple detail windows
   - Network disconnection scenarios
   - Invalid duty selections
   - Empty data scenarios

**Expected Results**:
- ✅ No unhandled exceptions in Dalamud log
- ✅ No plugin crashes or freezes
- ✅ Graceful error handling for network issues
- ✅ Proper logging of debug information
- ✅ Memory cleanup (no resource leaks)

## Additional Verification Points

### Configuration and Commands
- `/pfreborn` - Opens main window
- `/pfreborn config` - Opens configuration window  
- `/pfrefresh` - Manual refresh of duty progress data
- `/pfdebug` - Shows debug information

### Key Features to Validate
1. **Window Management**: Multiple windows can open/close without issues
2. **Data Loading**: API calls complete successfully or fail gracefully
3. **User Profile**: User authentication and profile loading
4. **Duty Selection**: Content Finder integration works
5. **Filter System**: Filtering by status, datacenter, world, tags
6. **Real-time Updates**: Auto-refresh maintains data consistency

### Performance Checks
- Window opening/closing speed
- API response times
- Memory usage stability
- UI responsiveness during data loading

## Troubleshooting Common Issues

### Plugin Won't Load
- Check Dalamud API level compatibility (currently set to 12)
- Verify all dependencies are present (ECommons.dll)
- Check plugin manifest JSON syntax

### API Connection Issues
- Verify network connectivity
- Check Configuration settings for API endpoints
- Look for authentication errors in logs

### UI Display Problems
- Check ImGui version compatibility
- Verify window size constraints
- Look for font rendering issues

## Test Results Template

```
# Test Results - [Date/Time]

## Environment
- FFXIV Version: 
- Dalamud Version:
- Plugin Version: 
- Character/World:

## Test 1: Plugin Loading ✅/❌
- Notes:

## Test 2: Pagination ✅/❌
- Notes:

## Test 3: Auto Refresh ✅/❌
- Notes:

## Test 4: Join Flow ✅/❌
- Notes:

## Test 5: Progress Points ✅/❌
- Notes:

## Test 6: Exception Handling ✅/❌
- Notes:

## Overall Status: ✅/❌
## Critical Issues Found:
## Minor Issues Found:
```

## Next Steps After Testing
1. Document any bugs or issues found
2. Verify fixes for any problems
3. Test edge cases and error scenarios  
4. Prepare for release testing
