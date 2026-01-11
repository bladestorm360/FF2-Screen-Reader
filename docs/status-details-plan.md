# StatusDetailsReader Port Plan

Port the status details navigation system from FF3 to FF2, enabling arrow-key navigation through character stats when viewing the status details screen.

## Hotkeys (Same as FF3)

| Key | Action |
|-----|--------|
| Up Arrow | Navigate to previous stat |
| Down Arrow | Navigate to next stat |
| Ctrl+Up | Jump to first stat (top) |
| Ctrl+Down | Jump to last stat (bottom) |
| Shift+Up | Jump to previous stat group |
| Shift+Down | Jump to next stat group |
| R | Repeat current stat |

## Stat Groups

### Group 1: Character Info (indices 0-3)
- **Name**: "Name: Firion"
- **Level**: "Level: 5"
- **Experience**: "Experience: 1234"
- **Next Level**: "Next Level: 500"

*Note: No Job stat (FF2 has no job system)*

### Group 2: Vitals (indices 4-5)
- **HP**: "HP: 45 / 120"
- **MP**: "MP: 20 / 40"

*Note: No spell charges per level (FF2 uses MP, not Vancian magic)*

### Group 3: Attributes (indices 6-10)
- **Strength**: "Strength: 25"
- **Agility**: "Agility: 18"
- **Stamina**: "Stamina: 22"
- **Intelligence**: "Intelligence: 15"
- **Spirit**: "Spirit: 20"

### Group 4: Combat Stats (indices 11-16)
- **Attack**: "Attack: 42" *(no attack count - FF2 doesn't display this)*
- **Accuracy**: "Accuracy: 85" *(displayed as raw value, not percentage)*
- **Defense**: "Defense: 30"
- **Evasion**: "Evasion: 25"
- **Magic Defense**: "Magic Defense: 18"
- **Magic Evasion**: "Magic Evasion: 12"

### Group 5: Weapon Skills (indices 17-26) - FF2 SPECIFIC
Format: "skill name lv(level): (progress) percent"

- **Sword**: "sword lv2: 30 percent"
- **Knife**: "knife lv1: 75 percent"
- **Spear**: "spear lv3: 10 percent"
- **Axe**: "axe lv1: 0 percent"
- **Staff**: "staff lv1: 45 percent"
- **Bow**: "bow lv2: 80 percent"
- **Shield**: "shield lv4: 15 percent"
- **Unarmed**: "unarmed lv1: 20 percent"
- **Evasion Skill**: "evasion skill lv2: 50 percent"
- **Magic Evasion Skill**: "magic evasion skill lv1: 90 percent"

**Formula**: level = (exp / 100) + 1, progress = exp % 100

**NVDA Note**: Using "percent" spelled out ensures proper reading regardless of NVDA punctuation settings.

## Implementation Files

### 1. Menus/StatusDetailsReader.cs (Major Expansion)

Add new classes:

```csharp
// StatusNavigationTracker - Singleton for tracking navigation state
public class StatusNavigationTracker
{
    public bool IsNavigationActive { get; set; }
    public int CurrentStatIndex { get; set; }
    public OwnedCharacterData CurrentCharacterData { get; set; }
    public StatusDetailsController ActiveController { get; set; }

    public void Reset();
    public bool ValidateState();
}

// StatusNavigationReader - Navigation logic and stat readers
public static class StatusNavigationReader
{
    // Navigation
    public static void NavigateNext();
    public static void NavigatePrevious();
    public static void JumpToNextGroup();
    public static void JumpToPreviousGroup();
    public static void JumpToTop();
    public static void JumpToBottom();
    public static void ReadCurrentStat();

    // Stat readers (27 total)
    // Character Info: Name, Level, Experience, NextLevel
    // Vitals: HP, MP
    // Attributes: Strength, Agility, Stamina, Intelligence, Spirit
    // Combat: Attack, Accuracy, Defense, Evasion, MagicDefense, MagicEvasion
    // Weapon Skills: Sword, Knife, Spear, Axe, Staff, Bow, Shield, Unarmed, EvasionSkill, MagicEvasionSkill
}
```

Group start indices: `{ 0, 4, 6, 11, 17 }` (CharacterInfo, Vitals, Attributes, CombatStats, WeaponSkills)

### 2. Core/InputManager.cs

Add `HandleStatusDetailsInput()` method (identical to FF3):

```csharp
private bool HandleStatusDetailsInput()
{
    var tracker = StatusNavigationTracker.Instance;
    if (!tracker.IsNavigationActive || !tracker.ValidateState())
        return false;

    if (Input.GetKeyDown(KeyCode.UpArrow))
    {
        if (IsCtrlHeld()) StatusNavigationReader.JumpToTop();
        else if (IsShiftHeld()) StatusNavigationReader.JumpToPreviousGroup();
        else StatusNavigationReader.NavigatePrevious();
        return true;
    }

    if (Input.GetKeyDown(KeyCode.DownArrow))
    {
        if (IsCtrlHeld()) StatusNavigationReader.JumpToBottom();
        else if (IsShiftHeld()) StatusNavigationReader.JumpToNextGroup();
        else StatusNavigationReader.NavigateNext();
        return true;
    }

    if (Input.GetKeyDown(KeyCode.R))
    {
        StatusNavigationReader.ReadCurrentStat();
        return true;
    }

    return false;
}
```

Call from `HandleGlobalInput()` early, before teleport handling.

### 3. Patches/StatusMenuPatches.cs

Add patches for StatusDetailsController:

```csharp
public static class StatusDetailsPatches
{
    public static void TryApplyPatches(Harmony harmony);

    // Patches InitDisplay() - fires when entering status details view
    // - Get character data from controller (statusController.targetData at 0x30)
    // - Initialize StatusNavigationTracker
    // - Announce initial status
    public static void InitDisplay_Postfix(object __instance);

    // Patches ExitDisplay() - fires when leaving status details view
    // - Call StatusNavigationTracker.Instance.Reset()
    // - Clear character data
    public static void ExitDisplay_Postfix();
}
```

**Controller structure (KeyInput variant)**:
- `StatusDetailsController` (offset 0x70: view, 0x78: statusController, 0x80: skillLevelContentList)
- `statusController.targetData` at offset 0x30 contains `OwnedCharacterData`

## IL2CPP Type Aliases

```csharp
using KeyInputStatusDetailsController = Il2CppSerial.FF2.UI.KeyInput.StatusDetailsController;
using TouchStatusDetailsController = Il2CppSerial.FF2.UI.Touch.StatusDetailsController;
using AbilityCharaStatusController = Il2CppSerial.FF2.UI.Touch.AbilityCharaStatusController;
```

## FF2 vs FF3 Differences

| Feature | FF3 | FF2 |
|---------|-----|-----|
| Job stat | Yes | No |
| MP system | Spell charges (LV1-LV8) | Single MP pool |
| Attack display | "2 x 42" (count x power) | "42" (power only) |
| Accuracy display | "85%" (percentage) | "85" (raw value) |
| Weapon skills | No | Yes (10 skill types, levels 1-16) |
| Total stats | 26 | 27 |

## Testing Checklist

- [ ] Build succeeds
- [ ] Navigation activates when entering status details screen
- [ ] All 5 stat groups navigate correctly
- [ ] Wrap-around works (bottom to top, top to bottom)
- [ ] Group jumping works with Shift+Up/Down
- [ ] Ctrl+Up/Down jumps to top/bottom
- [ ] R key repeats current stat
- [ ] Navigation deactivates when exiting status details
- [ ] Weapon skill format reads correctly with NVDA ("sword lv2: 30 percent")
- [ ] No interference with other menus/navigation
