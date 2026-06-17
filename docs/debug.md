# Implementation Details

## FF1-parity pass (2026-06-16)

- **Unreachable directions → "No path"** (`AnnounceCurrentEntity`): speaks `pathInfo.Description` or
  bare `T("No path")` (no entity-name repeat); no-player-position → `T("Cannot determine directions")`.
  Mirrors FF1. Beacon-vs-announce lives in the `\`/`P` key lambdas + `ControllerRouter`
  (`RestartEntityBeacon` re-speaks the destination only when AnnounceOnBeaconRestart is on).
- **Pathfinding filter "No reachable entities"**: `EntityScanner.NoReachableEntities()` (port of FF1)
  + `CycleNext`/`CyclePrevious` announce `T("No reachable entities")` when the filter is on and
  nothing in the category is reachable — instead of staying on/announcing an unreachable entity
  (Castle Fynn Treasure Chests). Root cause: `MapRouteSearcher` only reads the static tile-collision
  grid, not object colliders (a fence is a `FieldMapObjectDefault` BoxCollider2D), so fence-blocked
  chests are correctly No-path; the cycle skip's "stay-put when none reachable" fallback was the bug.
- **Keybinding FF1 parity** (`InputManager`): beacon toggle `Alpha9`→`F6` (field-gated; removed the
  `9` toggle); added `P`+Ctrl = layer filter; `\`/`P` are beacon-aware (re-ping when beacon on, else
  announce); removed the Status-screen `R` override (R = global repeat-dialogue); removed the `Alpha0`
  debug dump + `EntityTranslator.DumpUntranslatedNames` + its JSON helpers (dead code).
- **Manual rescan ` key** (`BackQuote`): `ForceEntityRescan()` rescans + speaks "Entity scan
  complete" (or "Entity scanner not available"); map transitions use the new silent
  `RescanEntitiesSilent()`.
- **Escape dedup**: removed hardcoded `StartEscape_Postfix` ("Party escaped!"); the game's on-screen
  escape text is read once via `SetMessage_Postfix`.
- **Fence translation generalized**: `TrailingDigitsRegex` now matches full-width digits
  (`[0-9０-９]+$`) and `StripTrailingDigits` normalizes the suffix to ASCII, so a single `柵`→"Fence"
  entry yields "Fence N" for every `柵N` (per-fence `柵３`/`柵５` entries removed).
- **Entity scanning parity (audited, already aligned)**: live delta scan (remove-gone, prune-dead via
  `IsAlive`, convert-new every cycle), event-driven map-transition rescan (`ChangeState_Postfix`), no
  OnEventEnd/chest/dialogue rescan hooks. FF1's `IsEntityOnCurrentMap` map-asset filter intentionally
  NOT ported — FF2's `GetAllFieldEntities` reads the current `FieldController.entityList` (already
  current-map-scoped), so the extra per-scan check would be redundant.

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
`skillLevelContentList` is NOT in `SkillLevelTarget` enum order, and the order is not reliable —
do NOT key by list index (a prior positional guess mislabeled skills, e.g. sword read as spear).
Each element is a `SkillLevelContentController` carrying its own type: `weaponType`
(`SkillLevelTarget`) @0x20 (the level/gauge `view` is @0x18). `StatusDetailsReader.CacheWeaponSkillsFromUI`
reads `weaponType` per element and keys `weaponSkillCache` by it — order-independent, matches how
`BattleResultPatches.GetWeaponSkillName` works off the enum directly.

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

