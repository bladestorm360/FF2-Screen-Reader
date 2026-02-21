# Implementation Details

## Architecture

```
MelonLoader (net6.0) + HarmonyLib → IL2CppInterop → Unity IL2CPP → Tolk.dll → NVDA/JAWS
```

### File Structure
```
Core/
├── FFII_ScreenReaderMod.cs    Main entry point, OnUpdate, input routing (~1,224 lines)
├── PreferencesManager.cs      MelonPreferences entries, volume/toggle properties
├── AudioLoopManager.cs        Wall tone + beacon coroutine lifecycle
├── GameInfoAnnouncer.cs       Gil, map name, character status announcements
├── WaypointController.cs      Waypoint CRUD, pathfinding, category cycling
├── CursorSuppressionCheck.cs  Centralized menu state suppression routing
├── InputManager.cs            Hotkey bindings and input handling
├── ModMenu.cs                 In-game settings menu
├── Filters/                   Entity scan filters (pathfinding, map exits, ToLayer)
├── ConfirmationDialog.cs      Windows API Yes/No dialog
├── TextInputWindow.cs         Windows API text input dialog
├── WaypointManager.cs         Waypoint persistence (JSON)
└── WaypointNavigator.cs       Waypoint list cycling and formatting

Patches/                       Harmony patches (one per game system, 24 files)
Menus/                         Menu readers (CharacterSelection, StatusDetails)
Field/                         Entity scanning, pathfinding, map names

Utils/
├── IL2CppOffsets.cs           Centralized memory offsets (~90 constants)
├── HarmonyPatchHelper.cs      Reusable patch registration (SetActive, PatchMethod, etc.)
├── StateReaderHelper.cs       State machine pointer reading
├── Helpers.cs                 PlayerPosition, CharacterUtility, Collection, Direction helpers
├── TolkWrapper.cs             Screen reader TTS wrapper
├── SoundPlayer.cs             Procedural audio (waveOut API)
├── MoveStateHelper.cs         Vehicle/foot movement state tracking
├── EntityTranslator.cs        Japanese→English name translation
├── LocalizationUtility.cs     Game localization helpers
├── GameObjectCache.cs         IL2CPP component caching
├── MenuStateHelper.cs         Boilerplate reducer for state classes (~83 lines)
└── AnnouncementDeduplicator.cs Announcement dedup across systems
```

### IL2CPP Access Patterns
- Use properties directly (`__instance.targetData`), not `AccessTools.Field`
- Use direct property access (`playerController.fieldPlayer`) instead of reflection
- Patch both Touch and KeyInput controller variants
- Wrap all IL2CPP calls in try-catch

### Active State Pattern (MenuStateHelper)

All 13 state classes use `MenuStateHelper` to eliminate IsActive/SetActive/Reset boilerplate.
Custom logic (ShouldSuppress, helper methods, extra fields) stays in each state class.

```csharp
public static class MenuState {
    private static readonly MenuStateHelper _helper = new(MenuStateRegistry.KEY, dedup_contexts...);
    static MenuState() { _helper.RegisterResetHandler(() => { /* extra cleanup */ }); }
    public static bool IsActive => _helper.IsActive;
    public static void SetActive() => _helper.SetActiveExclusive();
    public static void ClearState() => _helper.IsActive = false;  // triggers registered handler
    public static bool ShouldSuppress() { /* custom validation logic */ }
}
```

## Memory Offsets

All IL2CPP memory offsets are centralized in `Utils/IL2CppOffsets.cs` (~90 constants organized by subsystem). The offsets below are reference documentation.

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

### Damage Source Tracking Pattern
Status effect damage (poison, etc.) uses a tracking pattern:
1. Prefix hook on damage generator (e.g., `GeneratePoisonDamage`) sets `pendingDamageSource`
2. `CreateDamageView` postfix consumes source and includes in announcement
3. Postfix hook clears source if not consumed (e.g., target died)

