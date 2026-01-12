# Debug Log & Implementation Progress

## Status Summary

| Feature | Status | Notes |
|---------|--------|-------|
| Menu Text Reading | Working | Generic cursor + MenuTextDiscovery |
| New Game Naming | Working | Character slots, name cycling |
| Dialogue | Working | NPC dialogue, fade/scroll messages |
| Field Navigation | Working | Entity scan, pathfinding, wall bump, category cycling |
| Battle Commands | Working | Turn, command, target announcements |
| Battle Messages | Working | Damage, healing, status effects, encounter types, escape |
| Battle State Mgmt | Working | Proper flag clearing on flee/defeat, main menu reset |
| Battle Results | Working | Gil, weapon/magic level-ups, stat gains |
| Equipment Menu | Working | Slot/item selection with stat comparison |
| Item Menu | Working | Item list, character target with MP |
| Status Menu | Working | Character selection with HP/MP |
| Status Details | Working | Arrow key navigation through all stats |
| Magic Menu | Working | Spell list, Use/Forget commands, character target, proficiency |
| Popup Dialogs | Working | Yes/No confirmations (tome learning, etc.) |
| Config Menu | Working | Config options with current values |
| Shop Menu | Partial | Buy/sell works; unaffordable items don't announce |
| Battle Item/Magic | Working | Item/spell selection during battle |
| Keyword System | Partial | NPC dialogue (Ask/Learn/Key Items) works; Words menu reads name only |
| Vehicle/Movement | Working | Ship, airship, chocobo state changes |

---

## Project Structure

```
Core/           FFII_ScreenReaderMod.cs, InputManager.cs, Filters/
Menus/          MenuTextDiscovery.cs, SaveSlotReader.cs, StatusDetailsReader.cs
Field/          EntityScanner.cs, FieldNavigationHelper.cs, NavigableEntity.cs, MapNameResolver.cs
Patches/        NewGameNaming, MessageWindow, ScrollMessage, MovementSound,
                BattleCommand, BattleMessage, BattleResult, EquipMenu,
                ItemMenu, StatusMenu, MagicMenu, ConfigMenu, Shop,
                BattleItem, BattleMagic, Keyword, VehicleLanding, MovementSpeech, Popup
Utils/          TolkWrapper.cs, GameObjectCache.cs, TextUtils.cs, MoveStateHelper.cs
```

---

## Key Technical Details

### IL2CPP Access Patterns
- Use properties directly (`__instance.targetData`), not `AccessTools.Field` reflection
- Use direct property access (`playerController.fieldPlayer`) instead of `GetField("player")`
- Always patch both Touch and KeyInput controller variants
- Wrap all IL2CPP calls in try-catch blocks

### Active State Pattern
All menu states follow this pattern for cursor suppression:
```csharp
public static class MenuState {
    public static bool IsActive { get; private set; }
    public static void SetActive() {
        FFII_ScreenReaderMod.ClearOtherMenuStates("MenuName");
        IsActive = true;
    }
    public static bool ShouldSuppress() {
        if (!IsActive) return false;
        // Auto-reset if controller closed
        var controller = FindObjectOfType<ControllerType>();
        if (controller == null || !controller.gameObject.activeInHierarchy) {
            ClearState();
            return false;
        }
        return true;
    }
    public static void ClearState() { IsActive = false; }
}
```

### Battle Result Phases
| Phase | Method | Data Available |
|-------|--------|----------------|
| 1 | Show_Postfix | Gil amount |
| 2 | ShowSkillLevelsInit (Touch) / ShowLevelUpAbilitysInit (KeyInput) | Weapon skills |
| 3 | ShowStatusUpInit | Stat gains (HP, Evasion, etc.) |
| 4 | ShowGetItemsInit | Item drops |

**Key**: `GrouthWeaponSkillList` is empty in Show_Postfix - must patch ShowSkillLevelsInit phase.

### FF2 Weapon Skill Level Reading
**STATUS: WORKING**