### Confirmation popup announces focused choice on open (2026-06-16)
**Change**: When a `CommonPopup` (generic confirm) or `ChangeMagicStonePopup` (spell learn) opens,
the initially-focused button (Yes/No) is now announced together with the message, e.g. "Would you
like to learn this spell? Yes" — previously only the message read on open and the choice read only
on the first arrow.
**How**: `PopupState` gained `SelectCursorOffset` (per-type select-cursor offset, set in `SetActive`)
and `LastButtonIndex`. `PopupOpen_Postfix` passes the cursor offset (CommonPopup 0x68 /
ChangeMagicStonePopup 0x50; −1 for button-less and GameOver popups, which are unaffected).
`DelayedPopupRead` reads the LIVE `selectCursor.Index` (not a hard 0, so a "No"-default popup says
"No") and appends that button via the existing `ReadButtonFromCommandList`. `ReadCurrentButton`
gained a `LastButtonIndex` guard so a stray cursor event for the same focus right after open cannot
double the choice (preserves fix #3's single-read behavior). Offsets: `CommonPopup`/`ChangeMagicStonePopup`
both extend `Popup` (dump.cs:457709 / 457506).

### Item-use target list re-reads top character on confirm (2026-06-16)
**Problem**: When using an item on a character (incl. the Tome → learn-spell flow), confirming a
target re-announced the TOP character (index 0, e.g. "Firion") right before the learn popup —
"Firion" (entry) → "Maria" (navigate) → confirm → "Firion" again.
**Cause**: `ItemUseController_SelectContent_Postfix` (patches
`KeyInput.ItemUseController.SelectContent`) reads the character at the cursor index. It fires on
entry, on every cursor move (navigation), AND re-fires on confirm with the cursor reset to index 0.
This re-read always happened but was previously MASKED by the learn popup's `CommonPopup.UpdateFocus`
reader speaking "Yes" (interrupt:true) on confirm — it became audible only after that reader was
made to defer (see "Spell-learn popup choices read twice"). A value-dedup can't help — the re-read
is a *different* character (index 0) than the confirmed one.
**Failed first attempt**: gated on the OUTER `ItemWindowController` state via `ItemMenuState.GetState()`
+ `PopupState.IsConfirmationPopupActive`. No-op: the outer state stays `TARGET_SELECT` for the whole
flow (confirm included) and the popup opens a frame *after* the re-read, so the gate never fired.
**Fix**: Gate on the INNER `ItemUseController`'s OWN state machine (`IL2CppOffsets.ItemUse`,
stateMachine @ 0x70 / nextState @ 0x78). Announce only when `useState` is `Single`(1)/`All`(2) and
`nextState < LearningVerification`(3). Confirming a target transitions to a `Learning*` state and/or
sets `nextState` to `LearningVerification` synchronously before the re-read, so the spurious index-0
read is suppressed; entry/navigation stay in Single/All and are unaffected. The outer-vs-inner state
distinction is the key — only the inner state changes on confirm.

### Spell list initial focus silent on open (2026-06-16)
**Problem**: Opening the Magic Use/Forget list did not speak the initially-focused spell; the
list only read once the user arrowed.
**Cause**: `MagicMenuState.OnSpellListFocused()` (called from `UpdateController_Postfix`) sets the
active flag but never announces. `SetCursor_Postfix` is the only announcer, and it early-returns
while `IsSpellListActive` is false — which is set true only inside `UpdateController_Postfix`. The
game sets the cursor before the state reaches `USE_LIST`/`FORGET`, so the initial `SetCursor` is
gated out and no later one fires until navigation.
**Fix**: In `UpdateController_Postfix`, on first activation (after `OnSpellListFocused()`), read the
controller's `selectCursor` (`IL2CppOffsets.Magic.OFFSET_LIST_SELECT_CURSOR` = 0x38) and call the
existing `AnnounceSpellAtIndex`. Deduped by `ShouldAnnounceSpell(spellId)` so the follow-up
`SetCursor` for the same focus stays silent.

### Shop command bar "Buy" spoken then cut off on buy-list entry (2026-06-16)
**Problem**: Entering a shop / opening straight into the buy list spoke "Buy", then immediately
interrupted it with the focused item.
**Cause**: `ShopCommandMenuController.SetCursor` fires during shop init (cursor 0 = Buy) regardless
of where focus lands. On open into the buy list (state `SELECT_PRODUCT`), `CommandSetCursor_Postfix`
announced "Buy", then `SetDescription_Postfix` announced the item and cut it off. The item reader is
state-gated; the command reader was not.
**Fix**: Gate `CommandSetCursor_Postfix` on the `ShopController` state machine — only announce when
`ShopMenuTracker.GetState() == STATE_SELECT_COMMAND`. This is FF1's 1b6b1cf state-gate pattern (FF1
gates the item reader on states 2/3) applied to the command reader (the symmetric inverse).

### Spell-learn popup choices read twice (2026-06-16)
**Problem**: The spell-learn confirmation (`ChangeMagicStonePopup`) read "yes." before the message
on open, and each choice twice ("ye-yes"/"n-no") when arrowing.
**Cause**: Two readers fired out of battle — the global `CursorNavigation_Postfix` →
`ReadCurrentButton` (now that `PopupOpen_Postfix` registers `ChangeMagicStonePopup` in `PopupState`)
AND `BattlePausePatches.CommonPopup_UpdateFocus_Postfix` (patches `CommonPopup.UpdateFocus`, which
also catches the `CommonPopup` subclass). The battle guard removed on 2026-01-23 (see below) left
`UpdateFocus` reading everything; the global popup detection has since been built out, so both now
fire. Supersedes the 2026-01-23 "CommonPopup Buttons Not Reading" decision.
**Fix**: In `CommonPopup_UpdateFocus_Postfix`, defer to the global reader only when it is live for
this popup: `if (!IsInBattleUIContext() && PopupState.ShouldSuppress()) return;`. Safer than the
reverted 2026-01-23 battle guard — if `PopupState` isn't set, `UpdateFocus` still reads (preserves
that fallback); in battle (where cursor-nav exits early) it always reads.

