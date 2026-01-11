# FF3 to FF2 Porting Plan

This document outlines the remaining features to port from `ff3-screen-reader` to `ff2-screen-reader`, excluding job-related code (FF2 has no job system).

---

## Priority 1: Active State Check Pattern (Critical) - DONE

FF3 has a centralized menu state management pattern that prevents duplicate announcements and ensures proper cursor suppression. This is **missing from FF2** and causes issues.

### Current FF2 Problem
- Cursor suppression only checks `BattleTargetPatches.IsTargetSelectionActive` and equipment menu hierarchy
- No centralized state tracking for all menus
- No fallback to reset states when returning to main menu
- Can result in stuck suppression flags

### FF3 Pattern to Port

#### 1. Add State Tracking Classes
Each menu patch file needs a companion `*State` class:
```csharp
public static class ItemMenuState
{
    public static bool IsActive { get; set; } = false;
    public static bool ShouldSuppress() { ... }  // Validates controller still active
    public static void ClearState() { ... }
}
```

#### 2. Add to FFII_ScreenReaderMod.cs

```csharp
// Check all menu states
private static bool AnyMenuStateActive()
{
    return Patches.EquipMenuState.IsActive ||
           Patches.ItemMenuState.IsActive ||
           Patches.StatusMenuState.IsActive ||
           Patches.MagicMenuState.IsActive ||
           Patches.ConfigMenuState.IsActive ||
           Patches.BattleCommandState.IsActive ||
           Patches.BattleItemMenuState.IsActive ||
           Patches.BattleMagicMenuState.IsActive;
}

// Reset when returning to main menu
public static void ClearAllMenuStates()
{
    Patches.EquipMenuState.ClearState();
    Patches.ItemMenuState.ClearState();
    Patches.StatusMenuState.ResetState();
    Patches.MagicMenuState.ResetState();
    Patches.ConfigMenuState.ResetState();
    Patches.BattleItemMenuState.Reset();
    Patches.BattleMagicMenuState.Reset();
}

// Clear other states when entering a specific menu
public static void ClearOtherMenuStates(string exceptMenu)
{
    if (exceptMenu != "Equip") Patches.EquipMenuState.ClearState();
    if (exceptMenu != "Item") Patches.ItemMenuState.ClearState();
    if (exceptMenu != "Status") Patches.StatusMenuState.ResetState();
    if (exceptMenu != "Magic") Patches.MagicMenuState.ResetState();
    if (exceptMenu != "Config") Patches.ConfigMenuState.ResetState();
    if (exceptMenu != "BattleItem") Patches.BattleItemMenuState.Reset();
    if (exceptMenu != "BattleMagic") Patches.BattleMagicMenuState.Reset();
}
```

#### 3. Update CursorNavigation_Postfix

```csharp
public static void CursorNavigation_Postfix(object __instance)
{
    // CRITICAL: If any menu was active but we're now at main menu level, clear all states
    if (AnyMenuStateActive() && IsAtMainMenuLevel())
    {
        ClearAllMenuStates();
    }

    // Check each menu's ShouldSuppress() - if true, that menu handles announcements
    if (Patches.BattleTargetPatches.IsTargetSelectionActive) return;
    if (Patches.ItemMenuState.ShouldSuppress()) return;
    if (Patches.EquipMenuState.ShouldSuppress()) return;
    if (Patches.StatusMenuState.ShouldSuppress()) return;
    if (Patches.MagicMenuState.ShouldSuppress()) return;
    if (Patches.ConfigMenuState.ShouldSuppress()) return;
    if (Patches.BattleItemMenuState.ShouldSuppress()) return;
    if (Patches.BattleMagicMenuState.ShouldSuppress()) return;

    // Default: use MenuTextDiscovery
    CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(...));
}
```

