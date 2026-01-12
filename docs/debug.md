# Debug Log & Implementation Progress

## Status Summary

| Feature | Status | Notes |
|---------|--------|-------|
| Menu Text Reading | Working | Generic cursor + MenuTextDiscovery |
| New Game Naming | Working | Character slots, name cycling |
| Dialogue | Working | NPC dialogue, fade/scroll messages |
| Field Navigation | Working | Entity scan, pathfinding, wall bump, focus preservation |
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
Uses `BattleUtility.GetSkillLevel(OwnedCharacterData, SkillLevelTarget)` for accurate level calculation.
This is the game's internal method for determining weapon skill levels.

For battle results:
- `BattleResultCharacterData.GrouthWeaponSkillList` contains skills that leveled up
- Uses `BattleUtility.GetSkillLevel(charResult.AfterData, skillTarget)` for accurate level

Max level is 16. Percentage progress uses `exp % 100` formula.

**Important**: Evasion skills (PhysicalAvoidance, AbilityAvoidance) are NOT weapon skills - they only have stat gains, no exp bars. They are filtered out of weapon skill announcements.

### FF2 Spell Level Storage
**STATUS: FIXED**

`OwnedAbility.SkillLevel` stores raw exp, NOT the display level.
Formula from decompiled `BattleUtility.GetSkillLevel`: `level = (rawExp / 100) + 1`, clamped to 1-16.

`SkillLevelProvider.SettingSkillLevel` uses the same formula internally to display level in UI.

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

### Spell Level Calculation - FIXED
- `OwnedAbility.SkillLevel` stores raw exp, not actual level
- Formula discovered from decompiled `BattleUtility.GetSkillLevel`: `level = (rawExp / 100) + 1`
- Fixed in `MagicMenuPatches.GetSpellProficiency`, `BattleMagicPatches`, and `BattleResultPatches`
- Decompiled functions in `docs/Scripts/decompiled_skill_level.c`

### Weapon Skill Progress Percentages - FIXED
- Weapon skill levels announce correctly using `BattleUtility.GetSkillLevel`
- Progress percentage formula: `exp % 100` (each level requires 100 exp)
- Fixed in `StatusDetailsReader.ReadWeaponSkill` and `BattleResultPatches.AnnounceWeaponSkillProgress`
- Old incorrect formula was `(exp % 10) * 10` assuming 10 exp per level

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

---

## Key Fixes Reference

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