### Controller field context poisoned by stale KEYWORD_MENU (2026-06-13)
**Problem**: On the field, after opening the NPC keyword menu (Ask/Learn), right-stick-down
announced the on-screen game controls instead of cycling field entities/pathfinding targets.
**Cause**: `ControllerRouter.IsFieldActive` = `context==Field && !MenuStateRegistry.AnyActive()
&& !IsInBattle`. The keyword menu (`SecretWordController`) sets `KEYWORD_MENU` via
`SetActiveExclusive`, but its only reliable clear is the lazy self-heal in
`KeywordMenuState.ShouldSuppress()`, which runs only via `CursorSuppressionCheck.Check()`
during menu cursor navigation — never on the field. So the flag leaked stuck-active, making
`IsFieldActive` false on the field and routing right-stick to `KeyHelpReader.AnnounceKeyHelp`.
The controller code was the first consumer to read `AnyActive()` on the field (keyboard nav
keys off `DetermineContext()`, which ignores menu flags), exposing the latent leak.
**Fix** (FF1 state-flag-checking pattern): in `ControllerRouter.Update()`, before computing
`IsFieldActive`, when `context==Field && !IsInBattle && AnyActive()`, invoke
`CursorSuppressionCheck.Check()` for its self-healing side effect so dead flags (keyword,
words, shop) clear via their `ShouldSuppress()`/`ValidateState()`. One-shot: a stuck flag
clears within ~1 frame, then `AnyActive()` short-circuits.

### Pause menu mis-detected as field for controller (2026-06-13)
**Problem**: Inside the game's pause menu, right-stick scanned entities and the D-pad was
consumed for waypoints (menu treated as field).
**Cause**: `MainMenuPatches` registered no menu state, so `IsFieldActive` stayed true while
the pause menu was open.
**Fix** (FF1 `MAIN_MENU` parent-container pattern): added `MenuStateRegistry.MAIN_MENU`,
preserved across submenu switches in `SetActiveExclusive` (so the whole pause menu reads as
non-field), set in `MainMenuController.Show` postfix and cleared in a new `Close` postfix.
Also added to `ClearMenuFlagsForMapTransition` as a backstop.

### Controller right-stick-up reads item details in menus (2026-06-13)
**Added** (FF1 parity): in `ControllerRouter.HandleNormalNonField`, right-stick-up now calls
`InputManager.HandleItemDetailsKey()` — the same dispatcher the keyboard "I" key uses (config
tooltip → shop → item/magic/equip detail). Made that method and its config-tooltip helpers
`internal static` so the router can call them. Right-stick-down still reads on-screen controls.
FF1's right-stick-left "usable-by" reader was deliberately NOT ported (FF2 has no equip restrictions).

### Teleport could fire from menus/battle — game-breaking (2026-06-13)
**Problem**: Mod-mode right-stick teleport (and keyboard Ctrl+Arrow) only guarded via
`GetFieldPlayer()`. A menu/battle overlay keeps the field loaded, so the player still exists and
teleport moved it underneath the menu (out of bounds / into events).
**Fix**: structural `if (!ControllerRouter.IsFieldActive) { speak "Not available here"; return; }`
guard at the top of `TeleportInDirection` (covers controller AND keyboard callers). Also
restructured `HandleModModeState` so field mod functions (Gil/Location/toggles/teleport) run only
under `else if (IsFieldActive)`; battle branch unchanged; menu/non-field does nothing field-specific.

### Item detail key reads live UI panel, not master data (2026-06-13)
**Problem**: The detail key (I / right-stick-up) built shop/equip stats from master-data lookups
(`ShopPatches.GetItemStats` → `Weapon.Attack`/`Armor.Defense`/…) concatenated with the description.
This excluded some displayed stats and wasn't panel-sensitive (read both stats + description).
**Fix** (FF1 pattern): read the single live UI `Text` the game fills with the active panel —
- Shop: `ShopInfoController.view (0x18) → ShopInfoView.descriptionText (0x38)` (`ShopDetailsAnnouncer`).
- Equip: `EquipmentDescriptionWindowController.view (0x20) → descriptionText (0x18)` (`EquipDetailsAnnouncer`).
Reading that field is inherently panel-sensitive and reads the full stats panel (the game renders
whichever panel is toggled into it). `InputManager.HandleItemDetailsKey` dispatch now matches FF1:
config → shop → equip → item → magic, each reading live UI. Offsets in `IL2CppOffsets.ShopInfo` /
`Equipment` (KeyInput variants — Touch variants lack these fields). The item menu renders stats into
row controllers, not a single field, so `ItemDetailsAnnouncer` reads the live
`ItemWindowView.descriptionText (0x18)` for description and the visible `Text` under
`ItemEquipmentDetailView` for the stats panel, chosen by `isFrontTextVisible (0x48)` (logs both for
verification). FF1 got the same item-menu live-read fix; FF1 shop/equip were already correct.

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
- `IsInDialogue` / `RepeatLastDialogue()` - Re-speaks the last announced page (with
  speaker prefix). Bound to the **R key** (ported from FF1) via a `KeyContext.Global`
  binding in `InputManager` (`HandleRepeatDialogueKey`, silent when no window is open).
  R is also bound in `KeyContext.Status` (repeat current stat); the registry prefers
  the more-specific Status binding on the status screen, so the two never collide.
  - **Controller**: mod mode (Back/Select) + **West (X)** repeats the dialogue. In
    `ControllerRouter.HandleModModeState`, a `DialogueTracker.IsInDialogue` block runs
    *before* the battle/field branches (field dialogue still reads as `IsFieldActive`,
    so West would otherwise announce Gil). `AnnounceModModeControls` gains a matching
    dialogue branch so RB/right-stick-down describes the repeat control.

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