StatusDetailsReader uses game utility methods for accurate weapon skill level and progress calculation.

**Methods Used**:
- `BattleUtility.GetSkillLevel(OwnedCharacterData, SkillLevelTarget)` - returns display level (1-16)
- `data.SkillLevelTargets[skillType]` - raw exp value for the skill
- `ExpUtility.GetNextExp(1, exp, ExpTableType.LevelExp)` - exp remaining to next level
- `ExpUtility.GetExpDifference(1, exp, ExpTableType.LevelExp)` - total exp for current level
- Progress: `1.0 - (nextExp / expDiff)` gives gauge fill amount (0.0-1.0)

**Max level is 16** for weapon skills (same as spells).

**SkillLevelTarget Enum Values**:
- WeaponSword=0, WeaponKnife=1, WeaponSpear=2, WeaponAxe=3
- WeaponCane=4, WeaponBow=5, WeaponShield=6, WeaponWrestle=7
- PhysicalAvoidance=8, AbilityAvoidance=9

For battle results:
- `BattleResultCharacterData.GrouthWeaponSkillList` contains skills that leveled up
- Uses `BattleUtility.GetSkillLevel(charResult.AfterData, skillTarget)` for accurate level
- **TODO**: Weapon skill percentage gained not yet implemented in battle results

**Important**: Evasion skills (PhysicalAvoidance, AbilityAvoidance) are NOT weapon skills - they only have stat gains, no exp bars. They are filtered out of weapon skill announcements.

### FF2 Spell Level Storage
**STATUS: WORKING**

`OwnedAbility.SkillLevel` stores raw exp, NOT the display level.

Game uses `ExpUtility.GetExpLevel(1, rawExp, ExpTableType.LevelExp)` for level conversion (exp table lookup).

For progress/gauge display, game uses:
- `ExpUtility.GetNextExp(1, exp, LevelExp)` - exp remaining to next level
- `ExpUtility.GetExpDifference(1, exp, LevelExp)` - total exp for current level
- `progress = 1.0 - (nextExp / expDiff)` - gauge fill amount (0.0-1.0)

### Status Details Stat Methods
| Stat | Method |
|------|--------|
| Accuracy | `ConfirmedAccuracyCount() + 1`, `ConfirmedAccuracyRate()` |
| Evasion | `ConfirmedEvasionCount()`, `ConfirmedEvasionRate()` |
| Magic Defense | `ConfirmedMagicDefenseCount()`, `ConfirmedAbilityDefense()` |
| Magic Interference | `ConfirmedAbilityDisturbedRate(true)` |

**Naming quirks**:
- `ConfirmedAbilityDefense()` = Magic Defense rate % (not `ConfirmedAbilityDefenseRate()` which returns 0)
- `ConfirmedAbilityDisturbedRate(true)` = with equipment, `(false)` = base only

### Keyword System State Machine
States: 0=None, 1=CommandSelect, 2=CommandSelecting, 3=WordSelect, 4=WordSelecting, 5=ItemSelect, 6=ItemSelecting, 7=NewWordView, 8=NewWordViewing, 9=End, 10=EndWait

Commands: 0=Ask, 1=Remember/Learn, 2=Key Items, 3=Cancel

**SecretWordControllerBase offsets**:
- `stateMachine` at 0x20, `Current` at 0x10, `Tag` at 0x10
- `selectContentCursor` at 0x30
- `wordDataList` (IEnumerable<SelectFieldContentData>) at 0x60
- `itemDataList` (IEnumerable<ItemListContentData>) at 0x68

**SelectFieldContentData offsets**:
- `NameMessageId` at 0x18
- `DescriptionMessageId` at 0x20

**ItemListContentData offsets**:
- `Name` at 0x20
- `Description` at 0x28

**KeyInput.WordsContentListController offsets** (main menu Words/Key Terms):
- `OnActionCancel` at 0x18
- `view` (WordsContentListView) at 0x20
- `contentList` (List<CommonCommandContentController>) at 0x28
- `selectCursor` at 0x30
- `keyWordContentDictionary` (Dictionary<int, Content>) at 0x38

