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
| Dialogue | ✓ | NPC dialogue, fade/scroll messages |
| Battle Commands | ✓ | Turn, command, target announcements |
| Battle Items/Magic | ✓ | Item and spell selection |
| Battle Messages | ✓ | Damage, healing, status, action names |
| Battle Results | ✓ | Gil, weapon/magic level-ups, stat gains, items |
| Field Navigation | ✓ | Entity scan, pathfinding, wall bump, vehicles |
| Title Menu | ✓ | New Game, Continue, Options |
| Popup Dialogs | ✓ | All types: confirmations, game over, title screen |
| Save/Load | ✓ | Slot info, confirmations, quicksave |
| Battle Pause | ✓ | Spacebar menu commands |
| Keyword System | Partial | NPC dialogue works; Words menu reads name only |

## Known Issues

1. **Words menu description** - Main menu keyword list shows name only (NPC Ask/Learn works)
2. **Spell level-up announcements** - Removed; needs `ExpUtility.GetExpLevel` reimplementation
3. **Shop unaffordable items** - Game skips `SetFocus(true)` for these

## FF2-Specific

- **Usage-based growth**: Stats increase through use (HP from damage, skills from attacks)
- **Weapon skill levels**: 1-16 per weapon type
- **Spell proficiency**: 1-16 per spell
- **Keyword system**: Learn/use keywords in NPC dialogue

## Documentation

| Document | Purpose |
|----------|---------|
| [debug.md](debug.md) | Implementation details, memory offsets, bug fixes |
| [port.md](port.md) | Features to port from FF3 |
| [performanceIssues.md](performanceIssues.md) | Code cleanup: dead code, redundancy, debug logging |
| [CLAUDE.md](../CLAUDE.md) | Build instructions, coding conventions |

## Build

```batch
cmd //c "D:\Games\Dev\Unity\FFPR\ff2\ff2-screen-reader\build_and_deploy.bat"
```

## References

- **FF3 Screen Reader**: `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`
- **Game Dump**: `D:\Games\Dev\Unity\FFPR\ff2\dump.cs` (490K lines - search only)
