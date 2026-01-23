# Code Cleanup & Performance Issues

Documentation of code cleanup performed and remaining items.

## Completed Items (2026-01-23)

### 1. Dead Code Removed

| File | Method | Status |
|------|--------|--------|
| `GameStatePatches.cs` | `ResetMapTracking()` | ✓ Removed |
| `FFII_ScreenReaderMod.cs` | `ClearOtherMenuStates(string exceptMenu)` | ✓ Removed |

### 2. Duplicate Code Consolidated

#### State Machine Reading
- Added `OFFSET_BATTLE_COMMAND_CONTROLLER = 0x48` constant to `StateReaderHelper.cs`
- Removed duplicate `GetCommandState()` from `BattleItemPatches.cs`
- Removed duplicate `GetCommandState()` from `BattleMagicPatches.cs`
- Both now use `StateReaderHelper.ReadStateTag(controller.Pointer, StateReaderHelper.OFFSET_BATTLE_COMMAND_CONTROLLER)`

### 3. Debug Logging Removed

Removed verbose `MelonLogger.Msg()` debug logging from all files while keeping:
- `MelonLogger.Error()` in catch blocks (critical failures)
- `MelonLogger.Warning()` for patch failures and missing methods

**Files cleaned:**
- `BattleItemPatches.cs`
- `BattleMagicPatches.cs`
- `BattleResultPatches.cs`
- `BattleCommandPatches.cs`
- `BattleMessagePatches.cs`
- `BattlePausePatches.cs`
- `PopupPatches.cs`
- `MagicMenuPatches.cs`
- `ItemMenuPatches.cs`
- `EquipMenuPatches.cs`
- `ShopPatches.cs`
- `StatusMenuPatches.cs`
- `StatusDetailsReader.cs`
- `FieldNavigationHelper.cs`
- `EntityScanner.cs`
- `SaveLoadPatches.cs`
- `NewGameNamingPatches.cs`
- `ScrollMessagePatches.cs`
- `MessageWindowPatches.cs`
- `KeywordPatches.cs`
- `ConfigMenuPatches.cs`
- `GameStatePatches.cs`
- `InputManager.cs`
- `TolkWrapper.cs`

---

## Remaining Items (Low/Medium Priority)

### Medium Priority

1. **Consolidate `FormatItemAnnouncement()`** - Identical methods exist in:
   - `Patches\BattleItemPatches.cs`
   - `Patches\KeywordPatches.cs`

   *Solution*: Create shared helper in `Utils\` folder.

### Low Priority

2. **Consolidate asset name formatting** - Similar logic in `EntityScanner.cs`:
   - `FormatAssetNameAsReadable()`
   - `FormatAssetNameAsMapName()`

   *Solution*: Extract common formatting into single parameterizable method.

---

## Architecture Notes

### Per-Frame/Polling Status: ✓ COMPLIANT

The codebase uses event-driven hooks per CLAUDE.md requirements:
- Map transitions: `GameStatePatches.ChangeState_Postfix`
- Battle state: `BattleResultPatches.Show_Postfix`
- Menu states: Individual patch postfixes

The only `OnUpdate()` is in `FFII_ScreenReaderMod.cs` for keyboard input handling (acceptable exception).

### Helper Usage Now Consistent

`StateReaderHelper.ReadStateTag()` is now used consistently across all files that need state machine reading:
- `EquipMenuPatches.cs`
- `ItemMenuPatches.cs`
- `ShopPatches.cs`
- `BattleItemPatches.cs` (consolidated)
- `BattleMagicPatches.cs` (consolidated)

---

## Summary

| Category | Before | After |
|----------|--------|-------|
| Dead methods | 2 | 0 |
| Duplicate `GetCommandState()` | 2 | 0 |
| Verbose debug logging | ~300+ lines | 0 |
| StateReaderHelper inconsistencies | 2 files | 0 |
