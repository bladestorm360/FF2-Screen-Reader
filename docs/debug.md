# Implementation Details

## Architecture

```
MelonLoader (net6.0) + HarmonyLib → IL2CppInterop → Unity IL2CPP → Tolk.dll → NVDA/JAWS
```

### IL2CPP Access Patterns
- Use properties directly (`__instance.targetData`), not `AccessTools.Field`
- Use direct property access (`playerController.fieldPlayer`) instead of reflection
- Patch both Touch and KeyInput controller variants
- Wrap all IL2CPP calls in try-catch

### Active State Pattern
```csharp
public static class MenuState {
    public static bool IsActive { get; private set; }
    public static void SetActive() {
        FFII_ScreenReaderMod.ClearOtherMenuStates("MenuName");
        IsActive = true;
    }
    public static bool ShouldSuppress() => IsActive;
    public static void ClearState() { IsActive = false; }
}
```

## Memory Offsets

### Status Screen (UI Reading)
```
SkillLevelContentController:
  view: 0x18, weaponType: 0x20

SkillLevelContentView:
  LevelText: property, CommonGauge: property

CommonGauge:
  gaugeImage: 0x18

StatusDetailsController (KeyInput):
  skillLevelContentList: 0x80

ParameterContentController:
  type: 0x18, view: 0x20

ParameterContentView:
  multipliedValueText: 0x28
```

### Battle System
```
BattleItemInfomationController (KeyInput):
  displayDataList: 0xE0

BattleQuantityAbilityInfomationController:
  dataList: 0x70, contentList: 0x78, selectedCursorIndex: 0x88

BattleAbilityInfomationContentController:
  commonGauge: 0x38
```

### Menu Controllers
```
ItemWindowController.stateMachine: 0x70
EquipmentWindowController.stateMachine: 0x60
AbilityWindowController.stateMachine: 0x88
ShopController.stateMachine: 0x90

AbilityContentListController:
  contentList: 0x50, targetCharacterData: 0x78

AbilityCommandController:
  contentList: 0x48, selectCursor: 0x58
```

### Popup System
```
CommonPopup: selectCursor: 0x68, commandList: 0x70
SavePopup: messageText: 0x40, selectCursor: 0x58, commandList: 0x60
ChangeMagicStonePopup: cmdList: 0x58
GameOverSelectPopup: cmdList: 0x40
```

### Keyword System
```
SecretWordControllerBase:
  stateMachine: 0x20, selectContentCursor: 0x30
  wordDataList: 0x60, itemDataList: 0x68

SelectFieldContentData:
  NameMessageId: 0x18, DescriptionMessageId: 0x20

WordsContentListController (KeyInput):
  contentList: 0x28, selectCursor: 0x30, keyWordContentDictionary: 0x38
```

### Vehicle/Transportation
```
TransportationController.infoData: 0x18
Transportation.modelList: 0x18
TransportationInfo: MapObject: 0x28, Enable: 0x38, Type: 0x5C

TransportationType: None=0, Player=1, Ship=2, Plane=3, Symbol=4, Content=5,
                    Submarine=6, LowFlying=7, SpecialPlane=8, YellowChocobo=9, BlackChocobo=10
```

### Battle Pause
```
BattleUIManager.pauseController: 0x90
BattlePauseController.isActivePauseMenu: 0x71
```

## Battle Result Phases

| Phase | Method | Data |
|-------|--------|------|
| 1 | Show_Postfix | Gil |
| 2 | ShowSkillLevelsInit / ShowLevelUpAbilitysInit | Weapon skills |
| 3 | ShowStatusUpInit | Stat gains |
| 4 | ShowGetItemsInit | Item drops |

## Spell/Skill Calculations

**Spell Level**: `ExpUtility.GetExpLevel(1, rawExp, ExpTableType.LevelExp)` - NOT `rawExp/100+1`

**Progress Gauge**: `1.0 - (ExpUtility.GetNextExp() / ExpUtility.GetExpDifference())`

**Weapon Skill Level**: `BattleUtility.GetSkillLevel(charData, skillTarget)`

