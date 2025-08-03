# Testing Guide - Step 6: Unit / Integration Testing

This guide covers how to test the custom duties functionality (Hunt, FATE, Role Playing duties with IDs 9999-9997).

## Unit Tests

### Running Unit Tests In-Game

1. **Build and install the plugin** with the new test commands
2. **Launch FFXIV** and make sure the plugin is loaded
3. **Run the unit tests** using the chat command:
   ```
   /pftest
   ```

This will run all automated tests and display results in chat:
- ✅ PASS results will show in normal chat color
- ❌ FAIL results will show in red error color

### Expected Test Results

The unit tests verify:

1. **GetAllDuties() contains custom duties (9999-9997)**
   - Hunt duty (ID 9999) is present with correct name "Hunt"
   - FATE duty (ID 9998) is present with correct name "FATE"  
   - Role Playing duty (ID 9997) is present with correct name "Role Playing"

2. **SearchDuties("hunt") returns Hunt duty**
   - Case-insensitive search works ("hunt", "HUNT", "Hunt")
   - Returns duty with ID 9999 and name "Hunt"

3. **Dropdown display format consistency**
   - Custom duties show format: "Name (ContentType)" same as real duties
   - Hunt duty displays as "Hunt (Unspecified)"
   - Tooltips include ID, Level, and Item Level information

4. **Alphabetical ordering**
   - All duties are sorted alphabetically by name
   - Custom duties appear in correct alphabetical positions:
     - FATE comes before Hunt
     - Hunt comes before Role Playing

5. **Custom duties are selectable**
   - All custom duties pass `IsValidDuty()` check
   - Display names work correctly
   - `GetRealDuty()` returns null for custom duties (expected behavior)

### Debug Information

You can also run the debug command to see basic information about the ContentFinderService:
```
/pfdebug
```

This shows:
- Total number of duties loaded
- Number of custom duties
- Details about each custom duty (ID, Name, Type)

## Integration Testing (In-Game Manual Testing)

### Test 1: Duty Selector Modal Functionality

1. **Open the main Party Finder Reborn window**: `/pfreborn` or `/pfr`
2. **Create a new listing** or **edit an existing listing**
3. **Click the "Select Duty" button** to open the duty selector modal
4. **Verify custom duties appear**:
   - Look for "FATE (Unspecified)" in the list
   - Look for "Hunt (Unspecified)" in the list  
   - Look for "Role Playing (Unspecified)" in the list
5. **Verify alphabetical ordering**:
   - FATE should appear early in the list (before most real duties)
   - Hunt should appear in the H section
   - Role Playing should appear in the R section
6. **Test search functionality**:
   - Type "hunt" in the search box
   - Verify "Hunt (Unspecified)" appears in results
   - Try "fate" and "role" searches

### Test 2: Custom Duty Selection

1. **Select a custom duty** (e.g., "Hunt (Unspecified)")
2. **Click "Select"** to confirm the choice
3. **Verify the duty is selected** in the listing creation/edit window
4. **Complete the listing creation** process
5. **Verify the listing displays correctly** with the custom duty

### Test 3: Filter Functionality

1. **Open the duty selector modal**
2. **Test the "All Duties" filter** - custom duties should be visible
3. **Test the "High-End Only" filter** - custom duties should NOT appear (they're not high-end)
4. **Return to "All Duties"** - custom duties should reappear

### Test 4: Existing Functionality Preservation

1. **Select a real duty** (any normal FFXIV duty)
2. **Verify it still works** exactly as before
3. **Test searching for real duties** - should work unchanged
4. **Create listings with real duties** - should work unchanged
5. **Verify tooltip information** for real duties shows correct data

## Troubleshooting

### If Unit Tests Fail

1. **Check the error messages** in chat - they'll indicate what specifically failed
2. **Common issues**:
   - Custom duties not found: Check that ContentFinderService.InitializeCache() is adding them
   - Wrong names: Verify the NameText properties are set correctly
   - Ordering issues: Check that the list is sorted after adding custom duties

### If Manual Testing Fails

1. **Custom duties not visible**:
   - Run `/pfdebug` to verify they're loaded
   - Check that DutySelectorModal is using GetAllDuties() correctly

2. **Search not working**:
   - Verify SearchDuties() method includes custom duties
   - Check case-insensitive matching

3. **Selection not working**:
   - Verify custom duties implement IDutyInfo correctly
   - Check that GetRealDuty() handling doesn't break the flow

## Expected Results Summary

After successful testing, you should be able to:

✅ **Unit Tests**: All 5 automated tests pass  
✅ **Duty Selector**: Custom duties appear alphabetically in the modal  
✅ **Search**: Can find custom duties by typing their names  
✅ **Selection**: Can select custom duties and create listings with them  
✅ **Compatibility**: All existing functionality with real duties still works  

## Commands Reference

- `/pftest` - Run all unit tests
- `/pfdebug` - Show debug information about duties
- `/pfreborn` or `/pfr` - Open main plugin window
- `/pfreborn config` - Open configuration window

## Notes

- Custom duties have IDs 9999 (Hunt), 9998 (FATE), 9997 (Role Playing)
- They show content type as "Unspecified" since they don't map to real FFXIV content types
- `GetRealDuty()` returns null for custom duties, which is expected behavior
- Custom duties are sorted alphabetically along with real duties
