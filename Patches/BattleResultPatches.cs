using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

// Type aliases for FF2
using BattleResultData = Il2CppLast.Data.BattleResultData;
using BattleResultCharacterData = Il2CppLast.Data.BattleResultData.BattleResultCharacterData;
using ResultMenuController = Il2CppLast.UI.KeyInput.ResultMenuController;
using ListItemFormatter = Il2CppLast.Management.ListItemFormatter;
using MessageManager = Il2CppLast.Management.MessageManager;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using PlayerCharacterParameter = Il2CppLast.Data.PlayerCharacterParameter;
using SkillLevelTarget = Il2CppLast.Defaine.SkillLevelTarget;
using ExpUtility = Il2CppLast.Systems.ExpUtility;
using ExpTableType = Il2CppLast.Defaine.Master.ExpTableType;
using BattleUtility = Il2CppLast.Battle.BattleUtility;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for battle result announcements (XP, gil, items, stat gains, ability/skill exp).
    /// FF2-specific implementation:
    /// - Stats grow through use (HP/MP from damage, weapon skills from use)
    /// - Abilities level up through casting (Fire, Cure, etc.)
    /// - Weapon skills level up through weapon use (Sword, Axe, etc.)
    /// </summary>
    public static class BattleResultPatches
    {
        // Track what we've announced to prevent duplicates
        private static BattleResultData lastAnnouncedData = null;
        private static bool announcedPoints = false;
        private static bool announcedItems = false;
        private static bool announcedWeaponSkills = false;
        private static bool announcedStatGains = false;

        /// <summary>
        /// Apply all battle result patches manually.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[BattleResult] Applying battle result patches...");

                // Patch BOTH KeyInput and Touch variants of ResultMenuController
                PatchResultMenuController(harmony, typeof(ResultMenuController), "KeyInput");

                var touchResultMenuType = AccessTools.TypeByName("Il2CppLast.UI.Touch.ResultMenuController");
                if (touchResultMenuType != null)
                {
                    PatchResultMenuControllerByType(harmony, touchResultMenuType, "Touch");
                }
                else
                {
                    MelonLogger.Warning("[BattleResult] Could not find Touch.ResultMenuController");
                }

                MelonLogger.Msg("[BattleResult] Battle result patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ResultMenuController methods using the type alias (KeyInput).
        /// </summary>
        private static void PatchResultMenuController(HarmonyLib.Harmony harmony, Type controllerType, string variant)
        {
            PatchResultMenuControllerByType(harmony, controllerType, variant);
        }

        /// <summary>
        /// Patch ResultMenuController methods by type.
        /// </summary>
        private static void PatchResultMenuControllerByType(HarmonyLib.Harmony harmony, Type controllerType, string variant)
        {
            MelonLogger.Msg($"[BattleResult] Patching {variant} ResultMenuController: {controllerType.FullName}");

            // Patch ShowPointsInit
            var showPointsInitMethod = AccessTools.Method(controllerType, "ShowPointsInit");
            if (showPointsInitMethod != null)
            {
                var postfix = AccessTools.Method(typeof(BattleResultPatches), nameof(ShowPointsInit_Postfix_Generic));
                harmony.Patch(showPointsInitMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg($"[BattleResult] Patched ShowPointsInit ({variant})");
            }
            else
            {
                MelonLogger.Warning($"[BattleResult] Could not find ShowPointsInit on {variant}");
            }

            // Patch ShowGetItemsInit
            var showGetItemsInitMethod = AccessTools.Method(controllerType, "ShowGetItemsInit");
            if (showGetItemsInitMethod != null)
            {
                var postfix = AccessTools.Method(typeof(BattleResultPatches), nameof(ShowGetItemsInit_Postfix_Generic));
                harmony.Patch(showGetItemsInitMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg($"[BattleResult] Patched ShowGetItemsInit ({variant})");
            }

            // Patch Show
            var showMethod = AccessTools.Method(controllerType, "Show");
            if (showMethod != null)
            {
                var postfix = AccessTools.Method(typeof(BattleResultPatches), nameof(Show_Postfix));
                harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg($"[BattleResult] Patched Show ({variant})");
            }

            // Patch ShowLevelUpAbilitysInit
            var showLevelUpAbilitysInitMethod = AccessTools.Method(controllerType, "ShowLevelUpAbilitysInit");
            if (showLevelUpAbilitysInitMethod != null)
            {
                var postfix = AccessTools.Method(typeof(BattleResultPatches), nameof(ShowLevelUpAbilitysInit_Postfix_Generic));
                harmony.Patch(showLevelUpAbilitysInitMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg($"[BattleResult] Patched ShowLevelUpAbilitysInit ({variant})");
            }

            // Patch ShowSkillLevelsInit - Weapon skill level-ups (State = 3)
            var showSkillLevelsInitMethod = AccessTools.Method(controllerType, "ShowSkillLevelsInit");
            if (showSkillLevelsInitMethod != null)
            {
                var postfix = AccessTools.Method(typeof(BattleResultPatches), nameof(ShowSkillLevelsInit_Postfix_Generic));
                harmony.Patch(showSkillLevelsInitMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg($"[BattleResult] Patched ShowSkillLevelsInit ({variant})");
            }

            // Patch ShowStatusUpInit - Stat gains (HP, Evasion, Magic Evasion, etc.)
            var showStatusUpInitMethod = AccessTools.Method(controllerType, "ShowStatusUpInit");
            if (showStatusUpInitMethod != null)
            {
                var postfix = AccessTools.Method(typeof(BattleResultPatches), nameof(ShowStatusUpInit_Postfix_Generic));
                harmony.Patch(showStatusUpInitMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg($"[BattleResult] Patched ShowStatusUpInit ({variant})");
            }
        }

        /// <summary>
        /// Reset tracking when a new battle result starts
        /// </summary>
        public static void ResetTracking(BattleResultData data)
        {
            if (data != lastAnnouncedData)
            {
                lastAnnouncedData = data;
                announcedPoints = false;
                announcedItems = false;
                announcedWeaponSkills = false;
                announcedStatGains = false;
            }
        }

        #region Generic Postfix Methods (work with both Touch and KeyInput)

        /// <summary>
        /// Generic ShowPointsInit postfix that works with any ResultMenuController variant.
        /// </summary>
        public static void ShowPointsInit_Postfix_Generic(object __instance)
        {
            try
            {
                dynamic controller = __instance;
                BattleResultData data = controller.targetData;

                if (data != null)
                    AnnouncePointsGained(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowPointsInit_Generic patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Generic ShowGetItemsInit postfix.
        /// </summary>
        public static void ShowGetItemsInit_Postfix_Generic(object __instance)
        {
            try
            {
                dynamic controller = __instance;
                BattleResultData data = controller.targetData;

                if (data != null)
                    AnnounceItemsDropped(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowGetItemsInit_Generic patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Generic ShowLevelUpAbilitysInit postfix.
        /// KeyInput controller uses this for BOTH magic level-ups AND weapon skills.
        /// Touch controller uses ShowSkillLevelsInit for weapon skills instead.
        /// </summary>
        public static void ShowLevelUpAbilitysInit_Postfix_Generic(object __instance)
        {
            try
            {
                MelonLogger.Msg($"[BattleResult] ShowLevelUpAbilitysInit_Postfix_Generic fired for {__instance?.GetType().Name}");

                dynamic controller = __instance;
                BattleResultData data = controller.targetData;

                if (data != null)
                {
                    // Announce magic/ability level-ups
                    AnnounceAbilityLevelUps(data);

                    // For KeyInput controller, also announce weapon skills here
                    // (Touch controller uses ShowSkillLevelsInit instead)
                    if (!announcedWeaponSkills)
                    {
                        announcedWeaponSkills = true;
                        MelonLogger.Msg("[BattleResult] Announcing weapon skills from ShowLevelUpAbilitysInit (KeyInput path)");
                        AnnounceAllWeaponSkills(data);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowLevelUpAbilitysInit_Generic patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Generic ShowSkillLevelsInit postfix - announces weapon skill progress.
        /// This fires when the weapon skill level-up phase begins (State = 3).
        /// At this point, GrouthWeaponSkillList is populated with skills that gained exp.
        /// </summary>
        public static void ShowSkillLevelsInit_Postfix_Generic(object __instance)
        {
            try
            {
                MelonLogger.Msg($"[BattleResult] ShowSkillLevelsInit_Postfix_Generic fired for {__instance?.GetType().Name}");

                // Prevent double announcements
                if (announcedWeaponSkills)
                {
                    MelonLogger.Msg("[BattleResult] Weapon skills already announced, skipping");
                    return;
                }
                announcedWeaponSkills = true;

                dynamic controller = __instance;
                BattleResultData data = controller.targetData;

                if (data != null)
                {
                    AnnounceAllWeaponSkills(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowSkillLevelsInit_Generic patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Generic ShowStatusUpInit postfix - announces stat gains (HP, Evasion, Magic Evasion, etc.).
        /// This fires when the stat gain phase begins (IsStatusUp characters show their gains).
        /// </summary>
        public static void ShowStatusUpInit_Postfix_Generic(object __instance)
        {
            try
            {
                MelonLogger.Msg($"[BattleResult] ShowStatusUpInit_Postfix_Generic fired for {__instance?.GetType().Name}");

                // Prevent double announcements
                if (announcedStatGains)
                {
                    MelonLogger.Msg("[BattleResult] Stat gains already announced, skipping");
                    return;
                }
                announcedStatGains = true;

                dynamic controller = __instance;
                BattleResultData data = controller.targetData;

                if (data != null)
                {
                    AnnounceAllStatGains(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowStatusUpInit_Generic patch: {ex.Message}");
            }
        }

        #endregion

        #region ShowPointsInit - Experience, Gil, Skill Exp

        public static void ShowPointsInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                MelonLogger.Msg("[BattleResult] ShowPointsInit_Postfix fired");
                // IL2CppInterop exposes private fields as public properties - access directly
                var data = __instance.targetData;
                if (data != null)
                {
                    MelonLogger.Msg("[BattleResult] Got targetData successfully");
                    AnnouncePointsGained(data);
                }
                else
                {
                    MelonLogger.Warning("[BattleResult] targetData is null in ShowPointsInit");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowPointsInit patch: {ex.Message}");
            }
        }

        private static void AnnouncePointsGained(BattleResultData data)
        {
            if (data == null || announcedPoints) return;

            ResetTracking(data);
            announcedPoints = true;

            var parts = new List<string>();

            // Gil gained - this is the total shown in phase 1
            try
            {
                int gil = data.GetGil;
                MelonLogger.Msg($"[BattleResult] Gil value: {gil}");
                if (gil > 0)
                {
                    parts.Add($"{gil:N0} gil");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error getting gil: {ex.Message}");
            }

            if (parts.Count > 0)
            {
                string announcement = "Gained " + string.Join(", ", parts);
                MelonLogger.Msg($"[Victory] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }

            // NOTE: Weapon skills are announced in ShowSkillLevelsInit_Postfix_Generic
            // because GrouthWeaponSkillList is not populated until that phase
        }

        /// <summary>
        /// Announce weapon skills that gained exp, showing percentage delta (bar growth) and level-ups.
        /// Format for exp gain: "Firion Sword: 30 percent"
        /// Format for level up: "Firion Sword leveled up to 3"
        ///
        /// NOTE: GrouthWeaponSkillList only contains skills that LEVELED UP, not all skills that gained exp.
        /// So we compare BeforData vs AfterData SkillLevelTargets to find ALL skills that gained exp.
        /// Uses BattleUtility.GetSkillLevel for accurate level calculation.
        /// </summary>
        private static void AnnounceWeaponSkillProgress(BattleResultCharacterData charResult)
        {
            try
            {
                var afterData = charResult.AfterData;
                var beforeData = charResult.BeforData;
                if (afterData == null)
                {
                    MelonLogger.Warning("[BattleResult] AfterData is null");
                    return;
                }

                string charName = afterData.Name;
                if (string.IsNullOrEmpty(charName)) return;

                var afterSkills = afterData.SkillLevelTargets;
                var beforeSkills = beforeData?.SkillLevelTargets;

                if (afterSkills == null)
                {
                    MelonLogger.Warning($"[BattleResult] {charName}: afterSkills is null");
                    return;
                }

                MelonLogger.Msg($"[BattleResult] {charName}: Checking {afterSkills.Count} skill types");

                int skillsWithGrowth = 0;

                // Iterate through all skills in afterData and compare with beforeData
                foreach (var kvp in afterSkills)
                {
                    try
                    {
                        var skillTarget = kvp.Key;

                        // Skip evasion skills - they don't have exp bars, only stat gains
                        // Evasion gains are announced separately in ShowStatusUpInit phase
                        if (skillTarget == SkillLevelTarget.PhysicalAvoidance ||
                            skillTarget == SkillLevelTarget.AbilityAvoidance)
                        {
                            continue;
                        }

                        int afterExp = kvp.Value;
                        int beforeExp = 0;

                        if (beforeSkills != null && beforeSkills.ContainsKey(skillTarget))
                        {
                            beforeExp = beforeSkills[skillTarget];
                        }

                        // Only announce if exp actually increased
                        if (afterExp <= beforeExp) continue;

                        skillsWithGrowth++;
                        string skillName = GetWeaponSkillName(skillTarget);
                        int expGained = afterExp - beforeExp;

                        // Use BattleUtility.GetSkillLevel for accurate level calculation
                        int beforeLevel = beforeData != null ? BattleUtility.GetSkillLevel(beforeData, skillTarget) : 1;
                        int afterLevel = BattleUtility.GetSkillLevel(afterData, skillTarget);

                        MelonLogger.Msg($"[BattleResult] {charName} {skillName}: {beforeExp} -> {afterExp} (+{expGained}), level {beforeLevel}->{afterLevel}");

                        // Calculate percentage for display
                        // Each level requires 100 exp, so exp % 100 gives 0-99 directly as percentage
                        int beforePercent = beforeExp % 100;
                        int afterPercent = afterExp % 100;

                        // Check if level increased
                        bool leveledUp = afterLevel > beforeLevel;

                        if (leveledUp)
                        {
                            // Level-ups are announced in AnnounceWeaponSkillLevelUps during stat gain phase
                            // Just log here for debugging
                            MelonLogger.Msg($"[BattleResult] {charName} {skillName} leveled up to {afterLevel} (will announce in stat phase)");
                        }
                        else
                        {
                            // No level up - announce percentage gained
                            int percentDelta = afterPercent - beforePercent;
                            MelonLogger.Msg($"[BattleResult] {charName} {skillName}: percentDelta={percentDelta}");
                            if (percentDelta > 0)
                            {
                                string announcement = $"{charName} {skillName}: {percentDelta} percent";
                                MelonLogger.Msg($"[Victory] {announcement}");
                                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error announcing skill: {ex.Message}");
                    }
                }

                if (skillsWithGrowth == 0)
                {
                    MelonLogger.Msg($"[BattleResult] {charName}: No skill exp gained this battle");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AnnounceWeaponSkillProgress: {ex.Message}");
            }
        }

        /// <summary>
        /// Format a percentage announcement with appropriate precision.
        /// Uses integer for >= 1%, one decimal for smaller values.
        /// </summary>
        private static string FormatPercentAnnouncement(string charName, string skillName, float percent)
        {
            if (percent >= 1f)
            {
                // Round to nearest integer for 1% or higher
                return $"{charName} {skillName}: {(int)Math.Round(percent)} percent";
            }
            else
            {
                // Use one decimal place for sub-1% gains
                return $"{charName} {skillName}: {percent:F1} percent";
            }
        }

        /// <summary>
        /// Calculate percentage progress within a level.
        /// Each level requires 100 exp, so exp % 100 gives 0-99 directly as percentage.
        /// </summary>
        private static int CalculatePercentInLevel(int exp)
        {
            // Simple formula: each level is 100 exp, so modulo gives percentage
            return exp % 100;
        }

        private static string GetWeaponSkillName(SkillLevelTarget target)
        {
            return target switch
            {
                SkillLevelTarget.WeaponSword => "Sword",
                SkillLevelTarget.WeaponKnife => "Knife",
                SkillLevelTarget.WeaponSpear => "Spear",
                SkillLevelTarget.WeaponAxe => "Axe",
                SkillLevelTarget.WeaponCane => "Staff",
                SkillLevelTarget.WeaponBow => "Bow",
                SkillLevelTarget.WeaponShield => "Shield",
                SkillLevelTarget.WeaponWrestle => "Unarmed",
                SkillLevelTarget.PhysicalAvoidance => "Evasion",
                SkillLevelTarget.AbilityAvoidance => "Magic Defense",
                _ => target.ToString()
            };
        }

        #endregion

        #region ShowLevelUpAbilitysInit - Ability Level-Ups

        public static void ShowLevelUpAbilitysInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                MelonLogger.Msg("[BattleResult] ShowLevelUpAbilitysInit_Postfix fired");
                var data = __instance.targetData;
                if (data != null)
                {
                    AnnounceAbilityLevelUps(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowLevelUpAbilitysInit patch: {ex.Message}");
            }
        }

        private static void AnnounceAbilityLevelUps(BattleResultData data)
        {
            try
            {
                var characterList = data.CharacterList;
                if (characterList == null) return;

                foreach (var charResult in characterList)
                {
                    if (charResult == null) continue;
                    AnnounceCharacterAbilityLevelUps(charResult);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AnnounceAbilityLevelUps: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce abilities that leveled up this battle.
        /// Format: "Firion, Fire level up!"
        /// </summary>
        private static void AnnounceCharacterAbilityLevelUps(BattleResultCharacterData charResult)
        {
            try
            {
                var afterData = charResult.AfterData;
                var beforeData = charResult.BeforData;
                if (afterData == null) return;

                string charName = afterData.Name;
                if (string.IsNullOrEmpty(charName)) return;

                // Get abilities that changed this battle
                var abilityList = charResult.AbilityList;
                if (abilityList == null || abilityList.Count == 0) return;

                MelonLogger.Msg($"[BattleResult] {charName}: {abilityList.Count} abilities in list");

                // Get before/after ability lists for comparison
                var afterAbilities = afterData.OwnedAbilityList;
                var beforeAbilities = beforeData?.OwnedAbilityList;

                foreach (var ability in abilityList)
                {
                    if (ability == null) continue;

                    try
                    {
                        // Get ability name
                        string abilityName = null;
                        string mesIdName = ability.MesIdName;
                        if (!string.IsNullOrEmpty(mesIdName))
                        {
                            var messageManager = MessageManager.Instance;
                            if (messageManager != null)
                            {
                                abilityName = messageManager.GetMessage(mesIdName);
                            }
                        }

                        if (string.IsNullOrEmpty(abilityName))
                        {
                            continue; // Skip abilities without names
                        }

                        // Check if this ability leveled up by comparing before/after
                        // OwnedAbility.SkillLevel stores raw exp, formula: level = (exp / 100) + 1
                        int afterExp = ability.SkillLevel;
                        int beforeExp = 0;

                        if (beforeAbilities != null)
                        {
                            // Find matching ability in before list
                            foreach (var beforeAbility in beforeAbilities)
                            {
                                if (beforeAbility != null && beforeAbility.MesIdName == mesIdName)
                                {
                                    beforeExp = beforeAbility.SkillLevel;
                                    break;
                                }
                            }
                        }

                        // Convert raw exp to actual level
                        int afterLevel = (afterExp / 100) + 1;
                        int beforeLevel = (beforeExp / 100) + 1;
                        if (afterLevel > 16) afterLevel = 16;
                        if (beforeLevel < 1) beforeLevel = 1;

                        // Only announce if level actually increased
                        if (afterLevel > beforeLevel)
                        {
                            string announcement = $"{charName}, {abilityName} lv{afterLevel}!";
                            MelonLogger.Msg($"[Victory] {announcement}");
                            FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error announcing ability: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AnnounceCharacterAbilityLevelUps: {ex.Message}");
            }
        }

        #endregion

        #region ShowGetItemsInit - Item Drops

        public static void ShowGetItemsInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                MelonLogger.Msg("[BattleResult] ShowGetItemsInit_Postfix fired");
                var data = __instance.targetData;
                if (data != null)
                {
                    AnnounceItemsDropped(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowGetItemsInit patch: {ex.Message}");
            }
        }

        private static void AnnounceItemsDropped(BattleResultData data)
        {
            if (data == null || announcedItems) return;

            ResetTracking(data);
            announcedItems = true;

            var itemList = data.ItemList;
            if (itemList == null || itemList.Count == 0) return;

            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                // Convert drop items to localized content data
                var contentDataList = ListItemFormatter.GetContentDataList(itemList, messageManager);
                if (contentDataList == null || contentDataList.Count == 0) return;

                foreach (var itemContent in contentDataList)
                {
                    if (itemContent == null) continue;

                    string itemName = itemContent.Name;
                    if (string.IsNullOrEmpty(itemName)) continue;

                    // Strip any icon markup
                    itemName = TextUtils.StripIconMarkup(itemName);
                    if (string.IsNullOrEmpty(itemName)) continue;

                    string announcement;
                    int count = itemContent.Count;
                    if (count > 1)
                    {
                        announcement = $"Found {itemName} x{count}";
                    }
                    else
                    {
                        announcement = $"Found {itemName}";
                    }

                    MelonLogger.Msg($"[Victory] {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing items: {ex.Message}");
            }
        }

        #endregion

        #region ShowStatusUpInit - Stat Gains (HP, Evasion, Magic Evasion, etc.)

        /// <summary>
        /// Announce stat gains and weapon skill level-ups for all characters.
        /// Combined format: "Firion: Sword lv2, Strength +1, HP +5"
        /// </summary>
        private static void AnnounceAllStatGains(BattleResultData data)
        {
            try
            {
                var characterList = data.CharacterList;
                if (characterList == null)
                {
                    MelonLogger.Warning("[BattleResult] CharacterList is null for stat gains");
                    return;
                }

                MelonLogger.Msg($"[BattleResult] Checking stat gains for {characterList.Count} characters");
                foreach (var charResult in characterList)
                {
                    if (charResult == null) continue;
                    AnnounceCharacterStatGains(charResult);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error in AnnounceAllStatGains: {ex.Message}");
            }
        }

        /// <summary>
        /// Get weapon skill level-ups for a character using BattleUtility.GetSkillLevel for accurate level calculation.
        /// Returns list of strings like "Sword lv2" for each skill that leveled up.
        /// </summary>
        private static List<string> GetWeaponSkillLevelUps(BattleResultCharacterData charResult)
        {
            var levelUps = new List<string>();

            try
            {
                var afterData = charResult.AfterData;
                var beforeData = charResult.BeforData;
                if (afterData == null || beforeData == null) return levelUps;

                // Use GrouthWeaponSkillList if available - it contains the exact skills that leveled up
                var grouthList = charResult.GrouthWeaponSkillList;
                if (grouthList != null && grouthList.Count > 0)
                {
                    MelonLogger.Msg($"[BattleResult] Using GrouthWeaponSkillList with {grouthList.Count} skills");
                    foreach (var skillTarget in grouthList)
                    {
                        try
                        {
                            // Skip evasion skills - they don't have exp bars/level-ups
                            if (skillTarget == SkillLevelTarget.PhysicalAvoidance ||
                                skillTarget == SkillLevelTarget.AbilityAvoidance)
                            {
                                continue;
                            }

                            // Use BattleUtility.GetSkillLevel for accurate level
                            int afterLevel = BattleUtility.GetSkillLevel(afterData, skillTarget);
                            string skillName = GetWeaponSkillName(skillTarget);
                            MelonLogger.Msg($"[BattleResult] {skillName} leveled up to {afterLevel}");
                            levelUps.Add($"{skillName} lv{afterLevel}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Error getting skill level: {ex.Message}");
                        }
                    }
                    return levelUps;
                }

                // Fallback: compare before/after skill exp using BattleUtility
                var afterSkills = afterData.SkillLevelTargets;
                var beforeSkills = beforeData.SkillLevelTargets;

                if (afterSkills == null || beforeSkills == null) return levelUps;

                // Check each skill for level-ups
                foreach (var kvp in afterSkills)
                {
                    try
                    {
                        var skillTarget = kvp.Key;

                        // Skip evasion skills - they don't have exp bars/level-ups
                        if (skillTarget == SkillLevelTarget.PhysicalAvoidance ||
                            skillTarget == SkillLevelTarget.AbilityAvoidance)
                        {
                            continue;
                        }

                        int afterExp = kvp.Value;
                        int beforeExp = 0;

                        if (beforeSkills.ContainsKey(skillTarget))
                        {
                            beforeExp = beforeSkills[skillTarget];
                        }

                        // Only check skills that gained exp
                        if (afterExp <= beforeExp) continue;

                        // Use BattleUtility.GetSkillLevel for accurate levels
                        int beforeLevel = BattleUtility.GetSkillLevel(beforeData, skillTarget);
                        int afterLevel = BattleUtility.GetSkillLevel(afterData, skillTarget);

                        string skillName = GetWeaponSkillName(skillTarget);
                        MelonLogger.Msg($"[BattleResult] {skillName}: exp {beforeExp}->{afterExp}, level {beforeLevel}->{afterLevel}");

                        // Add if level increased
                        if (afterLevel > beforeLevel)
                        {
                            levelUps.Add($"{skillName} lv{afterLevel}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error checking skill level-up: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error in GetWeaponSkillLevelUps: {ex.Message}");
            }

            return levelUps;
        }

        /// <summary>
        /// Announce stat gains and weapon level-ups for a single character.
        /// Format: "Firion: Sword lv2, Strength +1, HP +5"
        /// Weapon level-ups come first, then stat changes.
        /// </summary>
        private static void AnnounceCharacterStatGains(BattleResultCharacterData charResult)
        {
            try
            {
                var beforeData = charResult.BeforData;
                var afterData = charResult.AfterData;

                if (beforeData == null || afterData == null)
                {
                    MelonLogger.Warning("[BattleResult] BeforData or AfterData is null");
                    return;
                }

                string charName = afterData.Name;
                if (string.IsNullOrEmpty(charName))
                {
                    MelonLogger.Warning("[BattleResult] Character name is empty");
                    return;
                }

                // Build combined list: weapon level-ups first, then stat changes
                var allChanges = new List<string>();

                // Get weapon skill level-ups first
                var weaponLevelUps = GetWeaponSkillLevelUps(charResult);
                allChanges.AddRange(weaponLevelUps);

                // Only check stat changes if IsStatusUp flag is set
                if (charResult.IsStatusUp)
                {
                    var beforeParam = beforeData.Parameter;
                    var afterParam = afterData.Parameter;

                    if (beforeParam != null && afterParam != null)
                    {
                        // Check all relevant stats
                        // Base stats
                        CheckStatChange(allChanges, "HP", beforeParam.AddtionalMaxHp, afterParam.AddtionalMaxHp);
                        CheckStatChange(allChanges, "MP", beforeParam.AddtionalMaxMp, afterParam.AddtionalMaxMp);
                        CheckStatChange(allChanges, "Strength", beforeParam.AddtionalPower, afterParam.AddtionalPower);
                        CheckStatChange(allChanges, "Vitality", beforeParam.AddtionalVitality, afterParam.AddtionalVitality);
                        CheckStatChange(allChanges, "Agility", beforeParam.AddtionalAgility, afterParam.AddtionalAgility);
                        CheckStatChange(allChanges, "Intelligence", beforeParam.AddtionalIntelligence, afterParam.AddtionalIntelligence);
                        CheckStatChange(allChanges, "Spirit", beforeParam.AddtionalSpirit, afterParam.AddtionalSpirit);

                        // Derived combat stats
                        CheckStatChange(allChanges, "Accuracy", beforeParam.AddtionalAccuracyRate, afterParam.AddtionalAccuracyRate);
                        CheckStatChange(allChanges, "Defense", beforeParam.AddtionalDefense, afterParam.AddtionalDefense);

                        // Evasion uses "Nx Y%" format on victory screen - track both count and rate
                        CheckStatChange(allChanges, "Evasion Count", beforeParam.AddtionalEvasionCount, afterParam.AddtionalEvasionCount);
                        CheckStatChange(allChanges, "Evasion", beforeParam.AddtionalEvasionRate, afterParam.AddtionalEvasionRate);

                        // Magic Defense uses "Nx Y%" format on victory screen - track both count and rate
                        CheckStatChange(allChanges, "Magic Defense Count", beforeParam.AddtionalMagicDefenseCount, afterParam.AddtionalMagicDefenseCount);
                        CheckStatChange(allChanges, "Magic Defense", beforeParam.AddtionalAbilityDefenseRate, afterParam.AddtionalAbilityDefenseRate);

                        // Magic Evasion
                        CheckStatChange(allChanges, "Magic Evasion", beforeParam.AddtionalAbilityEvasionRate, afterParam.AddtionalAbilityEvasionRate);
                    }
                }

                if (allChanges.Count > 0)
                {
                    // Format: "Firion: Sword lv2, Strength +1, HP +5"
                    string announcement = $"{charName}: {string.Join(", ", allChanges)}";
                    MelonLogger.Msg($"[Victory] {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
                else
                {
                    MelonLogger.Msg($"[BattleResult] {charName}: No level-ups or stat changes");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error in AnnounceCharacterStatGains: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a stat changed and add to the list if it did.
        /// Format: "Strength +1" for use in "Firion: Strength +1, HP +5"
        /// </summary>
        private static void CheckStatChange(List<string> changes, string statName, int before, int after)
        {
            int delta = after - before;
            if (delta > 0)
            {
                changes.Add($"{statName} +{delta}");
                MelonLogger.Msg($"[BattleResult] Stat change: {statName} {before} -> {after} (+{delta})");
            }
            else if (delta < 0)
            {
                // Stats can decrease in FF2 (e.g., HP down from certain actions)
                changes.Add($"{statName} {delta}");
                MelonLogger.Msg($"[BattleResult] Stat change: {statName} {before} -> {after} ({delta})");
            }
        }

        #endregion

        #region Show - Main Entry Point for Battle Results

        /// <summary>
        /// Main entry point when battle result screen shows.
        /// Since ShowPointsInit patch doesn't fire reliably in IL2CPP,
        /// we announce gil directly from here.
        /// Weapon skills are announced in ShowSkillLevelsInit (fires later when data is populated).
        /// </summary>
        public static void Show_Postfix(BattleResultData data, bool isReverse)
        {
            try
            {
                if (data == null || isReverse) return;

                MelonLogger.Msg("[BattleResult] Show_Postfix fired - announcing gil");

                // Clear battle active flag - battle is now over
                FFII_ScreenReaderMod.ClearBattleActive();

                // Reset tracking for new battle result
                ResetTracking(data);

                // Announce gil directly (ShowPointsInit doesn't fire reliably)
                AnnounceGilGained(data);

                // Announce weapon skill percentage gains
                if (!announcedWeaponSkills)
                {
                    announcedWeaponSkills = true;
                    AnnounceAllWeaponSkills(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.Show patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce gil gained from battle.
        /// </summary>
        private static void AnnounceGilGained(BattleResultData data)
        {
            // Prevent double announcements (ShowPointsInit might also fire)
            if (announcedPoints)
            {
                MelonLogger.Msg("[BattleResult] Gil already announced, skipping");
                return;
            }
            announcedPoints = true;

            try
            {
                int gil = data.GetGil;
                MelonLogger.Msg($"[BattleResult] Gil value: {gil}");
                if (gil > 0)
                {
                    string announcement = $"Gained {gil:N0} gil";
                    MelonLogger.Msg($"[Victory] {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error getting gil: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce weapon skill progress for all characters.
        /// </summary>
        private static void AnnounceAllWeaponSkills(BattleResultData data)
        {
            try
            {
                var characterList = data.CharacterList;
                if (characterList == null)
                {
                    MelonLogger.Warning("[BattleResult] CharacterList is null");
                    return;
                }

                MelonLogger.Msg($"[BattleResult] CharacterList count: {characterList.Count}");
                foreach (var charResult in characterList)
                {
                    if (charResult == null) continue;
                    AnnounceWeaponSkillProgress(charResult);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] Error processing character list: {ex.Message}");
            }
        }

        #endregion
    }
}