### Files to Create/Modify
- [x] `Core/FFII_ScreenReaderMod.cs` - Add state management methods
- [x] `Patches/EquipMenuPatches.cs` - Enhanced `EquipMenuState` with `IsActive`, `ShouldSuppress()`, `ClearState()`
- [x] `Patches/BattleCommandPatches.cs` - Added `BattleCommandState` class, `ShouldSuppress()` to `BattleTargetPatches`
- [ ] Other patch files - Add `*State` companion class as they're ported

---

## Priority 2: Map Name Resolution - DONE

### What to Port
Copy `Field/MapNameResolver.cs` from FF3 with namespace changes.

### FF3 Implementation
```csharp
public static class MapNameResolver
{
    public static string GetCurrentMapName()
    {
        var userDataManager = UserDataManager.Instance();
        int currentMapId = userDataManager.CurrentMapId;
        return TryResolveMapNameById(currentMapId);
    }

    public static string TryResolveMapNameById(int mapId)
    {
        var masterManager = MasterManager.Instance;
        var mapList = masterManager.GetList<Map>();
        var map = mapList[mapId];
        int areaId = map.AreaId;
        var areaList = masterManager.GetList<Area>();
        var area = areaList[areaId];

        string areaName = messageManager.GetMessage(area.AreaName, false);
        string mapTitle = messageManager.GetMessage(map.MapTitle, false);
        // Or use map.Floor for "1F", "B1" format

        return $"{areaName} {mapTitle}";
    }
}
```

### Integration Points
- [x] `Field/MapNameResolver.cs` - Create new file
- [x] `Core/FFII_ScreenReaderMod.cs` - Update `AnnounceCurrentMap()` to use MapNameResolver
- [x] `Field/EntityScanner.cs` - Update `GetMapExitDestination()` to use MapNameResolver

---

## Priority 2.5: Entity Focus Preservation (Field Navigation)

### Problem
When entity list re-sorts (by distance after player moves), FF2 loses focus on the currently selected entity. The index stays the same but points to a different entity.

### FF3 Fix to Port
FF3's `EntityScanner.cs` tracks selected entity by identifier and restores focus after re-sorting.

#### Fields to Add
```csharp
// Track selected entity by identifier to maintain focus across re-sorts
private Vector3? selectedEntityPosition = null;
private EntityCategory? selectedEntityCategory = null;
private string selectedEntityName = null;
```

#### Methods to Add
```csharp
private void SaveSelectedEntityIdentifier()
{
    var entity = CurrentEntity;
    if (entity != null)
    {
        selectedEntityPosition = entity.Position;
        selectedEntityCategory = entity.Category;
        selectedEntityName = entity.Name;
    }
}

public void ClearSelectedEntityIdentifier()
{
    selectedEntityPosition = null;
    selectedEntityCategory = null;
    selectedEntityName = null;
}

private int FindEntityByIdentifier()
{
    if (!selectedEntityPosition.HasValue || !selectedEntityCategory.HasValue)
        return -1;

    // Match by position (0.5f tolerance) and category
    for (int i = 0; i < filteredEntities.Count; i++)
    {
        var entity = filteredEntities[i];
        if (entity.Category == selectedEntityCategory.Value &&
            Vector3.Distance(entity.Position, selectedEntityPosition.Value) < 0.5f)
            return i;
    }

    // Fallback: match by name
    if (!string.IsNullOrEmpty(selectedEntityName))
    {
        for (int i = 0; i < filteredEntities.Count; i++)
        {
            if (filteredEntities[i].Category == selectedEntityCategory.Value &&
                filteredEntities[i].Name == selectedEntityName)
                return i;
        }
    }
    return -1;
}
```

#### Integration Points
1. **CurrentIndex setter**: Call `SaveSelectedEntityIdentifier()` when index changes
2. **ApplyFilter()**: After re-sorting, call `FindEntityByIdentifier()` to restore focus
3. **NextEntity()/PreviousEntity()**: Call `SaveSelectedEntityIdentifier()` after moving
4. **CurrentCategory setter**: Call `ClearSelectedEntityIdentifier()` when changing category