```csharp
// In BattleMessagePatches.cs
private static string pendingDamageSource = null;
public static void SetDamageSource(string source) => pendingDamageSource = source;
public static string ConsumeDamageSource() { var s = pendingDamageSource; pendingDamageSource = null; return s; }
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
GameOverSelectPopup: selectCursor: 0x38, commandList: 0x40
GameOverLoadPopup: titleText: 0x38, messageText: 0x40, selectCursor: 0x58, commandList: 0x60
GameOverPopupController: view: 0x30
GameOverPopupView: loadPopup: 0x18
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
| Shift+K | Reset to All category |
| =/- | Cycle category |
| 0 | Dump untranslated entity names |
| ; | Toggle wall tones |
| ' | Toggle footsteps |
| 9 | Toggle audio beacons |

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

### Poison Damage Source Not Announcing (2026-01-31)
**Problem**: Poison damage announced same as regular damage ("Firion: 5 damage") instead of identifying the source ("Poison: Firion: 5 damage").
**Cause**: `AccessTools.TypeByName("Last.Battle.PoisonConditionFunction")` returned null - IL2CPP types need the `Il2Cpp` prefix.
**Fix**: Changed to `"Il2CppLast.Battle.PoisonConditionFunction"`. Pattern: All IL2CPP types accessed via `AccessTools.TypeByName()` require `Il2Cpp` prefix (e.g., `"Il2CppLast.UI.Touch.ResultMenuController"`).

### Item Quantity Not Announced (2026-01-31)
**Problem**: Items announced as "Item Name: Description" without quantity count.
**Fix**: Added quantity to announcements in `ItemMenuPatches.cs` and `BattleItemPatches.cs`. Format: "Item Name (x): Description" when quantity > 1.
**Data source**: `ItemListContentData.Count` property.

### GameOverLoadPopup Yes/No Buttons Silent (2026-01-31)
**Problem**: Yes/No buttons on "Start from recent save data?" popup not announced when navigating with arrow keys.
**Cause**: Only `UpdateCommand` was patched, but cursor navigation may trigger `UpdateFocus` instead.
**Fix**: Also patched `GameOverLoadPopup.UpdateFocus` using the same postfix handler (`GameOverLoadPopup_UpdateCommand_Postfix`). Both methods read cursor position and announce button text identically.

### MP Recovery Reported as HP (2026-01-31)
**Problem**: All recovery announced as "Recovered X HP" even for MP recovery (Ether, etc.).
**Cause**: `BattleUtility.CreateDamageView` only has `isRecovery` bool, no HP/MP distinction.
**Fix**: Added patch for `BattleBasicFunction.CreateDamageView` which has `HitType` parameter:
- `HitType.Recovery = 4` (HP recovery)
- `HitType.MPRecovery = 6` (MP recovery)
Deduplication prevents both patches from announcing the same event.

### Duplicate HP/MP Recovery Announcements (2026-01-31)
**Problem**: Ether use announced both "Recovered 20 HP" and "Recovered 20 MP" - should only announce MP.
**Cause**: Two patches fired for recovery events with different dedupe keys:
1. `CreateDamageViewWithHitType_Postfix` (BattleBasicFunction) - has HitType.MPRecovery, correctly said "MP"
2. `CreateDamageViewUtility_Postfix` (BattleUtility) - only had `isRecovery` bool, defaulted to "HP"
Deduplication failed because keys differed: `"Firion:20:mp"` vs `"Firion:20:hp"`.
**Fix**: Added early return in `CreateDamageViewUtility_Postfix` for `isRecovery=true`. Now `CreateDamageViewWithHitType_Postfix` handles all recovery since it has the HitType parameter for HP/MP distinction.

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

## Code Audit & Release Prep (2026-02-07)

### Logging Cleanup
- Removed all `MelonLogger.Msg()` calls (29 total)
- Removed `MelonLogger.Warning()` from gameplay catch blocks (105 blocks → `catch { }`)
- Upgraded `MelonLogger.Warning()` in Apply/TryPatch methods to `MelonLogger.Error()` (56 calls)
- Only `MelonLogger.Error()` remains (126 calls for critical failures + broken patches)

### Dead Code Removal
- Deleted `Utils/SpeechHelper.cs` (zero callers, 30 lines)
- Removed EntityScanner debug fields: `loggedEntityTypes`, `vehicleDebugEnabled`, `entityDebugEnabled`, `entityDebugCount`, `MAX_DEBUG_ENTITIES`
- Removed EntityScanner debug if-blocks in `ConvertToNavigableEntity()`
- Removed empty try/catch debug block from `PathfindingFilter.cs`

### File Consolidation
- Created `Utils/Helpers.cs` containing 4 static classes:
  - `PlayerPositionHelper` (from `Utils/PlayerPositionHelper.cs` - deleted)
  - `CharacterUtility` (from `Utils/CharacterUtility.cs` - deleted)
  - `CollectionHelper` (from `Utils/CollectionHelper.cs` - deleted)
  - `DirectionHelper` (extracted from duplicated methods in `NavigableEntity` and `WaypointEntity`)
- `ToLayerFilter` now implements `IEntityFilter` interface with `FilterTiming.OnAdd`

## Entity Scanner Filtering (2026-02-05)

Filters out placeholder entities from the scanner to reduce clutter:

### ToLayer Events
Added `tolayer` to visual effects filter in `ConvertToNavigableEntity()`. These are internal layer transition triggers, not player-facing events.

### Filters (applied to all maps)

**Vehicle spawn points** (Japanese names - not actual vehicles):
- 飛竜 (Wyvern) - uses StartsWith to match `飛竜(sc_e_0080用)` etc.
- 飛空艇 (Airship) - exact match
- 大戦艦 (Dreadnought) - exact match

**Location markers** (redundant with map exits):
- Towns: アルテア, ガテア, パルム, ポフト, サラマンド, バフスク, フィン, ミシディア, ディスト, ジェイド, パンデモニウム, ガデアの村
- Castles/dungeons: カシュオーン城, ディストの城, パラメキア城, フィン城, ミシディアの塔, 闘技場
- Zone markers: ミシディアA, ミシディアB, ミシディアC

**Pattern filters**:
- `所持して通行すると発生` (item-trigger events)
- `に行けない` (access restrictions)
- `エフェクト` + `仮置き` (effect placeholders)

**Kept (not filtered)**:
- Collision zones (コリジョン) - may trigger battles
- Ferry/定期船 - valid transport point

### Debug Logging Fix (2026-02-05)
**Problem**: 20 unfiltered events on overworld, only Wyvern NPC visible.
**Root cause (from debug logs)**:
1. Wyvern NPC raw name is `飛竜(sc_e_0080用)` - exact match `飛竜` didn't catch it. Fixed with StartsWith.
2. Castle/town/dungeon names (カシュオーン城, フィン城, etc.) appeared as Interactive objects via `FieldMapObjectDefault` - these duplicate map exits. Added to exact filters.
3. Zone markers (ミシディアA/B/C) not useful. Added to exact filters.
4. Collision zones (コリジョン) kept - may trigger battles.
5. Ferry (定期船) kept - valid transport point.

### Filter Flow (Fixed 2026-02-05)
**Problem**: Filters contained Japanese names but received English (translated) names.
**Root cause**: Translation happened BEFORE filtering - `GetEntityNameFromProperty()` called `MessageManager.GetMessage()` which returns the game's localized text (English when game is in English). Then `EntityTranslator.Translate()` was also applied, making filtering on Japanese names impossible.
**Solution**: New tuple-returning methods that separate raw and display names:
```csharp
// GetEntityNameWithRaw() and GetNpcDisplayNameWithRaw() return both:
var (rawName, displayName) = GetEntityNameWithRaw(fieldEntity);
// rawName = property.Name (original Japanese/message ID for filtering)
// displayName = translated/localized for speech

