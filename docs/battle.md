# Battle System Implementation

## Overview

FF2 uses a turn-based battle system. Each character selects commands, then actions execute. The screen reader must announce:
- Whose turn it is
- Available commands
- Target selection
- Action results (damage, healing, status)
- Battle outcomes

## Key Classes from dump.cs

### Battle Scene Management
```csharp
// Line 290552 - Main battle scene
public class Battle : SubSceneBase, ISceneStateProcess<SubSceneManagerMainGame.State>

// Line 260710 - Battle menu scene
public class BattleMenu : SubSceneBase, ISceneStateProcess<SubSceneManagerMainGame.State>

// Line 260762 - Battle scene controller
public class BattleScene : SubSceneBase
```

### Battle UI Components
```csharp
// Line 271545 - Central UI manager
BattleMenuUiManager : SingletonMonoBehaviour<BattleMenuUiManager>

// Line 271205 - Menu window controller
BattleMenuWindowController : MonoBehaviour

// Line 271104 - Command controller
BattleCommandController : MonoBehaviour
BattleCommandView : MonoBehaviour (Line 271126)

// Line 269641 - Menu window view
BattleMenuWindowView : MonoBehaviour
```

### Target Selection
```csharp
// Line 271429 - Enemy target view
BattleTargetEnemySelectView : MonoBehaviour

// Line 401976, 434914 - Target selection controllers
BattleTargetSelectController : MonoBehaviour
BattleTargetSelectView : MonoBehaviour (Lines 402782, 435762)

// Line 401748 - Target content
BattleTargetSelectContentController : MonoBehaviour
BattleTargetSelectContentView : MonoBehaviour (Line 401894)
```

### Battle Data
```csharp
// Line 256538 - Player instance data
BattlePlayerInstanceData

// Line 256620 - Player battle data
BattlePlayerData : BattleUnitData

// Line 333611 - Battle result
BattleResultData
BattleResultData.BattleResultCharacterData (Line 333727)
```

### Ability/Magic Display
```csharp
// Line 274499, 277855 - Ability info controllers
BattleQuantityAbilityInfomationController : BattleAbilityInfomationControllerBase

// Line 284741, 286557 - Ability info views
BattleAbilityInfomationView : MonoBehaviour
```

## Implementation Details

### Command Selection Patch

Reference: `ff3-screen-reader/Patches/BattleCommandPatches.cs`

```csharp
// Patch BattleCommandController to announce selected command
[HarmonyPatch(typeof(BattleCommandController))]
public class BattleCommandPatches
{
    // When command selection changes
    public static void OnCommandChanged(BattleCommandController __instance)
    {
        // Read current command name: Attack, Magic, Item, etc.
        string commandName = GetCurrentCommandName(__instance);
        SpeakText(commandName, interrupt: false);
    }
}
```

### Target Selection

Reference: `ff3-screen-reader/Patches/BattleTargetPatches.cs`

Key considerations:
- Suppress generic cursor announcements during target selection
- Announce enemy name, current HP (if known), status effects
- For party targets, announce character name and HP/MP

```csharp
// Track when target selection is active
public static bool IsTargetSelectionActive { get; private set; }

// Announce target with HP info
private static void AnnounceTarget(BattleTargetSelectController controller)
{
    var target = controller.CurrentTarget;
    string name = target.Name;
    string hp = $"HP {target.CurrentHP}/{target.MaxHP}";
    SpeakText($"{name}, {hp}", interrupt: false);
}
```

### Battle Messages

Reference: `ff3-screen-reader/Patches/BattleMessagePatches.cs`

```csharp
// Line 288908 - System messages
BattleSystemMessageProvider

// Read damage numbers, healing amounts, status changes
// These often appear in BattleViewText (Line 256774)
```

### Battle Results

Reference: `ff3-screen-reader/Patches/BattleResultPatches.cs`

```csharp
// Announce victory/defeat
// Read rewards: Gil, items, exp (though FF2 doesn't use traditional exp)

BattleResultData result = ...;
// - Victory announcement
// - Gil earned
// - Items obtained
// - Stat growth (FF2-specific, see ff2-specific.md)
```

## Turn Order Announcements

```csharp
// Announce when it's a character's turn
// "Firion's turn"
// "Maria's turn"

// This may be tied to BattleMenuWindowController.Show or similar
```

## FF2-Specific Battle Features

### Spell Levels
FF2 spells have levels 1-16. When selecting magic:
- Announce spell name AND current level
- Example: "Fire Level 5, costs 8 MP"

### Weapon Skills
Characters build proficiency with weapon types:
- Sword, Axe, Spear, Bow, Staff, etc.
- Levels affect hit count and damage

### Status Growth
After battle, FF2 may grant stat increases based on actions:
- Took damage → HP may increase
- Used MP → MP may increase
- Attacked → Strength may increase
- Cast spells → Intelligence may increase

See [ff2-specific.md](ff2-specific.md) for more details on these systems.

## Implementation Order

1. Command selection announcements
2. Target selection with suppression
3. Turn start announcements
4. Damage/healing results
5. Battle outcome (victory/defeat)
6. FF2-specific: spell levels, stat growth