### Map Exit Filter dedup (2026-06-14, FF1 parity)
The "Map Exit Filter" toggle (`Shift+M` / mod menu) now actually groups exits.
`EntityScanner.ApplyFilter()` calls `DeduplicateMapExits()` when
`PreferencesManager.MapExitFilterEnabled` — after the distance sort, before the ToLayer filter —
keeping only the **closest** exit per `MapExitEntity.DestinationMapId` (`> 0`). Unresolved exits
(`DestinationMapId <= 0`) are kept individually so distinct unnamed exits aren't merged. This
collapses a town's dozen-plus world-map border exits (all resolve to world map id `1` via
`PropertyGotoMap.MapId`) into one. `ToggleMapExitFilter()` calls `entityScanner.ReapplyFilter()`
so the change applies immediately without re-entering the map. Previously the toggle only flipped
a bool and saved the pref — `ApplyFilter` never referenced it, so it did nothing.

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
- **Wall tones**: Looping directional tones (N=294Hz, S=165Hz, E=220Hz, W=196Hz) with constant-power stereo panning
- **Footsteps**: Click sound (500Hz noise burst, 25ms) on tile change
- **Audio beacons**: Panning ping (400Hz north / 280Hz south) toward selected entity every 2s

### Movement Sound Patches
- **Wall bumps** — patches `FieldPlayerKeyController.OnTouchPadCallback` (prefix); coroutine
  waits 0.08s, compares position before/after; requires 2+ consecutive hits at same position,
  300ms cooldown. On a confirmed bump sets `collisionDetectedThisFrame = true` so the footstep
  poll skips the coincident frame.
- **Footsteps (2026-06-14, FF1 parity)** — moved OUT of the wall-bump coroutine into a per-frame
  `MovementSoundPatches.PollFootsteps()` called from `InputManager.CheckInput()` (right after
  `ControllerRouter.Update`). Plays one step per live tile change, so cadence naturally tracks
  movement speed (walk slower than dash) — **no fixed cooldown** (the old 150ms `FOOTSTEP_COOLDOWN`
  capped it at ~6.6/s and decoupled it from real speed). Gated by `ControllerRouter.IsFieldActive`,
  `FFII_ScreenReaderMod.Instance.IsFootstepsEnabled()`, and `MoveStateHelper.IsOnFoot()` (silent in
  vehicles/menus/battle). Not a new per-frame Harmony patch — it rides the existing input loop.

### Wall Tone Loop
- 100ms coroutine interval
- Uses `FieldNavigationHelper.GetNearbyWallsWithDistance()` + `MapRouteSearcher.Search()`
- Suppresses at map exits via `EntityScanner.GetMapExitPositions()` + `IsDirectionNearMapExit()`
- Suppresses during screen fades via `MapTransitionPatches.IsScreenFading`
- Suppresses 1s after map transitions via `wallToneSuppressedUntil`
- **Gating (2026-06-14, FF1 parity)** — `WallToneLoop`/`BeaconLoop` gate their `while` on the
  mod's **local** flag (`mod.IsWallTonesEnabled()` / `IsAudioBeaconsEnabled()`), and
  `StartWallToneLoop`/`StartBeaconLoop` early-out on the same. Previously they read the **saved
  preference** (`PreferencesManager.WallTonesEnabled`), but `ToggleWallTones` calls Start
  *before* `SaveToggle`, and `CoroutineManager.StartManaged` → `MelonCoroutines.Start` runs the
  coroutine synchronously to its first `yield` — so the loop read the stale `false` and exited
  immediately, leaving wall tones (and beacons) permanently silent after every toggle-on.
- **Seamless mixed loop (2026-06-14)** — `PlayWallTonesLooped` builds the loop via
  `ToneGenerator.GenerateMixedLoopTone(specs, SUSTAIN_DURATION_MS)`: one buffer, all frequencies
  snapped to a whole number of cycles over the shared length so the *sum* is exactly periodic, with
  a single uniform `1/sqrt(n)` headroom. waveOut loops the whole buffer (`WHDR_BEGINLOOP/ENDLOOP`,
  `dwLoops=0xFFFFFFFF`), so the previous approach — `GenerateStereoTone(sustain:true)` per direction
  (each cycle-aligned to a *different* length) then `MixWavFiles` (sized to the longest, `1/sqrt`
  headroom only where tones overlap) — clicked once per loop when 2+ walls were adjacent: the
  shorter tone had a silent tail gap and the longer tone's tail jumped ~3 dB. Single-tone playback
  goes through the same path now (still seamless). `MixWavFiles` remains for the one-shot
  (`PlayWallTones`) path, whose attack/decay envelope already fades to zero at both ends.