if (IsPlaceholderEntity(rawName))  // Filter on raw
    return null;
return new EventEntity(..., displayName, ...);  // Use translated for display
```

### Filter Types (2026-02-05)
`IsPlaceholderEntity()` uses three filter strategies on `property.Name` (raw Japanese), before any translation:
1. **Exact match** - for known names (towns, castles, vehicles)
2. **StartsWith** - for names with variable suffixes (e.g. `飛竜` matches `飛竜(sc_e_0080用)`)
3. **Contains** - for pattern matching (event descriptions, goName patterns)

### Files Modified
- `Field/MapNameResolver.cs` - Added `IsOverworldMap()` method
- `Field/EntityScanner.cs` - Added `IsPlaceholderEntity()`, filtering integration, cache field; fixed filter-before-translate flow

## External Sound Player (2026-01-27)

Ported from FF1 screen reader. Replaces game's `AudioManager.PlaySe(4)` wall bump with procedural tones via Windows waveOut API.

### Architecture
```
SoundPlayer.cs (Utils/) - waveOut P/Invoke, 4 channels, procedural tone generation
├── Channel 0: Movement (footsteps)
├── Channel 1: WallBump (thud)
├── Channel 2: WallTone (looping directional tones)
└── Channel 3: Beacon (panning pings)
```

### Audio Features
- **Wall bumps**: Procedural thud (27Hz, 60ms) on collision detection via coroutine
- **Wall tones**: Looping directional tones (N=330Hz, S=110Hz, E=220Hz, W=200Hz) with constant-power stereo panning
- **Footsteps**: Click sound (500Hz noise burst, 25ms) on tile change
- **Audio beacons**: Panning ping (400Hz north / 280Hz south) toward selected entity every 2s

### Movement Sound Patches
- Patches `FieldPlayerKeyController.OnTouchPadCallback` (prefix)
- Coroutine waits 0.08s, compares position before/after
- Wall bump: requires 2+ consecutive hits at same position, 300ms cooldown
- Footstep: on tile change, gated by `IsFootstepsEnabled()`, 150ms cooldown

### Wall Tone Loop
- 100ms coroutine interval
- Uses `FieldNavigationHelper.GetNearbyWallsWithDistance()` + `MapRouteSearcher.Search()`
- Suppresses at map exits via `EntityScanner.GetMapExitPositions()` + `IsDirectionNearMapExit()`
- Suppresses during screen fades via `MapTransitionPatches.IsScreenFading`
- Suppresses 1s after map transitions via `wallToneSuppressedUntil`

### Map ID for FF2
Uses `UserDataManager.Instance().CurrentMapId` (not FF1's `FieldMapProvisionInformation.Instance.CurrentMapId`)

## Entity Name Translation (2026-01-27)

Ported from FF1 screen reader. Translates Japanese entity names to English using a JSON dictionary.

### Architecture
```
EntityTranslator.cs (Utils/)
├── Initialize() - loads FF2_translations.json from UserData/FFII_ScreenReader/
├── Translate(name) - exact match → prefix-stripped match → track untranslated
└── DumpUntranslatedNames() - writes EntityNames.json grouped by map
```

### Translation Lookup
1. Exact match in translations dictionary (includes circled number prefixes like ①②③)
2. Strip numeric/SC prefix (e.g., "6:" or "SC01:"), lookup base name
3. No match → track as untranslated for current map (if contains Japanese characters)

### Circled Number Prefixes (2026-02-05)
The game uses circled Unicode numbers (①②③...⑫) to distinguish multiple instances of the same NPC type. These are NOT stripped by `EntityPrefixRegex` (which only matches `^((?:SC)?\d+:)` patterns).

**Solution**: Added 119 prefixed variants directly to FF2_translations.json for exact-match lookup:
- Generic NPCs: ①子供（金髪）, ②女性（青服）, etc.
- Shop NPCs: ①道具屋（青いおやじ）, etc.
- Soldiers: ①パラメキア兵, ①帝国兵, ①兵士（白い帽子、青マント）, etc.
- Named characters: ①ヒルダ, ②ゴードン, ⑩シド, ⑪ボーゲン, etc.
- Creatures: ①ジャイアントビーバー, ①レッドソウル, etc.

All prefixed entries map to the same English translation as the base name.

### Integration Points
- `EntityScanner.GetEntityNameFromProperty()` - 2 return points wrapped with `Translate()`
- `EntityScanner.GetNpcDisplayName()` - 2 return points wrapped with `Translate()`
- `InputManager.HandleGlobalInput()` - `0` key triggers `DumpUntranslatedNames()`

### Files
- `FFII_translations.json` (project root) - translation dictionary source (version controlled)
- `UserData/FFII_ScreenReader/FF2_translations.json` - runtime copy (~209 translations including prefixed variants)
- `UserData/FFII_ScreenReader/EntityNames.json` - dumped untranslated names by map

### Hotkey
| Key | Action |
|-----|--------|
| `0` | Dump untranslated entity names for current map |

### Preferences
Saved to MelonLoader prefs: WallTones, Footsteps, AudioBeacons (all default false)

## Game Over Screen Accessibility (2026-01-31)

Ported from FF1 screen reader. Announces game over screen elements including defeat message, button navigation, and load popup.

### Patches

| Hook | Purpose |
|------|---------|
| `BattleCommandMessageController.SetMessage` | Announces "The party was defeated" with interrupt:true |
| `GameOverSelectPopup.UpdateCommand` | Announces Load/Return to Title button on navigation |
| `GameOverLoadPopup.UpdateCommand` | Announces Yes/No button on navigation |
| `GameOverLoadPopup.UpdateFocus` | Also announces Yes/No button (cursor nav may use either method) |
| `GameOverPopupController.InitSaveLoadPopup` | Starts delayed read of "Start from recent save data?" message |

### Flow
```
Party Defeated → SetMessage("The party was defeated") → announce with interrupt
             → GameOverSelectPopup.Open() → announce "Game Over"
             → Navigate buttons → UpdateCommand → announce button text
             → Select Load → InitSaveLoadPopup → delayed read of message
             → Navigate Yes/No → UpdateCommand → announce button text
