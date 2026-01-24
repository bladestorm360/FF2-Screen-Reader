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

### Shop Command Menu Not Reading (2026-01-22)
**Problem**: Command bar (Buy/Sell/Equipment/Back) not announced when navigating.
**Cause**: `OFFSET_SHOP_CONTROLLER` was `0x90` (Touch version) but KeyInput version uses `0x98`.
**Fix**: Changed offset to `0x98` in `StateReaderHelper`. Now `ShouldSuppress()` correctly detects command bar state and lets generic cursor handle it via `ShopCommandReader`.

### Menus Not Reading After New Game (2026-01-23)
**Problem**: After starting a new game, all menus silently failed to read (cursor navigation suppressed).
**Cause**: `IsInBattle` flag could get stuck if battle ended via defeat/flee/scripted sequence rather than victory screen. `ClearBattleActive()` was only called in `BattleResultPatches.Show_Postfix` (victory) and scene transitions.
**Fix**: Created `GameStatePatches` to hook `SubSceneManagerMainGame.ChangeState` - event-driven approach that clears battle state when transitioning to field states (FieldReady, Player, ChangeMap). Also handles map transition announcements without per-frame polling.

### Event-Driven Map Transitions (2026-01-23)
**Problem**: `CheckMapTransition()` in `OnUpdate()` violated the "no polling" rule in CLAUDE.md.
**Fix**: Moved map transition logic to `GameStatePatches.ChangeState_Postfix`. The game's state machine fires `ChangeState` on all major transitions (field, battle, menu, etc.), providing a reliable event-driven hook. Applied to FF2, FF3, and FF4.

### Popup Button Reading Issues (2026-01-23)
**Problem**: Two issues with popup button reading:
1. "Return to Title" popup: "No" button interrupted the popup message text
2. Load menu popup: First navigation key spoke nothing, second worked normally
**Cause**: `PopupJustOpened` flag introduced to prevent early button reads had multiple code paths checking/clearing it, causing race conditions. Flag was cleared on first button-read attempt but returned without reading, then subsequent reads worked.
**Fix**: Removed `PopupJustOpened` flag entirely. Aligned with FF3's simpler approach:
- `CursorNavigation_Postfix` routes ALL popup button reading through `PopupPatches.ReadCurrentButton()`
- `CommonPopup_UpdateFocus_Postfix` uses `lastAnnouncedButtonIndex` for duplicate prevention only
- Reset `lastAnnouncedButtonIndex` in `HandlePopupDetected()` when popup opens
Files modified: `PopupPatches.cs`, `BattlePausePatches.cs`, `SaveLoadPatches.cs`, `FFII_ScreenReaderMod.cs`.

### Popup Button Duplicate Announcements (2026-01-23)
**Problem**: Popup buttons announced twice when navigating (e.g., "Yes" spoken twice on New Game → Return to Title popup).
**Cause**: Two code paths both fired for popup button reading outside of battle:
1. `CursorNavigation_Postfix` → `PopupPatches.ReadCurrentButton()` (correct path outside battle)
2. `BattlePausePatches.CommonPopup_UpdateFocus_Postfix` (no battle guard, fired for ALL popups)
The `CommonPopup.UpdateFocus` patch was intended for battle context only (since `CursorNavigation_Postfix` returns early when `IsInBattleUIContext()` is true), but had no guard to enforce this.
**Fix**: Added `IsInBattleUIContext()` guard to `CommonPopup_UpdateFocus_Postfix`. Now:
- Outside battle: Only `CursorNavigation_Postfix` handles popup buttons
- During battle: Only `CommonPopup.UpdateFocus` patch handles popup buttons (since cursor nav exits early)

### CommonPopup Buttons Not Reading (2026-01-23)
**Problem**: CommonPopup Yes/No buttons not reading when navigating outside of battle.
**Cause**: The `IsInBattleUIContext()` guard added to fix duplicates blocked `CommonPopup_UpdateFocus_Postfix` outside battle, relying on `CursorNavigation_Postfix` fallback. The fallback path failed when popup detection didn't set `PopupState` correctly.
**Fix**: Removed `IsInBattleUIContext()` guard from `CommonPopup_UpdateFocus_Postfix` to align with FF3. Now handles ALL popup button reading directly via `UpdateFocus` patch, with `lastAnnouncedButtonIndex` preventing duplicates.

## Paginated Dialogue System (2026-01-23, updated 2026-01-24)

Ported from FF4 screen reader. Announces dialogue page-by-page as player advances, instead of all pages at once.
Updated to support multi-line pages (ported from FF1 screen reader).

### Architecture

```
SetContent → Read messageList + newPageLineList from instance via pointer access
SetSpeker → Store speaker in DialogueTracker.currentSpeaker
PlayingInit → Get currentPageNumber, combine lines for page, announce
Close → Reset DialogueTracker state
```

### Memory Offsets (MessageWindowManager)

```
messageList: 0x88           // List<string> - all dialogue lines
newPageLineList: 0xA0       // List<int> - ending line index per page
spekerValue: 0xA8           // string - speaker name
currentPageNumber: 0xF8     // int - current page being displayed
```

### Key Classes

**DialogueTracker** (MessageWindowPatches.cs):
- `currentMessageList` - All dialogue lines (one per visual line)
- `currentPageBreaks` - Page start indices (converted from ending indices)
- `lastAnnouncedPageIndex` - Prevents duplicate announcements
- `currentSpeaker` / `lastAnnouncedSpeaker` - Speaker deduplication
- `GetPageText(pageIndex)` - Combines lines within page boundaries

**LineFadeMessageTracker** (ScrollMessagePatches.cs):
- `storedMessages[]` - Array of auto-scroll lines
- `currentLineIndex` - Line tracking for per-line announcement

### Multi-Line Page Support

The game stores dialogue as individual display lines in `messageList`, with `newPageLineList` indicating where pages end.

Example:
- `messageList` = ["This is", "a long", "sentence.", "Page two."]
- `newPageLineList` = [2] (page 0 ends at line 2)
- Page 0 text = "This is a long sentence."
- Page 1 text = "Page two."

`GetPageText(pageIndex)`:
1. Looks up start/end line indices for the page
2. Concatenates all lines with spaces
3. Returns combined text

### API Hooks

| Method | Purpose |
|--------|---------|
| `MessageWindowManager.SetContent` | Reads messageList + newPageLineList via pointer |
| `MessageWindowManager.SetSpeker` | Stores speaker for "Speaker: text" format |
| `MessageWindowManager.PlayingInit` | Gets currentPageNumber, announces combined page |
| `MessageWindowManager.Close` | Resets tracker state |
| `LineFadeMessageWindowController.SetData` | Stores lines for per-line announcement |
| `LineFadeMessageWindowController.PlayInit` | Announces next line (fires per line) |

### Pointer-Based Access

Uses IL2CPP pointer access instead of reflection for reliability:
```csharp
IntPtr listPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_MESSAGE_LIST);
var il2cppList = new Il2CppSystem.Collections.Generic.List<string>(listPtr);
```
