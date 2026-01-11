# Debug Log & Implementation Progress

## Status Summary

| Feature | Status | Notes |
|---------|--------|-------|
| Menu Text Reading | Working | Generic cursor + MenuTextDiscovery |
| New Game Naming | Working | Character slots, name cycling |
| Dialogue | Working | NPC dialogue, fade/scroll messages |
| Field Navigation | Working | Entity scan, pathfinding, wall bump, focus preservation |
| Battle Commands | Working | Turn, command, target announcements |
| Battle Messages | Working | Damage, healing, status effects |
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
| Keyword System | Working | NPC dialogue (Ask/Learn/Key Items/Cancel), Words menu |
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

### FF2 Weapon Skill Formula
```
level = exp / 100 + 1
percentInLevel = exp % 100
```
Level-ups occur when exp crosses 100, 200, 300... (max level 16).

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

Commands: 0=Ask, 1=Remember, 2=Item, 3=Cancel

Pointer offsets: `stateMachine` at 0x20, `Current` at 0x10, `Tag` at 0x10

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

### Shop: Unaffordable Items Not Announced
- Affordable items trigger `SetFocus(true)` - works correctly
- Unaffordable items never receive `SetFocus(true)` - game deliberately skips this
- Visual cursor moves but no focus event fires
- **Workaround**: Scroll past or earn more gil

---

## Remaining To-Do

- [ ] Vehicle features testing (ship, airship, chocobo)
- [ ] Keyword system in-game testing

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

### Controller Variants
KeyInput and Touch controllers often have different method names:
- Touch: `ShowSkillLevelsInit`
- KeyInput: `ShowLevelUpAbilitysInit`
- Touch: `targetData` at offset 0x30
- KeyInput: `targetData` at offset 0x48

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