**CommonCommandContentController offsets** (KeyInput version):
- `Id` at 0x18
- `Name` at 0x20

**Content class** (Last.Data.Master):
- Has `MesIdName` and `MesIdDescription` properties for MessageManager lookup

**Announcement format**: "keyword: description" or just "keyword" if no description
**Known issue**: Words menu description lookup not working (see Known Issues section)

### Entity Categories
1. MoveArea → "Area Boundary" (Event category)
2. GotoMap → MapExitEntity with `PropertyGotoMap.MapId` for destination
3. Other entity types (NPCs, chests, etc.)

---

## Field Navigation Hotkeys

| Key | Action |
|-----|--------|
| J/[ | Previous entity (Shift: prev category) |
| K | Repeat current |
| L/] | Next entity (Shift: next category) |
| P/\ | Pathfinding (Shift: toggle filter) |
| 0 | Reset to All category |
| =/- | Cycle category |

### Status Details Hotkeys
| Key | Action |
|-----|--------|
| Up/Down | Navigate stats |
| Ctrl+Up/Down | Jump to top/bottom |
| Shift+Up/Down | Jump between groups |
| R | Repeat current stat |

---

## Known Issues

### Spell Level Calculation - FIXED (v2 2026-01-12)
- `OwnedAbility.SkillLevel` stores raw exp, not actual level
- Game uses `ExpUtility.GetExpLevel(1, rawExp, ExpTableType.LevelExp)` for level calculation
- Old formula `(rawExp / 100) + 1` was incorrect - game uses exp table lookup
- Fixed in `MagicMenuPatches.GetSpellProficiency` and `BattleMagicPatches.FormatAbilityAnnouncement`
- Decompiled functions in `docs/Scripts/decompiled_skill_level.c`
- **Verification**: Spell level should equal MP cost in FF2 (e.g., Fire Lv1 = 1 MP)

### Weapon/Spell Progress Percentages - FIXED (v2 2026-01-12)
- Game uses `ExpUtility` methods for accurate gauge fill calculation:
  - `ExpUtility.GetNextExp(1, exp, LevelExp)` - exp remaining to next level
  - `ExpUtility.GetExpDifference(1, exp, LevelExp)` - total exp span for current level
  - Formula: `progress = 1.0 - (expToNext / expDifference)`
- Old formula `exp % 100` was incorrect - assumed flat 100 exp per level
- Fixed in `StatusDetailsReader.ReadWeaponSkill` and `BattleResultPatches.CalculatePercentInLevel`
- Weapon levels use `BattleUtility.GetSkillLevel` (unchanged, already correct)

### Shop: Unaffordable Items Not Announced
- Affordable items trigger `SetFocus(true)` - works correctly
- Unaffordable items never receive `SetFocus(true)` - game deliberately skips this
- Visual cursor moves but no focus event fires
- **Workaround**: Scroll past or earn more gil

### Keyword Ask/Learn Submenus Not Reading - FIXED
- Original issue: Reflection-based data access didn't work with IL2CPP
- Fixed by using pointer offsets to access `wordDataList` at 0x60, `itemDataList` at 0x68
- Added patches for `SelectContentByWord` (Ask/Learn) and `SelectContentByItem` (Key Items)
- Format: "keyword: description" or just "keyword" if no description available
- Also fixed: Command menu uses `interrupt: false` to avoid cutting off NPC intro dialogue

### Words Menu (Main Menu Key Terms) Description Not Reading - KNOWN ISSUE
- **Status**: Not working - reads keyword name only, not description
- **What works**: Keyword name is announced when navigating the Words menu from main menu
- **What doesn't work**: Description is not being read despite multiple implementation attempts
- **Attempted fixes**:
  1. Patching `UpdateFocus()` and reading from view's `DescriptionText` - text was empty
  2. Patching `SetDescriptionText(int index)` and reading view's `DescriptionText` property - still empty
  3. Using `keyWordContentDictionary` with `Content.MesIdDescription` via `MessageManager` - returns null