## Key Discoveries

### Weapon Skill UI Order vs Enum
UI displays in different order than SkillLevelTarget enum. Solution: Use list index directly from `skillLevelContentList` (index 0=Sword, 1=Knife, etc.)

### Accuracy Count
`ConfirmedAccuracyCount()` returns 0 for players (BaseAccuracyCount never initialized). Count comes from equipped weapons, read from UI or use direct API.

### FF2 Vehicle Quirk
`GetOn(TRANSPORT_PLAYER=1)` called when disembarking, NOT `GetOff()`. Handle typeId==1 as disembark.

### Controller Variants
KeyInput and Touch often have different method names:
- Touch: `ShowSkillLevelsInit` / KeyInput: `ShowLevelUpAbilitysInit`
- Touch: targetData at 0x30 / KeyInput: targetData at 0x48

## Field Navigation Hotkeys

| Key | Action |
|-----|--------|
| J/[ | Previous entity (Shift: prev category) |
| K | Repeat current |
| L/] | Next entity (Shift: next category) |
| P/\ | Pathfinding (Shift: toggle filter) |
| V | Current vehicle/movement mode |
| 0 | Reset to All category |
| =/- | Cycle category |

## Status Details Hotkeys

| Key | Action |
|-----|--------|
| Up/Down | Navigate stats |
| Ctrl+Up/Down | Jump to top/bottom |
| Shift+Up/Down | Jump between groups |
| R | Repeat current stat |

## Bug Fixes Reference

### Battle Pause Menu (2026-01-22)
**Problem**: Spacebar pause menu not announcing commands.
**Cause**: Early `IsInBattleUIContext()` check blocked all cursor nav during battle.
**Fix**: Check cursor path for `"curosr_parent"` before battle suppression.

### New Game Naming Crash (2026-01-22)
**Problem**: Crash on InputPopup/CommonPopup.
**Cause**: Duplicate `Popup.Close` patches causing double `PopupState.Clear()`.
**Fix**: Removed `TryPatchPopupClose()` duplicate.

### Magic Menu Reading Wrong Commands (2026-01-22)
**Problem**: Arrow keys on Use/Forget menu read spells instead.
**Fix**: State machine check as PRIMARY gate in `SetCursor_Postfix`; suppress for COMMAND state.

### Entity Scanner Stuck (2026-01-12)
**Problem**: Cycling stuck on one entity in All category.
**Cause**: `ApplyFilter()` restoration logic interfered with cycling.
**Fix**: Removed `FindEntityByIdentifier()` restoration, just clamp index.

### Battle Item Wrong Item (2026-01-12)
**Problem**: Reading wrong item when navigating.
**Cause**: `FindObjectsOfType` returns undefined order.
**Fix**: Direct pointer access of `displayDataList` at offset 0xE0.

### Map Transition Detection (2026-01-21)
**Problem**: Entity list stale after map change.
**Fix**: Added `CheckMapTransition()` in `OnUpdate()`, calls `ForceRescan()` on map ID change.

### Duplicate Map Announcements (2026-01-21)
**Problem**: Both "Entering X" and fade message "X" announced.
**Fix**: Created `LocationMessageTracker` for cross-source deduplication.

### Popup Button Before Message (2026-01-22)
**Problem**: Popup buttons read before message (e.g., "Yes. Learn the selected spell?").
**Cause**: `UpdateFocus` fires synchronously; message uses 1-frame delayed coroutine.
**Fix**: Added `PopupState.PopupJustOpened` flag; skip initial button read in `CommonPopup_UpdateFocus_Postfix`.

### Shop Command Menu Not Reading (2026-01-22)
**Problem**: Command bar (Buy/Sell/Equipment/Back) not announced when navigating.
**Cause**: `OFFSET_SHOP_CONTROLLER` was `0x90` (Touch version) but KeyInput version uses `0x98`.
**Fix**: Changed offset to `0x98` in `StateReaderHelper`. Now `ShouldSuppress()` correctly detects command bar state and lets generic cursor handle it via `ShopCommandReader`.