```

## Confirmation Dialog System (2026-02-04)

Custom Yes/No confirmation dialog using Windows API focus stealing. Used for waypoint deletion confirmations.

### Architecture
```
ConfirmationDialog.cs (Core/)
├── Open(prompt, onYes, onNo, silentYes) - Opens dialog, optionally in silent mode
├── HandleInput() - Processes Y/N/Enter/Escape/Arrow keys
├── StealFocus() - Creates invisible window to capture keyboard
└── RestoreFocus() - Returns focus to game
```

### Silent Yes Mode

The `silentYes` parameter enables seamless chained confirmations:
- When `true`: Yes confirmation invokes callback immediately without "Yes" announcement or focus window
- When `false`: Normal flow with "Yes" speech and delayed callback

Used by `WaypointClearAll()` for 2-step confirmation:
```
First prompt (silentYes: true): "Clear all X waypoints?"
  → Yes → Immediately opens second prompt (no "Yes" announcement)
  → No → "Cancelled"

Second prompt (normal): "Are you sure?"
  → Yes → Clears waypoints, announces count
  → No → "Cancelled"
```

### Key Handling
| Key | Action |
|-----|--------|
| Y | Confirm Yes immediately |
| N | Confirm No immediately |
| Enter | Confirm current selection (Yes/No) |
| Escape | Cancel (same as No) |
| Left/Right | Toggle selection, announce |