- **Technical notes**:
  - `KeyInput.WordsContentListController` has `keyWordContentDictionary` at offset 0x38
  - `Content` class (Last.Data.Master) has `MesIdName` and `MesIdDescription` properties
  - Dictionary lookup and MessageManager resolution both attempted but description is empty
- **Workaround**: NPC dialogue Ask/Learn submenus work correctly and show descriptions
- **Note**: The Ask menu in NPC dialogue uses `SelectFieldContentData` from `wordDataList` which works correctly. The Words menu uses a different data structure (`keyWordContentDictionary` with `Content` objects) that doesn't seem to contain descriptions or the lookup is failing silently.

---

## Remaining To-Do

- [ ] Vehicle features testing (ship, airship, chocobo)
- [x] Keyword Ask/Learn submenu reading - DONE 2026-01-11
- [x] Keyword Key Items submenu reading - DONE 2026-01-11
- [ ] Words menu (main menu) description reading - KNOWN ISSUE (name works, description doesn't)
- [x] Battle encounter type messages (preemptive, back attack, etc.) - DONE 2026-01-11
- [x] Battle escape announcement - DONE 2026-01-11
- [x] Battle state flag clearing on flee/defeat - DONE 2026-01-11
- [x] Target selection backing out bug - DONE 2026-01-11
- [x] Main menu state reset for stuck flags - DONE 2026-01-11
- [x] Treasure box opened/unopened state - DONE 2026-01-12
- [x] Missing entity detection (springs, events, interactive objects) - DONE 2026-01-12
- [x] Entity scanner cycling stuck in All category - FIXED 2026-01-12
- [x] Pathfinding filter toggle (Shift+P/\) - DONE 2026-01-12
- [x] Battle item menu reading wrong item - FIXED 2026-01-12
- [x] Battle item usage announcing "Items" instead of item name - FIXED 2026-01-12
- [x] Shop command menu not reading after closing buy/sell - FIXED 2026-01-12

---

## Key Fixes Reference

### Treasure Box Opened State - FIXED 2026-01-12
**Problem**: Treasure boxes always showed `opened=False` even when opened.

**Root Cause**: Original code used generic reflection looking for `isOpened` property, but:
- The actual field is `private bool isOpen` (no 'd')
- Located at IL2CPP offset 0x159 in `FieldTresureBox` (from dump.cs:301492)

**Fix**:
1. Added `FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox` import
2. Use `TryCast<FieldTresureBox>()` before name-based detection
3. New `GetTreasureBoxOpenedState(FieldTresureBox)` method:
   - First tries reflection on private `isOpen` field
   - Falls back to IL2CPP pointer offset access at 0x159

**Announcement format**: "Treasure Chest" (opened) or "Treasure Chest" (unopened)

### Pathfinding Debug Logging - ADDED 2026-01-12
**Issue**: Pathfinding fails on Deist Cavern B2 (map 184) and possibly other dungeon floors.

**Logs added to `FieldNavigationHelper.FindPathTo()`**:
- Map dimensions
- Player world position and cell coordinates
- Target world position and cell coordinates
- Player layer (Unity layer) and Z offset
- Player collision state
- Each Z-layer search attempt result (0, 1, 2)
- Adjacent tile fallback attempts with direction names
- Final success/failure with path description

**Log prefixes**: `[Pathfinding]` for all pathfinding debug output.

### Missing Entity Detection - FIXED 2026-01-12
**Problem**: Springs, pendants, and other interactive objects were not detected in entity scanner.

**Root Causes**:
1. Skip filter incorrectly filtered out `"mapobject"` - this excluded `FieldMapObjectDefault` and `FieldMapObjectAnimDefault` entities (springs, interactive objects)
2. Skip filter incorrectly filtered out `"trigger"` - this excluded valid `EventTriggerEntity` objects
3. Missing `EventTriggerEntity` TryCast check
4. Missing `SavePointEventEntity` TryCast check
5. Missing `GetEntityNameFromProperty()` helper for name resolution

**Fixes applied to `EntityScanner.cs`**:
1. Removed `"mapobject"` and `"trigger"` from skip filter
2. Added `EventTriggerEntity` import and TryCast check (before NPC checks)
3. Added `SavePointEventEntity` import and TryCast check (before EventTrigger check)
4. Added `GetEntityNameFromProperty()` method - reads `PropertyEntity.Name` and resolves via MessageManager or formats underscore names
5. Added `FormatAssetNameAsReadable()` method - converts "recovery_spring" → "Recovery Spring"
6. Added debug logging for skipped entities: `[Entity] SKIP (visual/unidentified): TypeName / GameObjectName`
7. Updated `IInteractiveEntity` fallback to use `GetEntityNameFromProperty()`

**Log prefixes**:
- `[Entity] Processing:` - Every entity logged with type/name/position
- `[Entity] -> Event (TryCast):` - EventTriggerEntity detected
- `[Entity] -> SavePoint (TryCast):` - SavePointEventEntity detected
- `[Entity] -> Interactive (TryCast):` - IInteractiveEntity detected
- `[Entity] -> Generic (fallback):` - Unidentified entity included as Event
- `[Entity] SKIP (player):` - Player entity filtered
- `[Entity] SKIP (party member):` - Party member filtered
- `[Entity] SKIP (inactive):` - Inactive object filtered
- `[Entity] SKIP (visual/trigger):` - Visual effects or door triggers filtered

### Entity Scanner Cycling Fix - FIXED 2026-01-12
**Problem**: Entity scanner got stuck on one entity (e.g., pendant) when cycling in "All" category, but worked correctly in specific categories.

**Root Cause**: The `ApplyFilter()` method had restoration logic that tried to find and restore focus to the previously selected entity after re-sorting by distance. This interfered with normal cycling, especially when combined with periodic rescans (every 5 seconds).

**Fix in `EntityScanner.ApplyFilter()`**:
- Removed `FindEntityByIdentifier()` restoration logic
- Now just clamps index to valid bounds after filtering/sorting
- Entity cycling works correctly across all categories

**Related**: Removed unused `SaveSelectedEntityIdentifier()` tracking that was no longer needed.

### Entity Detection Improvements - FIXED 2026-01-12
**Problem**: Springs, pendants, and other interactive objects were being filtered out or not detected.

**Fixes**:
1. Changed "unidentified" entity handling from skip to include as generic Events
2. Added logging for ALL entities at start of `ConvertToNavigableEntity()` for debugging
3. Added logging for silent filters (player, party members, inactive objects)
4. Keep filtering OpenTrigger (door activation zones) since we have GotoMap exits
5. Keep filtering visual effects (pointin, tileanim, scroll, effect)

**Entity inclusion flow**:
- All entities logged with type/name/position
- Skip: player, party members, inactive, visual effects, opentrigger
- Include: Everything else as appropriate category or generic Event

### Pathfinding Filter Toggle - DONE 2026-01-12
**Feature**: Toggle pathfinding filter with Shift+P or Shift+\

**Behavior**:
- **Filter OFF (default)**: All entities in current category are shown, regardless of path accessibility
- **Filter ON**: Only entities with valid paths from player position are shown when cycling

**Implementation**:
- Preference saved between sessions (`PathfindingFilter` in MelonPreferences)
- Toggle announces "Pathfinding filter on/off"
- Log shows `Filter enabled: True/False` when cycling entities

**Code flow**:
- `NextEntity()`/`PreviousEntity()` check `pathfindingFilter.IsEnabled`
- If OFF: simple index increment/decrement
- If ON: loop through entities, skip those failing `PathfindingFilter.PassesFilter()`

### Pathfinding Debug Enhancements - ADDED 2026-01-12
**Additional logging in `FieldNavigationHelper.FindPathTo()`**:
- Cell bounds validation with early exit if out of bounds

**Additional logging in `PathfindingFilter.PassesFilter()`**:
- Entity layer and position for each entity checked

### Map Exit Resolution
- Use `PropertyGotoMap.MapId` for destination (not `TmeId` which is source map)
- MoveArea entities have no destination - categorize as Events

### Teleport
- Use `entityScanner.CurrentEntity` position, not player position
- Use `FieldPlayerController.fieldPlayer` directly (not reflection)

### Battle Result Timing
- Gil: Announce in `Show_Postfix`
- Weapon skills: Patch `ShowSkillLevelsInit` (Touch) or `ShowLevelUpAbilitysInit` (KeyInput)
- Stat gains: Patch `ShowStatusUpInit`, compare `BeforData.Parameter` vs `AfterData.Parameter`

### Battle State Management
State flags must be cleared when leaving battle or submenus:
1. **On scene load**: `OnSceneLoaded` clears battle flags if `IsInBattle` was true
2. **On main menu return**: `CursorNavigation_Postfix` detects main menu command bar and calls `ClearAllMenuStates()`
3. **On target selection exit**: `SetCursor_Postfix` clears `IsTargetSelectionActive` when command menu receives cursor

Key methods:
- `ClearAllMenuStates()`: Clears `IsInBattle` + all 13 menu state flags
- `IsAtMainMenuCommandBar()`: Checks cursor hierarchy for "CommandMenu"/"MainMenu" + verifies `MainMenuController.IsOpne`
- `AnyMenuStateActive()`: Fast check if any state flag is stuck

Battle message patches:
- `StartPreeMptiveMes_Postfix`: Announces encounter type via `BattlePlugManager.Instance().BattlePopPlug.GetResult()`
- `StartEscape_Postfix`: Announces "Party escaped!"

### Controller Variants
KeyInput and Touch controllers often have different method names:
- Touch: `ShowSkillLevelsInit`
- KeyInput: `ShowLevelUpAbilitysInit`
- Touch: `targetData` at offset 0x30
- KeyInput: `targetData` at offset 0x48

### Battle Magic Menu (In-Battle Spell Selection)
Uses `Il2CppSerial.FF2.UI.KeyInput.BattleQuantityAbilityInfomationController` (MP-based system).
Note: FF3 uses `BattleFrequencyAbilityInfomationController` (charge-based system).

Both Touch and KeyInput variants patched via `SelectContent(Cursor, WithinRangeType)` method.

Memory offsets for KeyInput.BattleQuantityAbilityInfomationController:
- `dataList` (List<OwnedAbility>) at 0x70
- `contentList` (List<BattleAbilityInfomationContentController>) at 0x78
- `selectedCursorIndex` at 0x88

Announcement format: "Spell Name, Level X (Y%), MP Z: Description"

Spell level reading:
- Gets `selectedBattlePlayerData` from controller at offset 0x28
- Looks up ability in character's `OwnedAbilityList` by ability ID
- Uses `OwnedAbility.SkillLevel` for accurate proficiency (1-16)
- Reads `CommonGauge.gaugeImage.fillAmount` for progress percentage (0-1 float)
- Offsets: `contentList` at 0x78, `commonGauge` at 0x38, `gaugeImage` at 0x18

Suppression: `BattleMagicMenuState.IsActive` checked in `CursorNavigation_Postfix`.
Auto-clears when controller is inactive or command menu returns to normal state.

### Magic Menu State Machine
Uses `Il2CppSerial.FF2.UI.KeyInput.AbilityWindowController` state machine:
- 0 = None, 1 = UseList (spell list), 2 = UseTarget (character target)
- 3 = Forget (spell list), 4 = Command (Use/Forget menu), 5 = Popup (yes/no)

**Key**: Each state has its own handler:
- Command(4): AbilityCommandController.UpdateFocus announces Use/Forget
- UseList(1)/Forget(3): AbilityContentListController.SetCursor announces spells
- UseTarget(2): AbilityUseContentListController.SetCursor announces character targets
- Popup(5): CommonPopup.UpdateFocus announces Yes/No

Memory offsets for KeyInput.AbilityContentListController:
- `contentList` at 0x50
- `targetCharacterData` at 0x78

Memory offsets for KeyInput.AbilityWindowController:
- `stateMachine` at 0x88

Memory offsets for KeyInput.AbilityCommandController:
- `contentList` at 0x48
- `selectCursor` at 0x58

Memory offsets for KeyInput.AbilityUseContentListController (target selection):
- `contentList` at 0x40
- `selectCursor` at 0x48

### Popup Dialogs
Uses `Il2CppLast.UI.KeyInput.CommonPopup`:
- Memory offsets: `selectCursor` at 0x68, `commandList` at 0x70
- CommonCommand has `Text` property for option text

---

## Decompiled Functions (docs/Scripts/)

### SkillLevelTarget Enum
```
0=Sword, 1=Knife, 2=Spear, 3=Axe, 4=Cane, 5=Bow, 6=Shield, 7=Wrestle, 8=PhysAvoid, 9=AbilityAvoid
```

### Growth Flow
```
Battle End → BattleResultProvider.Genelate() → StatusUpProvider.Execution()
  → ExecutionSkillUpWeapon() / ExecutionSkillUpMagic() / ExecutionParameterUp()
  → BattleResultCharacterData records changes
```

| Class | Function | Purpose |
|-------|----------|---------|
| StatusUpProvider | ExecutionSkillUpWeapon | Weapon skill growth |
| StatusUpProvider | ExecutionSkillUpMagic | Spell growth |
| BattleResultCharacterData | WeaponSkillLevelUp | Records weapon level-up |
| OwnedAbility | get_SkillLevel | Current spell level (1-16) |

---

## Battle State Bug Fixes (2026-01-11)

### 1. BattleTargetSelection Backing Out Bug - FIXED
**Problem**: When entering target selection during battle and backing out to the command menu, `IsTargetSelectionActive` remained true, suppressing command menu announcements.

**Fix**: Added explicit flag clearing in `SetCursor_Postfix` of `BattleCommandPatches.cs`:
```csharp
if (BattleTargetPatches.IsTargetSelectionActive)
{
    BattleTargetPatches.SetTargetSelectionActive(false);
}
```
When `SetCursor` is called on the command menu, we know we're back in command selection, so clear the target selection state.

### 2. Battle Exit Flag Not Cleared on Flee/Defeat - FIXED
**Problem**: `IsInBattle` flag was only cleared on victory (in `BattleResultPatches.Show_Postfix`). When fleeing or losing, the flag remained true, suppressing `MenuTextDiscovery` on the field.

**Fix**: Added battle state clearing in `OnSceneLoaded` in `FFII_ScreenReaderMod.cs`:
```csharp
if (IsInBattle)
{
    ClearBattleActive();
    BattleCommandState.ClearState();
    BattleTargetPatches.SetTargetSelectionActive(false);
    BattleCommandPatches.ResetTurnState();
    BattleCommandPatches.ResetCommandCursorState();
    BattleMagicMenuState.Reset();
    BattleItemMenuState.Reset();
    BattleMessagePatches.ResetState();
}
```
Any scene transition while in battle now clears all battle-related state flags.

### 3. Battle Messages (Encounter Type & Escape) - IMPLEMENTED
**Problem**: Battle messages like "Preemptive strike!", "Back attack!", and "Party escaped!" were not being announced.

**Fix**: Added patches in `BattleMessagePatches.cs`:

**Encounter Types** - Patch `BattleController.StartPreeMptiveMes`:
- Gets preemptive state via `BattlePlugManager.Instance().BattlePopPlug.GetResult()`
- Falls back to reflection on `BattleProgress.GetNowPreetive()` if BattlePopPlug is null
- Announces: "Preemptive strike!", "Back attack!", "Enemy preemptive!", "Enemy side attack!", "Side attack!"
- `BattlePopPlug.PreeMptiveState` enum: Non=-1, Normal=0, PreeMptive=1, BackAttack=2, EnemyPreeMptive=3, EnemySideAttack=4, SideAttack=5

**Escape Announcement** - Patch `BattleController.StartEscape`:
- Simply announces "Party escaped!" when escape begins

### 4. Main Menu State Reset - IMPLEMENTED
**Problem**: Even after battle ends (via flee, defeat, etc.), menu states could remain stuck, suppressing MenuTextDiscovery.

**Fix**: Added main menu detection in `CursorNavigation_Postfix`:
```csharp
if (FFII_ScreenReaderMod.IsInBattle || FFII_ScreenReaderMod.AnyMenuStateActive())
{
    if (IsAtMainMenuCommandBar(cursor))
    {
        FFII_ScreenReaderMod.ClearAllMenuStates();
    }
}
```

**`IsAtMainMenuCommandBar()`** helper checks:
1. Cursor's parent hierarchy for "CommandMenu", "MainMenu", "CommandContent", "CommandList"
2. Verifies `MainMenuController.IsOpne` is true (note: game has typo "IsOpne")
3. Returns false if "Battle" or "Target" found in hierarchy

**`ClearAllMenuStates()`** now clears:
- `IsInBattle` flag
- All battle state resets (turn state, command cursor, message state)
- All menu state flags (Equip, Item, Status, Magic, Config, Shop, BattleItem, BattleMagic, Keyword, Words, Popup)

### 5. Battle Item Menu Reading Wrong Item - FIXED 2026-01-12
**Problem**: Battle item menu was reading the wrong item when navigating.

**Root Cause**: Code used `FindObjectsOfType<BattleItemInfomationContentController>()` which returns objects in undefined order (not UI order). Using `cursorIndex` directly on this unordered array gave incorrect items.

**Fix in `BattleItemPatches.cs`**:
- Changed from `FindObjectsOfType` to direct pointer access of controller's `displayDataList` at offset 0xE0
- KeyInput.BattleItemInfomationController has `displayDataList` (List<ItemListContentData>) at 0xE0
- Index into `displayDataList[cursorIndex]` which is properly ordered
- Kept `IsFocus` fallback only if direct access fails

### 6. Battle Item Usage Announcing "Items" Instead of Item Name - FIXED 2026-01-12
**Problem**: When using an item in battle, it announced "Firion: Items" instead of "Firion: Phoenix Down".

**Root Cause**: `GetActionName()` in `BattleMessagePatches.cs` only checked `abilityList` and fell back to command name. It was missing the `itemList` check.

**Fix**: Added `itemList` check at the beginning of `GetActionName()` (matching FF3's approach):
```csharp
var itemList = battleActData.itemList;
if (itemList != null && itemList.Count > 0)
{
    var ownedItem = itemList[0];
    string itemName = GetItemName(ownedItem);
    if (!string.IsNullOrEmpty(itemName))
        return itemName;
}
```
Added `GetItemName()` helper to extract localized name from `OwnedItemData`.

### 7. Shop Command Menu Not Reading After Closing Buy/Sell - FIXED 2026-01-12
**Problem**: When backing out from buy/sell list to shop command menu (Buy/Sell/Equipment/Back), nothing was announced.

**Root Cause**: `ShopCommandReader.cs` existed but wasn't being called from `MenuTextDiscovery.TryAllStrategies()`.

**Fix in `MenuTextDiscovery.cs`**:
Added call to ShopCommandReader early in the strategy chain:
```csharp
// Strategy 1: Shop command menu (Buy/Sell/Equipment/Back)
menuText = ShopCommandReader.TryReadShopCommand(cursor.transform, cursor.Index);
if (menuText != null) return menuText;
```

`ShopCommandReader` uses cursor index to look up command from `ShopCommandMenuController.contentList` and translates `ShopCommandId` enum to text (Buy/Sell/Equipment/Back).