### Files to Modify
- [x] `Field/EntityScanner.cs` - Add focus preservation pattern from FF3

---

## Priority 3: Menu Patches to Port - DONE

### Item Menu (`Patches/ItemMenuPatches.cs`) ✓
Ported from FF3:
- `ItemMenuState` class with state machine reading (offset 0x70)
- `ItemListController_SelectContent_Patch` - Item list navigation
- `ItemUseController_SelectContent_Patch` - Character target selection with MP
- State values: NONE=0, COMMAND=1, USE_LIST=2, KEY_ITEMS=3, SORT=4, TARGET=5

### Status Menu (`Patches/StatusMenuPatches.cs`) ✓
Ported from FF3, excluding job-related code:
- `StatusMenuState` class with state machine
- `StatusWindowController_SelectContent_Patch` - Character selection
- Character stats (HP, MP, row, conditions)
- **Removed**: Job name/level announcements

### Magic Menu (`Patches/MagicMenuPatches.cs`) ✓
Ported with FF2 adaptations:
- `MagicMenuState` class with `IsActive`, `ShouldSuppress()`, `ResetState()`
- Spell selection with proficiency level (1-16) and MP cost
- Uses `UseValue` property for MP cost (not `ConsumeMp`)
- Format: "Spell Name, Level X, MP cost Y: Description"

### Config Menu (`Patches/ConfigMenuPatches.cs`) ✓
Ported:
- `ConfigMenuState` class
- `ConfigMenuReader` helper class
- Config option announcements with current values

### Shop Menu (`Patches/ShopPatches.cs`) ✓
Ported:
- `ShopMenuTracker` with `IsActive`, `ShouldSuppress()`, `ResetState()`
- Shop item selection with prices and stats
- Quantity selection with total price
- Weapon/Armor stat announcements

### Battle Item/Magic (`Patches/BattleItemPatches.cs`, `Patches/BattleMagicPatches.cs`) ✓
Ported from FF3:
- `BattleItemMenuState` / `BattleMagicMenuState`
- Item/spell selection during battle
- State machine validation to prevent stuck flags
- Battle magic uses `UseValue` for MP cost

---

## Priority 4: Menu Readers - DONE

### Character Selection Reader (`Menus/CharacterSelectionReader.cs`) ✓
Ported for detecting character selection in:
- Status menu
- Equipment menu
- Item target selection
- Formation menu
- **Format**: "Name, Row, Level X, HP current/max, MP current/max"

### Shop Command Reader (`Menus/ShopCommandReader.cs`) ✓
Ported for shop command bar detection (Buy/Sell/Equipment/Back).

### Status Details Reader (`Menus/StatusDetailsReader.cs`) ✓
Ported for detailed stat navigation:
- `ReadStatusDetails()` - Name, Level, HP, MP
- `ReadPhysicalStats()` - Strength, Vitality, Defense, Evade
- `ReadMagicalStats()` - Magic, Magic Defense, Magic Evade
- `ReadAttributes()` - Strength, Agility, Stamina, Intelligence, Spirit
- `ReadCombatStats()` - Attack, Accuracy, Defense, Evasion, Magic Defense, Magic Evasion
- **Removed**: Job-related stat sections

---

## Priority 5: Vehicle/Movement Features - DONE

### Movement Speech Patches (`Patches/MovementSpeechPatches.cs`) ✓
Ported vehicle state announcements:
- Walking/Running
- Ship/Canoe/Airship states
- "On foot" when disembarking

### Vehicle Landing Patches (`Patches/VehicleLandingPatches.cs`) ✓
Ported "Can land" detection.

### Move State Helper (`Utils/MoveStateHelper.cs`) ✓
Ported vehicle/movement state detection utilities.

---

## Priority 6: Additional Features

### Title Menu Patches (`Patches/TitleMenuPatches.cs`)
Port title screen navigation (New Game, Continue, Options).