### Map ID for FF2
Uses `UserDataManager.Instance().CurrentMapId` (not FF1's `FieldMapProvisionInformation.Instance.CurrentMapId`)

## Layer-aware pathfinding + `[NavDiag]` (2026-06-14)

FFPR maps are 2D tile maps with stacked collision layers (multi-floor dungeons). The native
`MapRouteSearcher` is layer-aware: `MapParameter.MappingData = List<int[,]>` (one collision grid per
layer), `LayerLength`, a 3D `int[layer,x,y]` route map, 64-cell bounded search. A cell's `z` =
`gameObject.layer - 9`. Crossing a layer is automatic as the player walks over a transition tile.

**Bug (confirmed via `[NavDiag]`):** the *Magic Circle* (layer 9, `z=349`) sits directly above the
player + *Revival Room* (layer 10, `z=249`) at the same X/Y `(0,0)`. `FindPathTo` set the start
cell's Z from the player's layer but **brute-forced the destination Z (`for tryDestZ = 2..0`) and
took the first hit** — the Magic Circle's X/Y is walkable on the player's own layer 10 (`z=1`), so
that search succeeded and routed horizontally on layer 10 to the target's X/Y projection; it never
tried the target's real layer 9 (`z=0`). Result: bogus "North N" toward a spot that isn't the
target. The 8-tile adjacency fallback also fabricated a "North 1" when the player stood on a target.

**Fix:**
- `FindPathTo(..., int? targetLayer = null)` — when the target's real Unity layer is supplied, set
  `destCell.z = targetLayer - 9` and search **that layer only** (no brute-force; adjacency fallback
  pinned to that layer). `MapRouteSearcher` then routes to the actual layer, crossing transitions.
  Null `targetLayer` keeps the legacy brute-force.
- **Same-tile short-circuit:** same X/Y cell + same layer → `Description="No movement needed"`,
  `StepCount=0` (fixes the standing-on-target "North 1").
- Callers pass the real layer: entities via `(GameEntity as FieldEntity).gameObject.layer`
  (`GetPathToEntity`, `PathfindingFilter`, `AudioLoopManager` beacon); **waypoints** persist the
  player's layer at creation (`"layer"` in `waypoints.json`, file `version` 2; legacy/absent → `-1`
  → brute-force fallback) and pass it from `WaypointController.Pathfind`.
