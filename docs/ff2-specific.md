# FF2-Specific Features Implementation

## Overview

Final Fantasy II has several unique systems that differ from other FF games:
- **Keyword System**: Learn and use keywords in dialogue
- **Stat Growth System**: Stats grow through use, not leveling
- **Spell/Weapon Levels**: Abilities improve through repetition
- **No Traditional Experience**: Characters don't gain "levels"

## Keyword System (SecretWord)

### Key Classes from dump.cs
```csharp
// Line 384310 - Central manager
SecretWordUIManager : SingletonMonoBehaviour<SecretWordUIManager>

// Line 358071 - UI client
SecretWordUIClient

// Line 424799, 459383 - Controller
SecretWordController : SecretWordControllerBase

// Line 425085, 459933 - View
SecretWordView : MonoBehaviour

// Line 420359, 454488 - Words window
WordsWindowController : MonoBehaviour, ISubMenuController
WordsWindowView : MonoBehaviour (Line 454617)

// Line 420178, 454326 - Words list
WordsContentListController : MonoBehaviour
WordsContentListView : MonoBehaviour
```

### How Keywords Work
1. Players learn keywords by talking to NPCs (highlighted text)
2. Keywords are stored in a "Words" menu
3. When talking to NPCs, players can:
   - **Ask**: Ask about a learned keyword
   - **Item**: Show an item from inventory
   - **Memorize**: Learn a new keyword from dialogue

### Implementation
```csharp
// Patch keyword learning
[HarmonyPatch(typeof(SecretWordController), "LearnWord")]
public static void OnKeywordLearned(string word)
{
    SpeakText($"Learned keyword: {word}", interrupt: true);
}

// Read keywords in Words menu
// Similar to item menu - cursor navigation reads keyword name
// May need to read keyword description/context

// During NPC dialogue, announce when Ask/Item options appear
```

### Words Menu Navigation
```csharp
// When browsing learned keywords
void AnnounceKeyword(WordsContentController controller)
{
    string keyword = controller.CurrentWord;
    SpeakText(keyword, interrupt: false);
}
```

## Stat Growth System

### How Stats Grow in FF2
Unlike traditional FF games, FF2 stats grow based on actions:

| Action | Potential Growth |
|--------|------------------|
| Take HP damage | Max HP increase |
| Take MP damage | Max MP increase |
| Use physical attacks | Strength increase |
| Cast magic spells | Intelligence/Spirit increase |
| Get hit by attacks | Stamina increase |
| Evade attacks | Agility/Evasion increase |
| Use weapon type | Weapon skill level increase |
| Cast specific spell | Spell level increase |

### Key Classes
```csharp
// Line 332649 - Player character parameters
PlayerCharacterParameter : CharacterParameterBase

// Line 283051 - Unique update provider (likely stat growth)
UniqueUpdateParameterProvider : IUniqueUpdateParameterProvider

// Line 274256 - Parameter provider
ParameterProvider : ParameterProviderBase

// Line 348407 - Parameter master data
Parameter : MasterBase
ParameterCorrection : MasterBase (Line 348488)
```

### Stat Growth Announcements
```csharp
// After battle, check for stat changes
[HarmonyPatch(typeof(BattleResultData), "ProcessResults")]
public static void OnBattleEnd(BattleResultData __instance)
{
    foreach (var charResult in __instance.CharacterResults)
    {
        var growths = GetStatGrowths(charResult);
        if (growths.Any())
        {
            string announcement = FormatGrowths(charResult.Name, growths);
            SpeakText(announcement, interrupt: false);
        }
    }
}

// Format: "Firion: HP +5, Strength +1"
```

## Spell Levels

### System Details
- Spells range from Level 1-16
- Higher levels = more damage/effect but more MP cost
- Leveling occurs through repeated casting

### Key Classes
```csharp
// Line 271795 - Magic level acquisition table
MiscAssetDesc.MagicLevelAcquisitionTableList

// Line 271865 - Magic item data
MiscAssetDesc.MagicItem

// Line 289221 - Equipment magic provider
EquipmentMagicProvider

// Line 289623 - Magic stone provider
MagicStoneProvider
```

### Magic Menu Reading
```csharp
// When browsing magic menu, announce:
// - Spell name
// - Current level
// - MP cost at current level

void AnnounceMagicSelection(AbilityContentListController controller)
{
    var spell = controller.CurrentSpell;
    string name = spell.Name;
    int level = spell.Level;
    int mpCost = spell.MPCost;

    SpeakText($"{name} Level {level}, {mpCost} MP", interrupt: false);
}
```

### Spell Level Up Announcement
```csharp
// After battle, announce spell level increases
// "Fire leveled up to 6"
```

## Weapon Skill Levels

### System Details
- Each weapon type has a skill level (1-16)
- Higher skill = more hits, better accuracy
- Weapon types: Sword, Axe, Spear, Bow, Staff, Knife, etc.

### Key Classes
```csharp
// Line 290534 - Weapon serial provider
BattleWeaponSerialProvider

// Equipment providers likely track weapon proficiency
EquipmentAbilityProvider (Line 289170)
```

### Weapon Skill Reading
```csharp
// In status/equipment menu, announce weapon proficiencies
// "Sword Level 8, Bow Level 3"

void AnnounceWeaponSkills(StatusWindowController controller)
{
    var skills = GetWeaponSkills(controller.Character);
    foreach (var skill in skills.Where(s => s.Level > 0))
    {
        SpeakText($"{skill.WeaponType} Level {skill.Level}", interrupt: false);
    }
}
```

## Character Rotation

### System Details
FF2 has party members who join and leave throughout the story:
- Core party: Firion, Maria, Guy
- Temporary members: Minwu, Josef, Gordon, Leila, Ricard, etc.

### Implementation
```csharp
// Announce when party composition changes
// May need to patch party management functions

void AnnouncePartyChange(string memberName, bool joined)
{
    string action = joined ? "joined" : "left";
    SpeakText($"{memberName} has {action} the party", interrupt: true);
}
```

## Status Menu (FF2-Specific)

The status screen in FF2 shows unique information:
- Current HP/MP and growth progress
- Weapon skill levels
- Spell levels (in magic menu)
- Status effects

```csharp
// Full status announcement
void AnnounceFullStatus(OwnedCharacterData character)
{
    var sb = new StringBuilder();
    sb.AppendLine($"{character.Name}");
    sb.AppendLine($"HP {param.CurrentHP}/{param.MaxHP}");
    sb.AppendLine($"MP {param.CurrentMP}/{param.MaxMP}");
    sb.AppendLine($"Strength {param.Strength}");
    sb.AppendLine($"Agility {param.Agility}");
    // etc.

    SpeakText(sb.ToString(), interrupt: true);
}
```

## Implementation Priority

1. **Keyword System** - Critical for story progression
   - Learn keyword announcements
   - Words menu navigation
   - Ask/Item dialogue options

2. **Spell Levels** - Important for magic users
   - Magic menu level display
   - Level up announcements

3. **Stat Growth** - Nice to have
   - Post-battle growth announcements
   - Status menu details

4. **Weapon Skills** - Lower priority
   - Equipment/status display
   - Level up announcements