### Popup Patches (`Patches/PopupPatches.cs`)
Port popup message announcements.
**Note**: FF3 disabled this due to crashes - test carefully in FF2.

### Item Details Announcer - NOT NEEDED
FF2 has no job/class restrictions - all characters can equip all items. No 'I' key functionality required.

---

## Files Summary

### Create New Files
| File | Priority | Status | Notes |
|------|----------|--------|-------|
| `Field/MapNameResolver.cs` | P2 | ✓ DONE | Direct port with namespace change |
| `Patches/ItemMenuPatches.cs` | P3 | ✓ DONE | Port with FF2 namespace, MP system |
| `Patches/StatusMenuPatches.cs` | P3 | ✓ DONE | Remove job references, add MP |
| `Patches/MagicMenuPatches.cs` | P3 | ✓ DONE | FF2 MP system, UseValue for cost |
| `Patches/ConfigMenuPatches.cs` | P3 | ✓ DONE | Direct port |
| `Patches/ShopPatches.cs` | P3 | ✓ DONE | Direct port with stats |
| `Patches/BattleItemPatches.cs` | P3 | ✓ DONE | Direct port |
| `Patches/BattleMagicPatches.cs` | P3 | ✓ DONE | FF2 MP system, UseValue for cost |
| `Menus/CharacterSelectionReader.cs` | P4 | ✓ DONE | With MP, no jobs |
| `Menus/ShopCommandReader.cs` | P4 | ✓ DONE | Direct port |
| `Menus/StatusDetailsReader.cs` | P4 | ✓ DONE | Remove job stats |
| `Patches/MovementSpeechPatches.cs` | P5 | ✓ DONE | Ported with FF2 vehicles |
| `Patches/VehicleLandingPatches.cs` | P5 | ✓ DONE | Ported |
| `Utils/MoveStateHelper.cs` | P5 | ✓ DONE | Ported with FF2 vehicles |
| `Patches/TitleMenuPatches.cs` | P6 | ✓ DONE | Title screen navigation working |
| `Patches/PopupPatches.cs` | P6 | Pending | Not implemented, needs testing |
| `Patches/ItemDetailsAnnouncer.cs` | P6 | NOT NEEDED | FF2 has no equip restrictions |

### Modify Existing Files
| File | Changes | Status |
|------|---------|--------|
| `Core/FFII_ScreenReaderMod.cs` | Add ClearAllMenuStates, ClearOtherMenuStates, AnyMenuStateActive, register all patches | ✓ DONE |
| `Field/EntityScanner.cs` | Add focus preservation (P2.5), use MapNameResolver (P2) | ✓ DONE |
| `Patches/EquipMenuPatches.cs` | Add proper EquipMenuState class | ✓ DONE |
| `Patches/BattleCommandPatches.cs` | Add BattleCommandState class | ✓ DONE |

---

## DO NOT Port (Job-Related)

- `Patches/JobMenuPatches.cs` - Entire file (job selection)
- `JobMenuState` class
- Job level announcements in status menu
- Job requirements in item details
- Job name resolution helpers

---

## FF2-Specific Considerations

### Magic System Differences
- FF3: Spell charges per level (no MP cost visible)
- FF2: MP cost system, spells have 16 proficiency levels

### Stat System Differences
- FF3: Traditional XP-based leveling
- FF2: Usage-based stat growth (weapon skills, magic levels, HP/MP from damage)

### Battle Result Differences
- FF3: XP, job points, level-ups
- FF2: Gil, weapon skill level-ups, magic level-ups, character level-ups
- **Note**: No need to announce detailed stat changes (HP, Str, etc.) - StatusDetailsReader handles viewing stats manually. Only announce what's visually shown on victory screen.

---

## Testing Checklist

After porting each feature:
- [ ] Build succeeds
- [ ] Mod loads without crashes
- [ ] Feature works as expected
- [ ] No duplicate announcements
- [ ] State properly resets when backing out to main menu
- [ ] No stuck suppression flags
