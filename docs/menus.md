# Menu Accessibility Implementation

## Overview

All menu navigation must be announced via screen reader. This includes the main menu, camp menu, shops, equipment, items, status, and save/load screens.

## Key Classes from dump.cs

### Cursor System
```csharp
// Line 386526 - Main cursor class
public class Cursor : MonoBehaviour  // Il2CppLast.UI.Cursor

// Methods to patch:
// - NextIndex()      - Move cursor down/right
// - PrevIndex()      - Move cursor up/left
// - SkipNextIndex()  - Page down
// - SkipPrevIndex()  - Page up
```

### Menu Controllers
```csharp
// Line 366117 - Central menu manager
SingletonMonoBehaviour<MenuManager>

// Status screens (Lines 276597, 280178)
StatusWindowContentController : StatusWindowContentControllerBase
StatusWindowContentView : StatusWindowContentViewBase

// Item window (Lines 418635, 418911)
ItemWindowController : MonoBehaviour, ISubMenuController
ItemWindowView : MonoBehaviour

// Equipment (Lines 414932, 415117)
EquipmentInfoWindowController : MonoBehaviour
EquipmentSelectWindowController : MonoBehaviour
```

### Message Windows
```csharp
// Line 296100 - Dialogue manager
MessageWindowManager : SingletonMonoBehaviour<MessageWindowManager>

// Fields to read:
// - messageList : List<string> - Current dialogue text
// - spekerValue : string - Speaker name (note typo in game code)

// Methods to patch:
// - SetContent(List<BaseContent>) - When dialogue text changes
// - Play(bool) - When dialogue starts playing
```

## Implementation Details

### Cursor Navigation Patch

Reference: `ff3-screen-reader/Patches/CursorNavigationPatches.cs`

```csharp
// Patch approach from FF3 (manual patching required)
private void TryPatchCursorNavigation(Harmony harmony)
{
    Type cursorType = FindType("Il2CppLast.UI.Cursor");

    var postfix = typeof(ManualPatches).GetMethod("CursorNavigation_Postfix");

    // Patch all navigation methods
    harmony.Patch(cursorType.GetMethod("NextIndex"), postfix: new HarmonyMethod(postfix));
    harmony.Patch(cursorType.GetMethod("PrevIndex"), postfix: new HarmonyMethod(postfix));
    // etc.
}
```

### Menu Text Discovery

After cursor moves, wait one frame then read text at cursor position:

```csharp
// From ff3: MenuTextDiscovery.WaitAndReadCursor()
public static IEnumerator WaitAndReadCursor(Cursor cursor, string context)
{
    yield return null; // Wait one frame for UI update

    string text = GetTextAtCursorPosition(cursor);
    if (!string.IsNullOrEmpty(text))
    {
        SpeakText(text, interrupt: false);
    }
}
```

### Menu Suppression

Some menus handle their own announcements. Suppress generic cursor reading for:
- Battle target selection (BattleTargetPatches handles this)
- Config menus (ConfigMenuPatches handles this)
- Equipment selection (EquipMenuPatches handles this)

Check parent hierarchy for specific UI names before announcing.

## Save/Load Slots

Reference: `ff3-screen-reader/Menus/SaveSlotReader.cs`

```csharp
// Key classes (Line 357976)
SaveWindowClient
SaveWindowManager : SingletonMonoBehaviour<SaveWindowManager>

// Read slot info:
// - Character names in party
// - Current location (map name)
// - Play time
// - Gil amount
```

## Character Status

```csharp
// Access via UserDataManager (Line 352506)
OwnedCharacterData character = userDataManager.GetOwnedCharactersClone(false)[index];

// Read fields:
// - character.Name
// - character.Parameter.CurrentHP / ConfirmedMaxHp()
// - character.Parameter.CurrentMP / ConfirmedMaxMp()
```

## Menu Items to Announce

| Menu | Information to Read |
|------|---------------------|
| Main Menu | Option name (Items, Magic, Equip, Status, etc.) |
| Item List | Item name, quantity, description |
| Magic List | Spell name, level, MP cost |
| Equipment | Slot name, current item, stat changes |
| Status | Character name, HP/MP, stats, status effects |
| Save/Load | Slot number, party, location, playtime |
| Shop | Item name, price, owned count |

## Config Menu

```csharp
// Lines 303938-303999
DebugMenuController
ConfigActualDetailsControllerBase (Line 813)
```

Config menus need special handling for:
- Volume sliders (read current value)
- Toggle options (read on/off state)
- Selection options (read current selection)
