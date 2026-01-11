# FF2 Screen Reader - Project Summary

Screen reader accessibility mod for Final Fantasy II Pixel Remaster, enabling blind/low-vision gameplay via NVDA/JAWS.

## Current Status

**Game is playable.** All core menu systems ported and working. Some battle result issues remain.

| System | Status | Notes |
|--------|--------|-------|
| Menu Navigation | Working | Generic cursor + specialized patches |
| Item Menu | Working | Item list, character target selection with MP |
| Status Menu | Working | Character selection with HP/MP (no jobs) |
| Magic Menu | Working | Spell list with proficiency level and MP cost |
| Config Menu | Working | Config options with current values |
| Shop Menu | Working | Buy/sell lists with prices and stats |
| Equipment Menu | Working | Slot/item selection with stat comparison |
| Dialogue | Working | NPC dialogue, fade/scroll messages |
| Battle Commands | Working | Turn, command, target announcements |
| Battle Items/Magic | Working | Item and spell selection during battle |
| Battle Messages | Working | Damage, healing, status effects |
| Battle Results | Working | Gil, weapon/magic skill level-ups, stat gains, items |
| Field Navigation | Working | Entity scan, pathfinding, wall bump |
| Title Menu | Working | New Game, Continue, Options |
| Keyword System | Working | NPC dialogue (Ask/Learn/Key Items/Cancel), Words menu |

## Documentation

| Document | Purpose |
|----------|---------|
| [debug.md](debug.md) | Implementation details, known issues, session logs |
| [port.md](port.md) | Features to port from FF3, prioritized checklist |
| [CLAUDE.md](../CLAUDE.md) | Build instructions, coding conventions |

## Architecture

```
MelonLoader (net6.0) + HarmonyLib → IL2CppInterop → Unity IL2CPP
                                         ↓
                                   Tolk.dll → NVDA/JAWS
```

### Directory Structure
```
Core/       Main mod entry, input handling, filters
Menus/      Menu text discovery, save slot reader
Field/      Entity scanning, pathfinding, navigation
Patches/    Harmony patches for game method interception
Utils/      Tolk wrapper, caching, text utilities
docs/       Documentation and decompiled scripts
```

## Priority Tasks

See [port.md](port.md) for full details.

1. ~~**Active State Pattern** (P1)~~ ✓ DONE - Centralized menu state management
2. ~~**Map Name Resolution** (P2)~~ ✓ DONE - Proper area/floor names
3. ~~**Entity Focus Preservation** (P2.5)~~ ✓ DONE - Fix selection jumping on re-sort
4. ~~**Menu Patches** (P3)~~ ✓ DONE - Item, Status, Magic, Config, Shop menus
5. ~~**Menu Readers** (P4)~~ ✓ DONE - CharacterSelection, ShopCommand, StatusDetails
6. ~~**Battle Result Fix**~~ ✓ DONE - Gil/skill level-ups now announce from Show_Postfix
7. ~~**Title Menu** (P6)~~ ✓ DONE - New Game, Continue, Options navigation
8. ~~**Stat Gain Announcements**~~ ✓ DONE - HP/Evasion/etc. in ShowStatusUpInit phase
9. ~~**Vehicle/Movement** (P5)~~ ✓ DONE - Movement speech, vehicle landing
10. ~~**Keyword System** (P7)~~ ✓ DONE - NPC dialogue keywords (Ask/Remember/Item), Words menu
11. **Popup Patches** (P8) - Pending - Popup message announcements

## Key Issues

See [debug.md](debug.md) for details.

- ~~**Battle Result Phase 1**: `ShowPointsInit` patch not firing~~ ✓ FIXED - moved to Show_Postfix

## FF2-Specific Features

Unlike FF3's job/XP system, FF2 uses:
- **Usage-based growth**: Stats increase through use (HP from damage, skills from attacks) - ✓ Announced in ShowStatusUpInit phase
- **Weapon skill levels**: 1-16 per weapon type - ✓ Announced with percentage/level-ups
- **Spell proficiency**: 1-16 per spell, increases with casting - ✓ Announced with level-ups
- **Keyword system**: Learn/use keywords in dialogue - ✓ Implemented (see [debug.md](debug.md#keyword-system-state-machine))

---

## Build

```batch
cmd //c "D:\Games\Dev\Unity\FFPR\ff2\ff2-screen-reader\build_and_deploy.bat"
```

Deploys to `FINAL FANTASY II PR\Mods\`. Logs at `MelonLoader\Logs\`.

## References

- **FF3 Screen Reader**: Primary template at `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`
- **Decompiled Functions**: `docs/Scripts/` - Ghidra analysis of growth system
- **Game Dump**: `D:\Games\Dev\Unity\FFPR\ff2\dump.cs` - IL2CPP class dump (490K lines)
