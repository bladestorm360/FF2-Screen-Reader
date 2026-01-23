using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppLast.Data.User;
using Il2CppLast.Defaine;
using Il2CppLast.Defaine.Master;
using Il2CppLast.Systems;
using Il2CppLast.Battle;
using FFII_ScreenReader.Core;

// Type aliases for status details UI controllers
using KeyInputStatusDetailsController = Il2CppSerial.FF2.UI.KeyInput.StatusDetailsController;
using SkillLevelContentController = Il2CppSerial.FF2.UI.SkillLevelContentController;
using SkillLevelContentView = Il2CppSerial.FF2.UI.SkillLevelContentView;
using CommonGauge = Il2CppLast.UI.CommonGauge;
using ParameterContentController = Il2CppLast.UI.KeyInput.ParameterContentController;
using ParameterType = Il2CppLast.Defaine.ParameterType;

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
    /// FF2-specific: 23 stats in 5 groups, includes weapon skills, no level/XP.
    /// </summary>
    public static class StatusNavigationReader
    {
        private static List<StatusStatDefinition> statList = null;
        // Group start indices: CharacterInfo=0, Vitals=1, Attributes=3, CombatStats=9, WeaponSkills=15
        private static readonly int[] GroupStartIndices = new int[] { 0, 1, 3, 9, 15 };

        #region UI Reading Constants and Cache

        /// <summary>
        /// Cache for weapon skill data read from UI.
        /// Key: SkillLevelTarget enum value (list index), Value: (level, percentage)
        /// Event-driven: populated when status screen opens, cleared when it closes.
        /// </summary>
        private static Dictionary<int, (int level, int percentage)> weaponSkillCache = new Dictionary<int, (int, int)>();

        /// <summary>
        /// Cached accuracy count read from UI.
        /// In FF2, accuracy count (number of hits) comes from equipped weapons, not base stats.
        /// ConfirmedAccuracyCount() returns 0 because BaseAccuracyCount is never initialized.
        /// We read from UI text like FF3 does for attack count.
        /// </summary>
        private static int cachedAccuracyCount = -1;

        /// <summary>
        /// Flag to track if cache has been populated this session.
        /// Reset when InvalidateUICache() is called.
        /// </summary>
        private static bool cachePopulated = false;

        /// <summary>
        /// Mapping from UI list index to SkillLevelTarget enum value.
        /// UI displays: Sword, Knife, Spear, Staff, Axe, Bow, Shield, Unarmed
        /// Enum order:  Sword(0), Knife(1), Spear(2), Axe(3), Staff(4), Bow(5), Shield(6), Unarmed(7)
        /// Indices 3 and 4 are SWAPPED between UI and enum.
        /// </summary>
        private static readonly int[] uiIndexToSkillType = { 0, 1, 2, 4, 3, 5, 6, 7 };

        #endregion

        #region UI Reading Helpers

        /// <summary>
        /// Invalidate UI cache. Called when entering/exiting status screen.
        /// Clears weapon skills and accuracy count (read from UI).
        /// </summary>
        public static void InvalidateUICache()
        {
            MelonLogger.Msg("[StatusDetails] Invalidating UI cache");
            weaponSkillCache.Clear();
            cachedAccuracyCount = -1;
            cachePopulated = false;
        }

        /// <summary>
        /// Populate UI cache. Called once when status details screen opens.
        /// Caches weapon skills and accuracy count (which comes from equipped weapons).
        /// </summary>
        public static void PopulateUICache()
        {
            if (cachePopulated)
            {
                MelonLogger.Msg("[StatusDetails] Cache already populated, skipping");
                return;
            }

            MelonLogger.Msg("[StatusDetails] Populating UI cache...");
            CacheWeaponSkillsFromUI();
            CacheAccuracyCountFromUI();
            cachePopulated = true;
            MelonLogger.Msg($"[StatusDetails] Cache populated: {weaponSkillCache.Count} weapon skills, accuracy count={cachedAccuracyCount}");
        }

        // Memory offset for SkillLevelContentController.view (private field)
        private const int OFFSET_SKILL_VIEW = 0x18;
        // Memory offset for CommonGauge.gaugeImage (private field)
        private const int OFFSET_GAUGE_IMAGE = 0x18;
        // Memory offset for StatusDetailsController (KeyInput).skillLevelContentList
        private const int OFFSET_SKILL_LEVEL_CONTENT_LIST_KEYINPUT = 0x80;
        // Memory offset for StatusDetailsControllerBase.contentList (parameter controllers)
        private const int OFFSET_CONTENT_LIST = 0x48;
        // Memory offset for ParameterContentController.type
        private const int OFFSET_PARAMETER_TYPE = 0x18;
        // Memory offset for ParameterContentController.view
        private const int OFFSET_PARAMETER_VIEW = 0x20;
        // Memory offset for ParameterContentView.multipliedValueText (count value like "8" in "8x")
        private const int OFFSET_MULTIPLIED_VALUE_TEXT = 0x28;
        // ParameterType.AccuracyRate enum value (the count is displayed in the same controller's view)
        private const int PARAMETER_TYPE_ACCURACY_RATE = 16;

        /// <summary>
        /// Cache all weapon skill data from UI controllers using LIST INDEX approach.
        /// The skillLevelContentList in StatusDetailsController is ordered to match visual display.
        /// Index 0 = Sword, Index 1 = Knife, ..., Index 7 = Unarmed (matches SkillLevelTarget enum).
        /// This avoids relying on weaponType field or visible text which may not match.
        /// </summary>
        private static void CacheWeaponSkillsFromUI()
        {
            weaponSkillCache.Clear();

            try
            {
                // Get the active status details controller from the navigation tracker
                var tracker = StatusNavigationTracker.Instance;
                if (tracker?.ActiveController == null)
                {
                    MelonLogger.Warning("[StatusDetails] No active controller in tracker - cannot cache weapon skills");
                    return;
                }

                var activeController = tracker.ActiveController;
                IntPtr controllerPtr = activeController.Pointer;
                if (controllerPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[StatusDetails] Active controller pointer is null");
                    return;
                }

                MelonLogger.Msg($"[StatusDetails] Accessing skillLevelContentList from StatusDetailsController at offset 0x{OFFSET_SKILL_LEVEL_CONTENT_LIST_KEYINPUT:X}");

                // Access skillLevelContentList at offset 0x80 (KeyInput variant)
                IntPtr listPtr;
                unsafe
                {
                    listPtr = *(IntPtr*)((byte*)controllerPtr + OFFSET_SKILL_LEVEL_CONTENT_LIST_KEYINPUT);
                }

                if (listPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[StatusDetails] skillLevelContentList pointer is null");
                    return;
                }

                // Create managed List wrapper
                var skillList = new Il2CppSystem.Collections.Generic.List<SkillLevelContentController>(listPtr);
                int count = skillList.Count;
                MelonLogger.Msg($"[StatusDetails] skillLevelContentList has {count} entries");

                if (count == 0)
                {
                    MelonLogger.Warning("[StatusDetails] skillLevelContentList is empty");
                    return;
                }

                // Skill names for logging (index matches SkillLevelTarget enum)
                string[] skillNames = { "Sword", "Knife", "Spear", "Axe", "Staff", "Bow", "Shield", "Unarmed" };
                // UI skill names in display order (indices 3 and 4 are swapped vs enum)
                string[] uiSkillNames = { "Sword", "Knife", "Spear", "Staff", "Axe", "Bow", "Shield", "Unarmed" };

                // Iterate through list BY INDEX, then map to correct enum value
                for (int i = 0; i < count && i < 8; i++)
                {
                    try
                    {
                        var controller = skillList[i];
                        if (controller == null)
                        {
                            MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): controller is null");
                            continue;
                        }

                        IntPtr skillControllerPtr = controller.Pointer;
                        if (skillControllerPtr == IntPtr.Zero)
                        {
                            MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): controller pointer is zero");
                            continue;
                        }

                        // Read view pointer at offset 0x18 (private field)
                        IntPtr viewPtr;
                        unsafe
                        {
                            viewPtr = *(IntPtr*)((byte*)skillControllerPtr + OFFSET_SKILL_VIEW);
                        }
                        if (viewPtr == IntPtr.Zero)
                        {
                            MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): view pointer is zero");
                            continue;
                        }

                        // Create managed wrapper for the view
                        var view = new SkillLevelContentView(viewPtr);
                        if (view == null)
                        {
                            MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): failed to create view wrapper");
                            continue;
                        }

                        // Read level from LevelText property
                        int level = 1;
                        Text levelText = view.LevelText;
                        if (levelText != null)
                        {
                            string levelStr = levelText.text;
                            if (!string.IsNullOrEmpty(levelStr))
                            {
                                int.TryParse(levelStr.Trim(), out level);
                            }
                            MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): levelText=\"{levelStr}\" -> level={level}");
                        }
                        else
                        {
                            MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): LevelText is null");
                        }

                        // Read percentage from gauge
                        int percentage = -1;
                        CommonGauge gauge = view.CommonGauge;
                        if (gauge != null)
                        {
                            IntPtr gaugePtr = gauge.Pointer;
                            if (gaugePtr != IntPtr.Zero)
                            {
                                IntPtr imagePtr;
                                unsafe
                                {
                                    imagePtr = *(IntPtr*)((byte*)gaugePtr + OFFSET_GAUGE_IMAGE);
                                }
                                if (imagePtr != IntPtr.Zero)
                                {
                                    var gaugeImage = new Image(imagePtr);
                                    if (gaugeImage != null)
                                    {
                                        float fillAmount = gaugeImage.fillAmount;
                                        percentage = (int)(fillAmount * 100);
                                        if (percentage < 0) percentage = 0;
                                        if (percentage > 99) percentage = 99;
                                        MelonLogger.Msg($"[StatusDetails] Index {i} ({(i < skillNames.Length ? skillNames[i] : "?")}): gauge fillAmount={fillAmount} -> {percentage}%");
                                    }
                                }
                            }
                        }

                        // Map UI index to SkillLevelTarget enum value (indices 3 and 4 are swapped)
                        int skillType = uiIndexToSkillType[i];
                        weaponSkillCache[skillType] = (level, percentage);
                        string uiName = i < uiSkillNames.Length ? uiSkillNames[i] : "?";
                        string enumName = skillType < skillNames.Length ? skillNames[skillType] : "?";
                        MelonLogger.Msg($"[StatusDetails] UI index {i} ({uiName}) -> enum {skillType} ({enumName}): level={level}, percentage={percentage}");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[StatusDetails] Error reading skill at index {i}: {ex.Message}");
                    }
                }

                MelonLogger.Msg($"[StatusDetails] Weapon skill cache complete: {weaponSkillCache.Count} entries");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[StatusDetails] Error caching weapon skills: {ex.Message}");
            }
        }

        /// <summary>
        /// Cache accuracy count from UI.
        /// In FF2, accuracy count (number of hits shown as "8x" in "8x 99%") comes from equipped weapons.
        /// ConfirmedAccuracyCount() returns 0 because BaseAccuracyCount is never initialized for players.
        /// This is similar to how FF3 handles attack count for hidden weapon skill levels.
        /// </summary>
        private static void CacheAccuracyCountFromUI()
        {
            cachedAccuracyCount = -1;

            try
            {
                var tracker = StatusNavigationTracker.Instance;
                if (tracker?.ActiveController == null)
                {
                    MelonLogger.Warning("[StatusDetails] No active controller - cannot cache accuracy count");
                    return;
                }

                IntPtr controllerPtr = tracker.ActiveController.Pointer;
                if (controllerPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[StatusDetails] Controller pointer is null");
                    return;
                }

                // Access contentList at offset 0x48 (from base StatusDetailsControllerBase)
                IntPtr contentListPtr;
                unsafe
                {
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr + OFFSET_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[StatusDetails] contentList pointer is null");
                    return;
                }

                var contentList = new Il2CppSystem.Collections.Generic.List<ParameterContentController>(contentListPtr);
                int count = contentList.Count;

                // Search for the AccuracyRate parameter controller (count is displayed in same view)
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var controller = contentList[i];
                        if (controller == null) continue;

                        IntPtr paramControllerPtr = controller.Pointer;
                        if (paramControllerPtr == IntPtr.Zero) continue;

                        // Read type at offset 0x18
                        int paramType;
                        unsafe
                        {
                            paramType = *(int*)((byte*)paramControllerPtr + OFFSET_PARAMETER_TYPE);
                        }

                        if (paramType == PARAMETER_TYPE_ACCURACY_RATE)
                        {

                            // Read view pointer at offset 0x20
                            IntPtr viewPtr;
                            unsafe
                            {
                                viewPtr = *(IntPtr*)((byte*)paramControllerPtr + OFFSET_PARAMETER_VIEW);
                            }

                            if (viewPtr == IntPtr.Zero) continue;

                            // Read multipliedValueText at offset 0x28 (this is the "12" in "12x 99%")
                            IntPtr textPtr;
                            unsafe
                            {
                                textPtr = *(IntPtr*)((byte*)viewPtr + OFFSET_MULTIPLIED_VALUE_TEXT);
                            }

                            if (textPtr != IntPtr.Zero)
                            {
                                var text = new Text(textPtr);
                                if (text != null)
                                {
                                    string valueStr = text.text;
                                    if (!string.IsNullOrEmpty(valueStr) && int.TryParse(valueStr.Trim(), out int value))
                                    {
                                        cachedAccuracyCount = value;
                                        MelonLogger.Msg($"[StatusDetails] Cached accuracy count from AccuracyRate view: {cachedAccuracyCount}");
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[StatusDetails] Error checking parameter controller {i}: {ex.Message}");
                    }
                }

                MelonLogger.Warning("[StatusDetails] AccuracyRate controller not found in contentList");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[StatusDetails] Error caching accuracy count: {ex.Message}");
            }
        }

        // NOTE: Fallback methods have been intentionally removed.
        // Fallbacks that provide incorrect information are worse than no information.
        // Better to return "N/A" than misleading data that detracts from playability.
        // DO NOT re-implement fallback calculations without verifying they match visual display.

        /// <summary>
        /// Get weapon skill data from cache.
        /// Returns (level, percentage) or (-1, -1) if not found.
        /// Cache is populated via PopulateUICache() when status screen opens.
        /// </summary>
        private static (int level, int percentage) GetWeaponSkillFromCache(SkillLevelTarget skillType)
        {
            if (weaponSkillCache.TryGetValue((int)skillType, out var data))
            {
                return data;
            }
            return (-1, -1);
        }

        // NOTE: Combat stats (Accuracy, Evasion, Magic Defense) now use direct API calls
        // like FF3, instead of UI caching. This is simpler and more reliable.
        // See ReadAccuracy(), ReadEvasion(), ReadMagicDefense() for implementation.

        #endregion

        /// <summary>
        /// Initialize the stat list with all visible stats in UI order.
        /// FF2-specific stats: 23 total (no job, no level, no XP - all growth is usage-based)
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

            // Weapon Skills Group (indices 15-22) - FF2 specific
            // Order matches status screen and SkillLevelTarget enum (0-7):
            // Sword, Knife, Spear, Axe, Staff, Bow, Shield, Unarmed
            statList.Add(new StatusStatDefinition("Sword", StatGroup.WeaponSkills, ReadSwordSkill));
            statList.Add(new StatusStatDefinition("Knife", StatGroup.WeaponSkills, ReadKnifeSkill));
            statList.Add(new StatusStatDefinition("Spear", StatGroup.WeaponSkills, ReadSpearSkill));
            statList.Add(new StatusStatDefinition("Axe", StatGroup.WeaponSkills, ReadAxeSkill));
            statList.Add(new StatusStatDefinition("Staff", StatGroup.WeaponSkills, ReadStaffSkill));
            statList.Add(new StatusStatDefinition("Bow", StatGroup.WeaponSkills, ReadBowSkill));
            statList.Add(new StatusStatDefinition("Shield", StatGroup.WeaponSkills, ReadShieldSkill));
            statList.Add(new StatusStatDefinition("Unarmed", StatGroup.WeaponSkills, ReadUnarmedSkill));
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
                if (data?.Parameter == null) return "Accuracy: N/A";
                // FF2: Accuracy displayed as "Nx Y%" (e.g., "12x 99%")
                // Accuracy count comes from equipped weapons, not base stats.
                // ConfirmedAccuracyCount() returns 0 because BaseAccuracyCount is never initialized.
                // Read count from UI cache via AccuracyRate controller's multipliedValueText.
                int count = cachedAccuracyCount;
                int rate = data.Parameter.ConfirmedAccuracyRate(false);

                if (count > 0)
                {
                    return $"Accuracy: {count}x {rate} percent";
                }
                else
                {
                    // Fallback to just rate if count unavailable
                    return $"Accuracy: {rate} percent";
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Accuracy: {ex.Message}");
                return "Accuracy: N/A";
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
                if (data?.Parameter == null) return "Evasion: N/A";
                // FF2: Evasion displayed as "Nx Y%" (e.g., "4x 17%")
                // Use direct API calls like FF3 - simpler and more reliable than UI cache
                int count = data.Parameter.ConfirmedEvasionCount();
                int rate = data.Parameter.ConfirmedEvasionRate(false);
                return $"Evasion: {count}x {rate} percent";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Evasion: {ex.Message}");
                return "Evasion: N/A";
            }
        }

        private static string ReadMagicDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "Magic Defense: N/A";
                // FF2: Magic Defense displayed as "Nx Y%" (e.g., "7x 77%")
                // Use direct API calls like FF3 - simpler and more reliable than UI cache
                int count = data.Parameter.ConfirmedMagicDefenseCount();
                // ConfirmedAbilityDefense() returns the rate value (confusing naming)
                int rate = data.Parameter.ConfirmedAbilityDefense();
                return $"Magic Defense: {count}x {rate} percent";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Defense: {ex.Message}");
                return "Magic Defense: N/A";
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
        /// Read a weapon skill level with progress percentage.
        /// Format: "skill name: Level X, Y percent" (e.g., "Sword: Level 9, 45 percent")
        /// Level and percentage read directly from UI components.
        /// </summary>
        private static string ReadWeaponSkill(OwnedCharacterData data, SkillLevelTarget skillType, string skillName)
        {
            try
            {
                if (data == null) return $"{skillName}: N/A";

                // Try to read from UI cache first (matches visual display exactly)
                var (uiLevel, uiPercentage) = GetWeaponSkillFromCache(skillType);

                if (uiLevel > 0)
                {
                    // Successfully read from UI
                    if (uiPercentage >= 0)
                    {
                        return $"{skillName}: Level {uiLevel}, {uiPercentage} percent";
                    }
                    return $"{skillName}: Level {uiLevel}";
                }

                // NO FALLBACK - incorrect data is worse than no data
                // DO NOT re-implement fallback calculations without verifying they match visual display
                return $"{skillName}: N/A";
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
            return ReadWeaponSkill(data, SkillLevelTarget.WeaponWrestle, "Unarmed");
        }

        // Note: Evasion skills (PhysicalAvoidance, AbilityAvoidance) are intentionally NOT included
        // as weapon skills. They don't have exp bars - only stat gains announced in battle results.

        #endregion
    }
}
