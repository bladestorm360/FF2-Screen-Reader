# FF2 Screen Reader - Project Status

Screen reader accessibility mod for Final Fantasy II Pixel Remaster. **Game is fully playable.**

**Landed (2026-09-25):** L3+R3 chord on the field toggles Stick Click Normalization (FF1 port); field
stick clicks now act on release. See `docs/debug.md` ("L3+R3 chord"). **Not yet verified in game.**
Parity wording: the chord says "Stick click normalization on/off" and the beacon toggle "Beacon navigation
on/off", as in FF1.

**Landed (2026-09-24, round 2):** see `docs/debug.md` ("Round 2"). Status removal ("X: Poison removed")
for cures, wear-off and revive; the remaining per-frame / polling hooks replaced (config rows, battle
popups, bestiary minimap and formation, gallery / music entry, walk/run, vehicle backup hook); double paths
removed (shop list SetCursor, Words UpdateView read, dead SetNextState hooks). **Not yet verified in game.**

**Landed (2026-09-23, session 2):** open-issues pass — see `docs/debug.md` ("Open-issues pass").
"Press any button" on return to title (InitNone arms, the prompt's own SystemIndicator.Hide speaks),
language-picker open read, naming-screen generic-reader suppression, config Return-to-Title/Quit guard,
per-frame hooks replaced (item/equip/magic command bars, spell list, shop quantity, game-over popups),
Words menu descriptions, shop list second focus signal, value-0 battle diagnostic log, IsOnValidMap
throttle, L3/R3 readme. **Not yet verified in game.**

**Landed (2026-09-23):** FF1-parity pass 2 — see `docs/debug.md` (2026-09-23). Four bug fixes (config
bestiary states 18/19, vehicle names, item command-bar offset, battle-magic player offset), phased
battle results incl. spell level-ups, FF1 battle command/target model, bestiary detail keys + single
announce, controls-list navigation, config/field/title/save/item open and back-out reads, battle I key
+ AutoDetail, active-actor H key, encounter toggle hook, controller fixes, 148 new translated strings.
**Unverified in game** — all of it; see "Needs in-game confirmation" below.

**Landed (2026-07-09):** untranslated-phrase + bug-fix pass — see `docs/debug.md` (2026-07-09).
3 translations + `⑩`-prefix strip, double-spell-announce merge ("Caster: name level"), enemy-HP
toggle, beacon result-screen gate, spell-list layout (name/level/percent), multi-hit damage count
(bug 3, FF1 port), and canoe "On canoe" state tracking (bug 4, FF1 port). Awaiting in-game
confirmation.

## Feature Status

| System | Status | Notes |
|--------|--------|-------|
| Menu Navigation | ✓ | Generic cursor + specialized patches |
| Item Menu | ✓ | Item list, character target with MP |
| Status Menu | ✓ | All stats, weapon skills (UI read), combat stats |
| Magic Menu | ✓ | Spell list with level/percent, Use/Forget commands; entry reads from state Inits, no per-frame hooks (not yet verified in game) |
| Config Menu | ✓ | Options with values and "(X of Y)", I key tooltip, Boost submenu, re-read after bestiary/popup, controls list navigation; language screen reads its row on open; no re-read after a confirmed Return to Title / Quit; rows read from SelectCommand + list-entry hooks, no per-frame SetFocus (not yet verified in game) |
| Shop Menu | ✓ | Buy/sell with prices/stats; "Quantity: N, Total: X" from UpdateArrowImage (no per-frame hook); greyed items read through SetDescription like the others (not yet verified in game) |
| Equipment Menu | ✓ | Slot/item selection, stat comparison |
| Dialogue | ✓ | NPC dialogue, multi-line pages, fade/scroll messages |
| Battle Commands | ✓ | Turn, command (per-turn/page dedup, back-out re-read), targets with statuses, ally initial target |
| Battle Items/Magic | ✓ | Item and spell selection; descriptions with AutoDetail or the I key |
| Battle Messages | ✓ | Damage, healing, status, "Actor: Action"; "X: Poison removed" on cure / wear-off / revive (not yet verified in game) |
| Battle Results | ✓ | Phased: gil + weapon skills, spell level-ups, stat gains per page, items (unverified in game) |
| Field Navigation | ✓ | Entity scan, pathfinding, wall bump, vehicles, wall tones, footsteps, audio beacons |
| Waypoint System | ✓ | Add/rename/delete waypoints, category filtering, single-confirm clear all (FF1 model) |
| Mod Menu | ✓ | Windowless (no focus stealing); field-only gating; FF1 layout; Beacon Destination Announcement toggle |
| Entity Translation | ✓ | Japanese→English name translation, circled number prefixes (①②③...), full-width trailing-digit strip/append (柵N→"Fence N"). All 421 map labels covered via the offline extractor (`tools/`, 2026-09-23); proper nouns use the game's official names per language |
| Title Menu | ✓ | New Game, Continue, Options; initial focus read on entry; "Press any button" on boot and return to title (not yet verified in game) |
| Naming Screen | ✓ | Character slots and suggested names by their own readers; generic reader suppressed there, start popup still read by it (not yet verified in game) |
| Popup Dialogs | ✓ | All types: confirmations, game over (read through PopupState, no per-frame hooks — not yet verified in game), title screen |
| Save/Load | ✓ | Slot info, confirmations, quicksave |
| Battle Pause | ✓ | Spacebar menu commands; its Return to Title popup read through PopupState, no per-frame hook (not yet verified in game) |
| Keyword System | ✓ | NPC dialogue works; Words menu reads name + description (Auto Detail / I key) via SetCommandSelectCursor, silent when the menu closes (not yet verified in game) |
| Extras | ✓ | Bestiary minimap / formation, gallery and music player entry reads event-driven, no polling (not yet verified in game) |

## Recent Fixes (2026-06-14)

- **Map Exit Filter now groups exits** — `EntityScanner.DeduplicateMapExits()` collapses
  same-destination exits (e.g. a town's dozen world-map border exits) to the nearest; the
  toggle was previously a no-op. Applies immediately via `ReapplyFilter()`.
- **Wall tones (and beacons) no longer silent** — fixed a start-before-save race; the audio
  loops now gate on the mod's local enable flag (FF1 parity) instead of the not-yet-saved
  preference.
- **Walk/run state no longer inverted** — replaced the XOR `GetDashFlag` machinery with a
  read-only `GameToggleAnnouncer` poll of `Config.IsAutoDash`; now correct from F1 *and* the
  config menu.

## Known Issues

1. **No EXP counter / "Battle Results" mod-menu section** - FF2 has no EXP, and no result counting
   animation was confirmed
2. **Status removal** - announced from the condition-function reconcile as "X: Poison removed" (cures,
   wear-off, revive); silent at battle end and for a unit that is down. Not yet verified in game.
   `HitType.Zero` still reads "0 damage"; the value-0 diagnostic log is gone.
3. Fixed 2026-09-23 session 2, not yet verified in game: Words menu description, unaffordable shop
   items, "Press any button" on return to title

## Needs in-game confirmation (2026-09-23 pass)

- Spell level-ups: `SetLevelUpList` fires once per result (check the `[BattleResult] SetLevelUpList call`
  log line) and before/after ability exp differ as expected.
- Stat-gain pages: one announcement per character page (`ResultStatusUpController.SetData`).
- Config: re-read after leaving the config bestiary and after cancelling Quit/Return to Title; no stray
  read on a confirmed Return to Title; title Configuration opens with one read.
- Battle: command menu speaks every turn (incl. a single survivor), after page switches and after
  backing out of targets/spells/items, never on a commit; ally targeting speaks its first target once.
- Bestiary detail: one "Name: …" read on entry, monster switch and page flip.
- Item menu: item list and item-use targets read on entry and on back-out, no double on single-target entry.
- Save list and title menus: initial slot/command read once.
- Encounter toggle speaks once on the field (not on config changes / loads).

## Needs in-game confirmation (2026-09-23 open-issues pass, session 2)

- "Press any button" once at boot and once after Return to Title, when the prompt appears (not during
  the fade-in); never in game.
- Item / Equipment / Magic command bars: focused command read once on open and on return from a list;
  magic spell list reads its first spell on every entry; no double reads while navigating.
- Shop trade window: "Quantity: 1, Total: X" on open, then each change once; greyed items read like others.
- Game-over: "Game Over. Load" on open; arrows read each choice once; "Start from recent save data?
  Yes (1 of 2)"; back out of it → the focused Load / Return to Title is read.
- Title Configuration → Language: "Language: English (1 of 1)"; the dropdown reads its items.
- Config → Return to Title → Yes: no config row spoken during the fade.
- Naming screen: suggested names read once each; the start popup's Yes/No still read.
- Words menu: "keyword: description (X of Y)" with Auto Detail on; the name only with it off, I reads the
  description.

## Needs in-game confirmation (2026-09-24, round 2)

- "X: Poison removed" after Antidote / Basuna, after a status wears off, "X: KO removed" (the game's KO
  name) after Life / Phoenix Down; nothing at victory / escape, nothing when a unit dies.
- Config: arrows read each row once; entering config, Boost, the title Configuration list and Sound
  settings reads the focused row once; leaving config is silent; no generic-reader double.
- Battle pause → Return to Title popup: open read once, arrows read Yes / No once.
- Bestiary: "Minimap open: X" / "Minimap closed. X"; formation view read once on entry.
- Gallery / Music Player: title then first entry once.
- F1 / L3 on the field: "Run" / "Walk" once; vehicles still announced on boarding / leaving.
- Words menu: nothing spoken when closing it; shop items read once each.

## FF2-Specific

- **Usage-based growth**: Stats increase through use (HP from damage, skills from attacks)
- **Weapon skill levels**: 1-16 per weapon type
- **Spell proficiency**: 1-16 per spell
- **Keyword system**: Learn/use keywords in NPC dialogue

## Architectural Refactoring (2026-02-08)

Ported FF3's proven architectural patterns to FF2. Main entry point reduced from 1,921 to 1,224 lines (36% reduction). All changes behavior-preserving.

| Phase | New File | Lines | Status |
|-------|----------|-------|--------|
| 1. PreferencesManager | `Core/PreferencesManager.cs` | 138 | Done |
| 2. AudioLoopManager | `Core/AudioLoopManager.cs` | 210 | Done |
| 3. GameInfoAnnouncer | `Core/GameInfoAnnouncer.cs` | 100 | Done |
| 4. WaypointController | `Core/WaypointController.cs` | 256 | Done |
| 5. CursorSuppressionCheck | `Core/CursorSuppressionCheck.cs` | 94 | Done |
| 6. IL2CppOffsets | `Utils/IL2CppOffsets.cs` | 248 | Done |
| 7. HarmonyPatchHelper | `Utils/HarmonyPatchHelper.cs` | 185 | Done |
| 8. MenuStateHelper | `Utils/MenuStateHelper.cs` | 83 | Done |

## Code Audit (2026-02-07)

Release prep cleanup completed:
- **Logging**: Removed all `MelonLogger.Msg()` (29 calls) and `MelonLogger.Warning()` (105 catch blocks + 56 upgraded to Error). Only `MelonLogger.Error()` remains (126 calls for critical failures + broken patches).
- **Dead code**: Deleted `Utils/SpeechHelper.cs` (zero callers), removed EntityScanner debug fields/infrastructure, simplified PathfindingFilter debug block.
- **File consolidation**: Merged `PlayerPositionHelper.cs`, `CharacterUtility.cs`, `CollectionHelper.cs` into `Utils/Helpers.cs`. Extracted `DirectionHelper` from duplicated code in NavigableEntity/WaypointEntity into `Helpers.cs`. Made `ToLayerFilter` implement `IEntityFilter`.

## Documentation

| Document | Purpose |
|----------|---------|
| [debug.md](debug.md) | Implementation details, memory offsets, bug fixes |
| [port.md](port.md) | Features to port from FF3 |
| [CLAUDE.md](../CLAUDE.md) | Build instructions, coding conventions |

## Build

```batch
cmd //c "D:\Games\Dev\Unity\FFPR\ff2\ff2-screen-reader\build_and_deploy.bat"
```

## References

- **FF3 Screen Reader**: `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`
- **Game Dump**: `D:\Games\Dev\Unity\FFPR\ff2\dump.cs` (490K lines - search only)
