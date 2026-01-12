using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine.UI;
using Il2CppLast.Data.User;
using Il2CppLast.Defaine;
using Il2CppLast.Defaine.Master;
using Il2CppLast.Systems;
using Il2CppLast.Battle;
using FFII_ScreenReader.Core;

// Type alias for status details controller
using KeyInputStatusDetailsController = Il2CppSerial.FF2.UI.KeyInput.StatusDetailsController;

namespace FFII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading character status details.
    /// FF2 specific: No jobs, uses MP system, weapon skills, spell proficiency.
    /// </summary>
    public static class StatusDetailsReader
    {
        private static OwnedCharacterData currentCharacterData = null;

        public static void SetCurrentCharacterData(OwnedCharacterData data)
        {
            currentCharacterData = data;
        }

        public static void ClearCurrentCharacterData()
        {
            currentCharacterData = null;
        }

        public static OwnedCharacterData GetCurrentCharacterData()
        {
            return currentCharacterData;
        }

        /// <summary>
        /// Read all character status information.
        /// FF2 format: Name, HP/MP (no level - FF2 uses usage-based growth)
        /// </summary>
        public static string ReadStatusDetails()
        {
            if (currentCharacterData == null)
            {
                return "No character data available";
            }

            var parts = new List<string>();

            try
            {
                string name = currentCharacterData.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    parts.Add(name);
                }

                var param = currentCharacterData.Parameter;
                if (param != null)
                {
                    int currentHp = param.currentHP;
                    int maxHp = param.ConfirmedMaxHp();
                    parts.Add($"HP: {currentHp} / {maxHp}");

                    int currentMp = param.currentMP;
                    int maxMp = param.ConfirmedMaxMp();
                    parts.Add($"MP: {currentMp} / {maxMp}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading status details: {ex.Message}");
            }

            return parts.Count > 0 ? string.Join(". ", parts) : "No data";
        }

        /// <summary>
        /// Read physical combat stats (Strength, Vitality, Defense, Evade).
        /// </summary>
        public static string ReadPhysicalStats()
        {
            if (currentCharacterData == null || currentCharacterData.Parameter == null)
            {
                return "No character data available";
            }

            try
            {
                var param = currentCharacterData.Parameter;
                var parts = new List<string>();

                int strength = param.ConfirmedPower();
                parts.Add($"Strength: {strength}");

                int vitality = param.ConfirmedVitality();
                parts.Add($"Vitality: {vitality}");

                int defense = param.ConfirmedDefense();
                parts.Add($"Defense: {defense}");

                int evade = param.ConfirmedDefenseCount();
                parts.Add($"Evade: {evade}");

                return string.Join(". ", parts);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading physical stats: {ex.Message}");
                return $"Error reading physical stats";
            }
        }

        /// <summary>
        /// Read magical combat stats (Magic, Magic Defense, Magic Evade).
        /// </summary>
        public static string ReadMagicalStats()
        {
            if (currentCharacterData == null || currentCharacterData.Parameter == null)
            {
                return "No character data available";
            }

            try
            {
                var param = currentCharacterData.Parameter;
                var parts = new List<string>();

                int magic = param.ConfirmedMagic();
                parts.Add($"Magic: {magic}");

                int magicDefense = param.ConfirmedAbilityDefense();
                parts.Add($"Magic Defense: {magicDefense}");

                int magicEvade = param.ConfirmedMagicDefenseCount();
                parts.Add($"Magic Evade: {magicEvade}");

                return string.Join(". ", parts);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading magical stats: {ex.Message}");
                return $"Error reading magical stats";
            }
        }

        /// <summary>
        /// Read core attributes (Strength, Agility, Stamina, Intelligence, Spirit).
        /// </summary>
        public static string ReadAttributes()
        {
            if (currentCharacterData == null || currentCharacterData.Parameter == null)
            {
                return "No character data available";
            }

            try
            {
                var param = currentCharacterData.Parameter;
                var parts = new List<string>();

                parts.Add($"Strength: {param.ConfirmedPower()}");
                parts.Add($"Agility: {param.ConfirmedAgility()}");
                parts.Add($"Stamina: {param.ConfirmedVitality()}");
                parts.Add($"Intelligence: {param.ConfirmedIntelligence()}");
                parts.Add($"Spirit: {param.ConfirmedSpirit()}");

                return string.Join(". ", parts);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading attributes: {ex.Message}");
                return $"Error reading attributes";
            }
        }

        /// <summary>
        /// Read combat stats (Attack, Accuracy, Defense, Evasion).
        /// </summary>
        public static string ReadCombatStats()
        {
            if (currentCharacterData == null || currentCharacterData.Parameter == null)
            {
                return "No character data available";
            }

            try
            {
                var param = currentCharacterData.Parameter;
                var parts = new List<string>();

                int attack = param.ConfirmedAttack();
                parts.Add($"Attack: {attack}");

                int accuracy = param.ConfirmedAccuracyRate(false);
                parts.Add($"Accuracy: {accuracy}%");

                int defense = param.ConfirmedDefense();
                parts.Add($"Defense: {defense}");

                int evasion = param.ConfirmedEvasionRate(false);
                parts.Add($"Evasion: {evasion}%");

                int magicDef = param.ConfirmedAbilityDefense();
                parts.Add($"Magic Defense: {magicDef}");

                int magicEvade = param.ConfirmedAbilityEvasionRate(false);
                parts.Add($"Magic Evasion: {magicEvade}%");

                return string.Join(". ", parts);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading combat stats: {ex.Message}");
                return $"Error reading combat stats";
            }
        }
    }

    /// <summary>
    /// Stat groups for organizing status screen statistics.
    /// FF2 doesn't have jobs, levels, or spell charges - all growth is usage-based.
    /// </summary>
    public enum StatGroup
    {
        CharacterInfo,  // Name only (FF2 has no traditional levels)
        Vitals,         // HP, MP
        Attributes,     // Strength, Agility, Stamina, Intellect, Spirit
        CombatStats,    // Attack, Accuracy, Defense, Evasion, Magic Defense, Magic Evasion
        WeaponSkills,   // Sword, Knife, Spear, Axe, Staff, Bow, Shield, Unarmed (no evasion - it's a stat, not a weapon skill)
    }

    /// <summary>
    /// Definition of a single stat that can be navigated
    /// </summary>
    public class StatusStatDefinition
    {
        public string Name { get; set; }
        public StatGroup Group { get; set; }
        public Func<OwnedCharacterData, string> Reader { get; set; }

        public StatusStatDefinition(string name, StatGroup group, Func<OwnedCharacterData, string> reader)
        {
            Name = name;
            Group = group;
            Reader = reader;
        }
    }

    /// <summary>
    /// Tracks navigation state within the status screen for arrow key navigation
    /// </summary>
    public class StatusNavigationTracker
    {
        private static StatusNavigationTracker instance = null;
        public static StatusNavigationTracker Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new StatusNavigationTracker();
                }
                return instance;
            }
        }

        public bool IsNavigationActive { get; set; }
        public int CurrentStatIndex { get; set; }
        public OwnedCharacterData CurrentCharacterData { get; set; }
        public KeyInputStatusDetailsController ActiveController { get; set; }

        private StatusNavigationTracker()
        {
            Reset();
        }

        public void Reset()
        {
            IsNavigationActive = false;
            CurrentStatIndex = 0;
            CurrentCharacterData = null;
            ActiveController = null;
        }

        public bool ValidateState()
        {
            return IsNavigationActive &&
                   CurrentCharacterData != null &&
                   ActiveController != null &&
                   ActiveController.gameObject != null &&
                   ActiveController.gameObject.activeInHierarchy;
        }
    }

    /// <summary>
    /// Handles navigation through status screen stats using arrow keys.
    /// FF2-specific: 22 stats in 5 groups, includes weapon skills, no level/XP.
    /// </summary>
    public static class StatusNavigationReader
    {
        private static List<StatusStatDefinition> statList = null;
        // Group start indices: CharacterInfo=0, Vitals=1, Attributes=3, CombatStats=9, WeaponSkills=15
        private static readonly int[] GroupStartIndices = new int[] { 0, 1, 3, 9, 15 };

        /// <summary>
        /// Initialize the stat list with all visible stats in UI order.
        /// FF2-specific stats: 22 total (no job, no level, no XP - all growth is usage-based)
        /// Order matches the in-game status screen layout.
        /// </summary>
        public static void InitializeStatList()
        {
            if (statList != null) return;

            statList = new List<StatusStatDefinition>();

            // Character Info Group (index 0) - Just name (FF2 has no traditional levels)
            statList.Add(new StatusStatDefinition("Name", StatGroup.CharacterInfo, ReadName));

            // Vitals Group (indices 1-2): HP, MP
            statList.Add(new StatusStatDefinition("HP", StatGroup.Vitals, ReadHP));
            statList.Add(new StatusStatDefinition("MP", StatGroup.Vitals, ReadMP));

            // Attributes Group (indices 3-8) - order matches screen: Strength, Spirit, Intellect, Stamina, Agility, Magic
            statList.Add(new StatusStatDefinition("Strength", StatGroup.Attributes, ReadStrength));
            statList.Add(new StatusStatDefinition("Spirit", StatGroup.Attributes, ReadSpirit));
            statList.Add(new StatusStatDefinition("Intellect", StatGroup.Attributes, ReadIntelligence));
            statList.Add(new StatusStatDefinition("Stamina", StatGroup.Attributes, ReadStamina));
            statList.Add(new StatusStatDefinition("Agility", StatGroup.Attributes, ReadAgility));
            statList.Add(new StatusStatDefinition("Magic", StatGroup.Attributes, ReadMagic));

            // Combat Stats Group (indices 9-14) - order matches screen
            // Accuracy/Evasion/Magic Defense use "Nx Y%" format
            statList.Add(new StatusStatDefinition("Attack", StatGroup.CombatStats, ReadAttack));
            statList.Add(new StatusStatDefinition("Accuracy", StatGroup.CombatStats, ReadAccuracy));
            statList.Add(new StatusStatDefinition("Defense", StatGroup.CombatStats, ReadDefense));
            statList.Add(new StatusStatDefinition("Evasion", StatGroup.CombatStats, ReadEvasion));
            statList.Add(new StatusStatDefinition("Magic Defense", StatGroup.CombatStats, ReadMagicDefense));
            statList.Add(new StatusStatDefinition("Magic Interference", StatGroup.CombatStats, ReadMagicInterference));

            // Weapon Skills Group (indices 15-21) - FF2 specific
            // Order matches status screen: Fist, Sword, Staff, Spear, Axe, Bow, Shield
            statList.Add(new StatusStatDefinition("Fist", StatGroup.WeaponSkills, ReadUnarmedSkill));
            statList.Add(new StatusStatDefinition("Sword", StatGroup.WeaponSkills, ReadSwordSkill));
            statList.Add(new StatusStatDefinition("Staff", StatGroup.WeaponSkills, ReadStaffSkill));
            statList.Add(new StatusStatDefinition("Spear", StatGroup.WeaponSkills, ReadSpearSkill));
            statList.Add(new StatusStatDefinition("Axe", StatGroup.WeaponSkills, ReadAxeSkill));
            statList.Add(new StatusStatDefinition("Bow", StatGroup.WeaponSkills, ReadBowSkill));
            statList.Add(new StatusStatDefinition("Shield", StatGroup.WeaponSkills, ReadShieldSkill));
        }

        /// <summary>
        /// Navigate to the next stat (wraps to top at end)
        /// </summary>
        public static void NavigateNext()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = (tracker.CurrentStatIndex + 1) % statList.Count;
            ReadCurrentStat();
        }

        /// <summary>
        /// Navigate to the previous stat (wraps to bottom at top)
        /// </summary>
        public static void NavigatePrevious()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex--;
            if (tracker.CurrentStatIndex < 0)
            {
                tracker.CurrentStatIndex = statList.Count - 1;
            }
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the first stat of the next group
        /// </summary>
        public static void JumpToNextGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int nextGroupIndex = -1;

            // Find next group start index
            for (int i = 0; i < GroupStartIndices.Length; i++)
            {
                if (GroupStartIndices[i] > currentIndex)
                {
                    nextGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            // Wrap to first group if at end
            if (nextGroupIndex == -1)
            {
                nextGroupIndex = GroupStartIndices[0];
            }

            tracker.CurrentStatIndex = nextGroupIndex;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the first stat of the previous group
        /// </summary>
        public static void JumpToPreviousGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int prevGroupIndex = -1;

            // Find previous group start index
            for (int i = GroupStartIndices.Length - 1; i >= 0; i--)
            {
                if (GroupStartIndices[i] < currentIndex)
                {
                    prevGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            // Wrap to last group if at beginning
            if (prevGroupIndex == -1)
            {
                prevGroupIndex = GroupStartIndices[GroupStartIndices.Length - 1];
            }

            tracker.CurrentStatIndex = prevGroupIndex;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the top (first stat)
        /// </summary>
        public static void JumpToTop()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = 0;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the bottom (last stat)
        /// </summary>
        public static void JumpToBottom()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = statList.Count - 1;
            ReadCurrentStat();
        }

        /// <summary>
        /// Read the stat at the current index
        /// </summary>
        public static void ReadCurrentStat()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.ValidateState())
            {
                FFII_ScreenReaderMod.SpeakText("Navigation not available");
                return;
            }

            ReadStatAtIndex(tracker.CurrentStatIndex);
        }

        /// <summary>
        /// Read the stat at the specified index
        /// </summary>
        private static void ReadStatAtIndex(int index)
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;

            if (index < 0 || index >= statList.Count)
            {
                MelonLogger.Warning($"Invalid stat index: {index}");
                return;
            }

            if (tracker.CurrentCharacterData == null)
            {
                FFII_ScreenReaderMod.SpeakText("No character data");
                return;
            }

            try
            {
                var stat = statList[index];
                string value = stat.Reader(tracker.CurrentCharacterData);
                FFII_ScreenReaderMod.SpeakText(value, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading stat at index {index}: {ex.Message}");
                FFII_ScreenReaderMod.SpeakText("Error reading stat");
            }
        }

        #region Character Info Readers

        private static string ReadName(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "N/A";
                string name = data.Name;
                return !string.IsNullOrWhiteSpace(name) ? $"Name: {name}" : "N/A";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading name: {ex.Message}");
                return "N/A";
            }
        }

        // Note: FF2 has no traditional level system - all growth is usage-based

        #endregion

        #region Vitals Readers

        private static string ReadHP(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                int current = data.Parameter.currentHP;
                int max = data.Parameter.ConfirmedMaxHp();
                return $"HP: {current} / {max}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading HP: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadMP(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                int current = data.Parameter.currentMP;
                int max = data.Parameter.ConfirmedMaxMp();
                return $"MP: {current} / {max}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading MP: {ex.Message}");
                return "N/A";
            }
        }

        #endregion

        #region Attribute Readers

        private static string ReadStrength(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Strength: {data.Parameter.ConfirmedPower()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Strength: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadAgility(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Agility: {data.Parameter.ConfirmedAgility()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Agility: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadStamina(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Stamina: {data.Parameter.ConfirmedVitality()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Stamina: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadIntelligence(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                // Displayed as "Intellect" on screen
                return $"Intellect: {data.Parameter.ConfirmedIntelligence()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Intellect: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadSpirit(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Spirit: {data.Parameter.ConfirmedSpirit()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Spirit: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadMagic(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Magic: {data.Parameter.ConfirmedMagic()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic: {ex.Message}");
                return "N/A";
            }
        }

        #endregion

        #region Combat Stat Readers

        private static string ReadAttack(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                // FF2: Just attack power, no attack count display
                int attackPower = data.Parameter.ConfirmedAttack();
                return $"Attack: {attackPower}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Attack: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadAccuracy(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                // FF2: Accuracy displayed as "Nx Y%" (e.g., "1x 70%")
                // ConfirmedAccuracyCount() returns 0-indexed, need +1 for display
                int count = data.Parameter.ConfirmedAccuracyCount() + 1;
                int rate = data.Parameter.ConfirmedAccuracyRate(false);
                return $"Accuracy: {count}x {rate} percent";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Accuracy: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Defense: {data.Parameter.ConfirmedDefense()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Defense: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadEvasion(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                // FF2: Evasion displayed as "Nx Y%" (e.g., "1x 24%")
                int count = data.Parameter.ConfirmedEvasionCount();
                int rate = data.Parameter.ConfirmedEvasionRate(false);
                return $"Evasion: {count}x {rate} percent";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Evasion: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadMagicDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                // FF2: Magic Defense displayed as "Nx Y%" (e.g., "1x 12%")
                // ConfirmedAbilityDefense() returns the rate value (confusing naming)
                int count = data.Parameter.ConfirmedMagicDefenseCount();
                int rate = data.Parameter.ConfirmedAbilityDefense();
                return $"Magic Defense: {count}x {rate} percent";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Defense: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadMagicInterference(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                // FF2: Magic Interference (spell success penalty from heavy armor)
                // ConfirmedAbilityDisturbedRate(true) returns the displayed value
                // (false) returns base value, (true) includes equipment
                int interference = data.Parameter.ConfirmedAbilityDisturbedRate(true);
                return $"Magic Interference: {interference}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Interference: {ex.Message}");
                return "N/A";
            }
        }

        #endregion

        #region Weapon Skill Readers

        /// <summary>
        /// Read a weapon skill level and progress using BattleUtility.GetSkillLevel for accurate calculation.
        /// Format: "skill name lv(level): (progress) percent"
        /// </summary>
        private static string ReadWeaponSkill(OwnedCharacterData data, SkillLevelTarget skillType, string skillName)
        {
            try
            {
                if (data == null) return $"{skillName}: N/A";

                // Use BattleUtility.GetSkillLevel for accurate level (same method game uses internally)
                int level = BattleUtility.GetSkillLevel(data, skillType);

                // Get exp for percentage within level
                int exp = 0;
                var skillTargets = data.SkillLevelTargets;
                if (skillTargets != null && skillTargets.ContainsKey(skillType))
                {
                    exp = skillTargets[skillType];
                }

                // Calculate progress within current level
                // Each level requires 100 exp, so exp % 100 gives 0-99 directly as percentage
                int progress = exp % 100;

                return $"{skillName} lv{level}: {progress} percent";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading {skillName} skill: {ex.Message}");
                return $"{skillName}: N/A";
            }
        }

        private static string ReadSwordSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponSword, "Sword");
        }

        private static string ReadKnifeSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponKnife, "Knife");
        }

        private static string ReadSpearSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponSpear, "Spear");
        }

        private static string ReadAxeSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponAxe, "Axe");
        }

        private static string ReadStaffSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponCane, "Staff");
        }

        private static string ReadBowSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponBow, "Bow");
        }

        private static string ReadShieldSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponShield, "Shield");
        }

        private static string ReadUnarmedSkill(OwnedCharacterData data)
        {
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponWrestle, "Fist");
        }

        // Note: Evasion skills (PhysicalAvoidance, AbilityAvoidance) are intentionally NOT included
        // as weapon skills. They don't have exp bars - only stat gains announced in battle results.

        #endregion
    }
}
