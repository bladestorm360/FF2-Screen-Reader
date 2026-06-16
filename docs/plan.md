# FF2 Screen Reader - Project Status

Screen reader accessibility mod for Final Fantasy II Pixel Remaster. **Game is fully playable.**

## Feature Status

| System | Status | Notes |
|--------|--------|-------|
| Menu Navigation | ✓ | Generic cursor + specialized patches |
| Item Menu | ✓ | Item list, character target with MP |
| Status Menu | ✓ | All stats, weapon skills (UI read), combat stats |
| Magic Menu | ✓ | Spell list with level/MP, Use/Forget commands |
| Config Menu | ✓ | Options with values, I key tooltip, Boost submenu |
| Shop Menu | ✓ | Buy/sell with prices/stats |
| Equipment Menu | ✓ | Slot/item selection, stat comparison |
| Dialogue | ✓ | NPC dialogue, multi-line pages, fade/scroll messages |
| Battle Commands | ✓ | Turn, command, target announcements |
| Battle Items/Magic | ✓ | Item and spell selection |
| Battle Messages | ✓ | Damage, healing, status, action names |
| Battle Results | ✓ | Gil, weapon/magic level-ups, stat gains, items |
| Field Navigation | ✓ | Entity scan, pathfinding, wall bump, vehicles, wall tones, footsteps, audio beacons |
| Waypoint System | ✓ | Add/rename/delete waypoints, category filtering, single-confirm clear all (FF1 model) |
| Mod Menu | ✓ | Windowless (no focus stealing); field-only gating; FF1 layout; Beacon Destination Announcement toggle |
| Entity Translation | ✓ | Japanese→English name translation, circled number prefixes (①②③...), full-width trailing-digit strip/append (柵N→"Fence N") |
| Title Menu | ✓ | New Game, Continue, Options |
| Popup Dialogs | ✓ | All types: confirmations, game over, title screen |
| Save/Load | ✓ | Slot info, confirmations, quicksave |
| Battle Pause | ✓ | Spacebar menu commands |
| Keyword System | Partial | NPC dialogue works; Words menu reads name only |

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

1. **Words menu description** - Main menu keyword list shows name only (NPC Ask/Learn works)
2. **Spell level-up announcements** - Removed; needs `ExpUtility.GetExpLevel` reimplementation
3. **Shop unaffordable items** - Game skips `SetFocus(true)` for these

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