- Layer transitions detected robustly (`EntityScanner`): `SwitchLayerEventEntity` cast (runs before
  the generic `EventTriggerEntity` check since it derives from it) or name `toupper`/`tobottom`/
  `tolayer*` → `EventEntity "ToLayer"` (so `ToLayerFilter` / `Ctrl+\` work).
- The crow-flies list distance (`FormatDescription`, 3D `Vector3.Distance`) is intentionally left
  unchanged — a stacked-layer target reads as distant in the `[`/`]` list while `\` gives its route.

**`[NavDiag]`** (kept) — one line per `\` press from `GetPathToEntity`; `destCell.z` now reflects the
target's real layer (`entityLayer - 9`), confirming the layer-aware route:
```
[NavDiag] entity='<name>' playerPos=(x,y,z) playerLayer=L entityPos=(x,y,z) entityLayer=L
          crowDist=NN.N steps=N.N startCell=(cx,cy,cz) destCell=(cx,cy,cz) map=WxH
          success=bool stepCount=N points=N err='...' desc='...'
```

## Entity Name Translation (2026-01-27)

Ported from FF1 screen reader. Translates Japanese entity names to English using a JSON dictionary.

### Architecture
```
EntityTranslator.cs (Utils/)
├── Initialize() - loads FF2_translations.json from UserData/FFII_ScreenReader/
└── Translate(name) - exact match → prefix-stripped match → original
```

### Translation Lookup
1. Exact match in translations dictionary (includes circled number prefixes like ①②③)
2. Strip numeric/SC prefix (e.g., "6:" or "SC01:"), lookup base name
3. No match → return original Japanese

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

### Files
- `FFII_translations.json` (project root) - translation dictionary source (version controlled)
- `UserData/FFII_ScreenReader/FF2_translations.json` - runtime copy (~209 translations including prefixed variants)

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

## Modal dialogs + mod menu (FF1 model — no invisible windows) (2026-06-14)

`ModMenu`, `TextInputWindow`, and `ConfirmationDialog` are **virtual** (in-memory state +
TTS only). There is **no** focus-stealing / invisible window anymore — the old
`WindowsFocusHelper.StealFocus()/RestoreFocus()` mechanism was removed (the class now only
exposes `IsGameWindowFocused()`).

While any of these is open, the game receives no input because:
1. `ControllerRouter.SuppressGameInput` is true (it already includes `ModMenu.IsOpen ||
   TextInputWindow.IsOpen || ConfirmationDialog.IsOpen`), which makes the
   `InputPassthroughPatches` postfixes on `InputSystemManager.GetKeyDown/GetKey/GetKeyUp/
   GetAnyKey` return `false`; **and**
2. `InputManager.CheckInput()` calls `Input.ResetInputAxes()` every frame while
   `SuppressGameInput` is true — this is the keyboard-only net, because the passthrough
   postfixes early-return when no gamepad is connected (`!GamepadManager.IsAvailable`).

The modals read the keyboard via `GamepadManager.IsKeyCodePressed/IsKeyCodeHeld`
(GetAsyncKeyState — hardware state, no window focus needed). This matches FF1 exactly.

### ConfirmationDialog
Plain Yes/No: `Open(prompt, onYes, onNo)`. No `silentYes` mode (removed — was FF2-only).
`WaypointController.ClearAll()` now uses a **single** confirmation ("Clear all X waypoints
from this map?") like FF1, not a two-stage "Are you sure?" chain.

| Key | Action |
|-----|--------|
| Y | Confirm Yes immediately |
| N | Confirm No immediately |
| Enter | Confirm current selection (Yes/No) |
| Escape | Cancel (same as No) |
| Left/Right | Toggle selection, announce |

### Mod menu gating + open/close speech (FF1-exact)
Both entry points gate on `ControllerRouter.IsFieldActive` in the caller, exactly like FF1:
F8 in `InputManager.CheckInput()` and Start via `ControllerRouter.HandleStateTransitions` →
`OpenModMenu()`. `ModMenu.Open()` itself does **not** self-gate (matches FF1). F5's enemy-HP
cycle gates the same way.

The robustness ("only on a walkable field map — not title/boot/menus/battle/transitions") comes
from **`DetermineContext()`**, which — like FF1 — returns `KeyContext.Field` **only when a
`FieldPlayerController.fieldPlayer` exists**, otherwise `KeyContext.Global`. So `IsFieldActive`
(`gameContext == Field && !MenuStateRegistry.AnyActive() && !IsInBattle`) is trustworthy on its
own; no extra `EnsureFieldContext` chokepoint is needed. (Earlier FF2 had a weaker
`DetermineContext` that defaulted to `Field`; fixing it at the root removed the need for the
band-aid gate.) `KeyBindingRegistry` treats `Global` as "matches any context," so `Global`
bindings still fire everywhere; only `Field`-context bindings correctly stop firing off-field.

Open/close announcements ("Mod menu" / "Mod menu closed") live in `ModMenu.Open()/Close()`
(centralized — `ControllerRouter` no longer announces them).

### Mod menu item descriptions (I key — FF1 parity)
Each `MenuItem` carries a `Func<string> DescriptionGetter` (toggles return state-dependent text).
`I` in `ModMenu.HandleInput()` calls `AnnounceCurrentItemDescription()` (falls back to
`T("No description")`). Keyboard-only, exactly like FF1 (the controller path has no description
button). All description strings are localized in `mod_text.json` (12 locales).

### Beacon Destination Announcement (ported from FF1)
Toggle `AnnounceOnBeaconRestart` (pref + `FFII_ScreenReaderMod.AnnounceOnBeaconRestartEnabled`
+ `ToggleAnnounceOnBeaconRestart()`; mod-menu item under Announcements). When on and beacon nav
is on, restarting the beacon also re-speaks the destination — entities via
`RestartEntityBeacon()` (ControllerRouter left-trigger), waypoints via the beacon branch in
`WaypointController.Pathfind()` (re-pings instead of turn-by-turn directions in beacon mode).

### Mod menu order (FF1 layout)
Audio Feedback → Volume Controls → Navigation Filters (Pathfinding, Map Exit, Layer Transition,
**Stick Click Normalization** last) → Battle Settings → Announcements (Auto Detail, **Beacon
Destination Announcement**) → Close Menu. The separate "Controller" section was removed.

## Keyword/Ask Menu (FF1-manner, fully custom)

`SecretWordController` (NPC Ask/Learn/Key Items) is now a fully-custom menu: the
generic cursor reader is suppressed across the whole active menu and every level is
announced by a dedicated postfix that fires on open AND navigation.

- `KeywordMenuState.ShouldSuppress()` is state-validated (reads `stateMachine` @0x20 via
  `StateReaderHelper`): suppress whenever active and state != `None`; auto-clear on
  None/dead controller. No SetActive(false) backstop.
- Command bar **open/return**: postfix on `CommandSelectInit` + `CommandSelectingInit`
  → 1-frame coroutine reads `selectCommandCursor` (@0x38) `.Index` → `GetCommandName`.
  This is what speaks the default "Ask" on appear (`SelectCommand` only fires on nav).
- Command bar **nav**: `SelectCommand` postfix announces the command name only (the old
  first-list-entry coroutine leak was removed).
- Sub-list (Ask/Learn keywords, Key Items): `SelectContentByWord`/`SelectContentByItem`
  postfixes (rich "name: description"), fire on entry + nav.
- Symmetric dedup cross-reset: command-bar entry resets the WORD dedup; sub-list entry
  resets the COMMAND dedup, so returning to either level re-announces.
- New offsets in `IL2CppOffsets.Keyword`: `OFFSET_STATE_MACHINE` 0x20,
  `OFFSET_SELECT_COMMAND_CURSOR` 0x38, `STATE_NONE` 0.

## Shop FF1-parity

Command bar and item list are both fully custom now (generic reader suppressed for the
whole shop; `ShouldSuppress` only clears on `STATE_NONE`).

- Command bar on open + nav: `ShopCommandMenuController.SetCursor(int)` postfix reads
  `contentList[index].CommandId` → Buy/Sell/Equipment/Back. Index-dedup via `SHOP_COMMAND`.
- Unaffordable items: item announce moved from `ShopListItemContentController.SetFocus`
  (skips greyed items) to `ShopInfoController.SetDescription(string)`, which fires for
  every focused item. Reads the focused item from `ShopListMainContentController`
  (`selectCursor` @0x48 → `productContentList` @0x68). Gated to states SelectProduct(2)/
  SelectSellItem(3); list-index dedup. Empty sell slots say "Empty" without clobbering the
  I-key cache. New offsets in `IL2CppOffsets.Shop`: `STATE_SELECT_PRODUCT` 2,
  `STATE_SELECT_SELL_ITEM` 3, `LIST_MAIN_SELECT_CURSOR` 0x48, `LIST_MAIN_PRODUCT_LIST` 0x68.

## Ported FF1 fixes (cross-game parity)

- **SpeakText** strips rich-text tags centrally (`TextUtils.StripRichTextTags`);
  `StripIconMarkup` regex broadened to `<[^<>\s]+?>` (any single-token tag, preserves prose `<`).
- **Battle**: buff/debuff spells (value=0, non-Miss hitType) no longer announce a false
  "Miss" — suppressed in `CreateDamageViewWithHitType_Postfix` (the condition is announced
  by `ConditionAdd_Postfix`). Status dedup is now per-unit (keyed by `BattleUnitData`
  pointer) so same-named enemies each announce.
- **Map transition**: `FFII_ScreenReaderMod.ClearMenuFlagsForMapTransition()` clears stuck
  non-battle menu/popup flags on map-id change (`GameStatePatches.CheckMapTransition`).
- **Input**: bare F-keys gated on no-modifier; `Alt` held skips hotkey dispatch (Alt+F4 etc.).
- **Field**: `EntityScanner.FindEntityByIdentifier` matches the selected entity by reference
  first, so moving NPCs stay pinned instead of snapping to a same-named neighbor.

Already present in FF2 (not re-ported): controller hotplug (GamepadManager/SDL3), popup
cursor-index dedup, unified beacon/waypoint A*.

### Walk/run state tracker (`GameToggleAnnouncer`, 2026-06-14, FF1 parity)
`Core/Handlers/GameToggleAnnouncer.Poll()` (called once per frame from
`InputManager.CheckInput()`, reset on map transition in `MovementSpeechPatches.ResetState()`)
reads `UserDataManager.Instance().Config.IsAutoDash` **read-only** and announces `Run`/`Walk`
only on change, gated to `ControllerRouter.IsFieldActive`. This catches every source of the
toggle — F1, L3, the in-game config menu, the cheat menu — not just F1. The light per-frame read
is an allowed exception to the no-polling rule (same as footsteps).

Replaced the previous machinery, which **inverted** the state: `MoveStateHelper.GetDashFlag()`
returned `IsAutoDash XOR cachedDashFlag`, where `cachedDashFlag` came from a `SetDashFlag`
postfix (`DashFlagPatches`) that only fired on the in-game F1 toggle — so it went stale and wrong
whenever auto-dash changed from the config menu. Deleted: `DashFlagPatches.cs` + its registration,
`MoveStateHelper.{cachedDashFlag,SetCachedDashFlag,GetDashFlag}`, and the F1 `AnnounceWalkRunState`
coroutine in `InputManager`. The mod never wrote movement speed — only the announcement was wrong.

## AutoDetail mode (FF1 port)

A toggle (`PreferencesManager.AutoDetailEnabled`, **default OFF**) that controls whether
item/magic/equip/shop focus announcements include the description/stats inline.

- **Toggle**: F7 → `FFII_ScreenReaderMod.ToggleAutoDetail()` (flips the `AutoDetail` pref,
  announces on/off); also a ModMenu "Announcements → Auto Detail" toggle. Pref defined in
  `PreferencesManager` (`prefAutoDetail`, `AutoDetailEnabled`, `SaveToggle` case).
- **Gating**: each focus handler announces a terse base by default and appends the detail
  only when AutoDetail is on:
  - Item menu (`ItemMenuPatches`): base "Name (qty)"; detail = description.
  - Magic menu (`MagicMenuPatches.AnnounceSpell`): base "Name lvN, MP n"; detail = description.
  - Equip slot panel (`EquipMenuPatches.EquipmentInfoWindowController_SelectContent_Postfix`):
    base "Slot: Name"; detail = parameter message ("ATK +10") appended inline only when
    AutoDetail is on. With it off, stats come from the I key / right stick up, which reads
    the live stat panel via `EquipDetailsAnnouncer` (not the cache).
  - Equip select (`EquipMenuPatches.EquipmentSelectWindowController_SelectContent_Postfix`):
    base "Name"; detail = parameter change ("ATK +15") + description.
  - Shop (`ShopPatches.AnnounceShopItem`): base "Name, Price"; detail = stats + description.
  - Keyword menus (`KeywordPatches`): NPC Ask/Learn/Key Items and the main-menu Words browser
    (KeyInput + Touch). All four formatters (`FormatKeywordAnnouncement`,
    `FormatItemAnnouncement`, `GetWordsKeywordFromDictionary`, `GetWordsTouchKeywordAtIndex`)
    funnel through one helper `ComposeKeywordAnnouncement(name, description)` that caches the
    description and appends it inline only when AutoDetail is on. Base "Name"; detail = description.
- **On-demand (I key)**: `Core/MenuDetailCache` holds the focused item's detail string
  (written by item/magic/equip/keyword handlers); `InputManager.HandleItemDetailsKey` announces it
  when an item/magic/equip/keyword (`KeywordMenuState`/`WordsMenuState`) menu is active (else
  "No details"). Shop keeps its existing `ShopDetailsAnnouncer` (reads
  `ShopMenuTracker.LastItemStats/Description`). So detail is always reachable via I even with
  AutoDetail off.
- **No U key**: FF1's U key announces "which classes can equip" — FF2 has no equip
  restrictions (every character can equip everything), so it was intentionally not ported.

## Announcement model: no central deduplicator (FF1 parity)

The central `AnnouncementDeduplicator` + `AnnouncementContexts` were **deleted**. The mod no
longer "decides when speech should fire" via a global cache — it announces whatever is in
focus, driven by event hooks. `MenuStateHelper` keeps only IsActive/registry + reset-handler
registration (no dedup).

Rule (FF1): a hook that fires **once per user action** (SelectContent / SetCursor / SetFocus /
SelectCommand / state-entry / per-hit battle event) announces **directly, no guard**. A hook the
game calls **repeatedly / per-frame** keeps a **local single-slot value-or-index guard**, reset
at the menu boundary. Local guards in FF2:
- Shop: `_lastAnnouncedListIndex` (item list), `_lastCommandIndex` (command bar), `_lastQuantity`
  (trade window) — re-armed at level boundaries, all cleared on shop close (`ResetGuards`).
- Config sliders/arrows: `ConfigMenuState.ShouldAnnounce` + `lastSliderPercentage`/`lastArrowValue`
  (already local — value-change detection).
- Magic: `lastSpellId` / `lastCommandAnnouncement` / `lastTargetAnnouncement` (already local).
- Popup (game-over): `_lastGameOverButtonIndex` / `_lastGameOverLoadButtonIndex`, reset on close.
- Words browser: `WordsMenuState._lastIndex`.
- Battle: damage `lastDamageAnnouncement`/`lastDamageTime`; message `_lastMessage`; action
  `_lastActDataPtr` (native ptr); condition `_lastConditionByUnit` (per-unit ptr dict).
- New-game naming: `lastTargetIndex` / `_lastNameAnnounced` / `_lastAutoIndex`.
- Fade/scroll messages: `LocationMessageTracker` last-message guard.

## Keyword "Ask" — state-gated, no dedup

`SelectCommand_Postfix` is the **sole** command speaker — it fires on command-bar entry AND
navigation, so it announces the initial "Ask" and each arrow. It's gated on `IsAtCommandBar`
(state CommandSelect(1)/CommandSelecting(2)) so a cursor reset during term-select / menu-close
stays silent. `CommandSelectEntry_Postfix` (hooked to `CommandSelectingInit`) only calls
`SetActive()` to engage suppression on entry — it does NOT announce (announcing there too
doubled the command, "ask ask", once the dedup was gone). Offsets `STATE_COMMAND_SELECT`/
`STATE_COMMAND_SELECTING` added.

## Hotkeys gated on window focus

`WindowsFocusHelper.IsGameWindowFocused()` (cached `Process.MainWindowHandle` vs
`GetForegroundWindow`, fail-open) gates the keyboard hotkey dispatch in `InputManager.CheckInput`,
placed **after** the mod's own dialog/menu handlers (which may hold focus) and **before** the
F-key/registered-binding block. Controller input and `IsFieldActive` are unaffected.
