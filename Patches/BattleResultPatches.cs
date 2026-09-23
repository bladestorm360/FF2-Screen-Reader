using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// Type aliases for FF2
using BattleResultData = Il2CppLast.Data.BattleResultData;
using BattleResultCharacterData = Il2CppLast.Data.BattleResultData.BattleResultCharacterData;
using ResultMenuController = Il2CppLast.UI.KeyInput.ResultMenuController;
using ResultPointController = Il2CppSerial.FF2.UI.KeyInput.ResultPointController;
using ResultStatusUpController = Il2CppSerial.FF2.UI.KeyInput.ResultStatusUpController;
using ResultSkillController = Il2CppLast.UI.KeyInput.ResultSkillController;
using ListItemFormatter = Il2CppLast.Management.ListItemFormatter;
using MessageManager = Il2CppLast.Management.MessageManager;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using SkillLevelTarget = Il2CppLast.Defaine.SkillLevelTarget;
using ExpUtility = Il2CppLast.Systems.ExpUtility;
using ExpTableType = Il2CppLast.Defaine.Master.ExpTableType;
using BattleUtility = Il2CppLast.Battle.BattleUtility;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Battle result announcements, one hook per on-screen phase so each is spoken as it appears
    /// (KeyInput result UI; FF2 has no EXP, stats and skills grow through use):
    ///   ResultMenuController.Show                → reset + battle-state clear (no speech)
    ///   ResultPointController.ShowSkillLevels    → gil, then weapon-skill gains / level-ups, per character
    ///   ResultSkillController.SetLevelUpList     → spell level-ups, per character (same Init, right after)
    ///   ResultStatusUpController.SetData         → that character's stat gains, per page
    ///   ResultMenuController.ShowGetItemsInit    → item drops
    /// Show always starts at state ShowLevelUpAbilitys (BattleResultData.IsSkillLevel is always set),
    /// whose Init calls ShowSkillLevels then SetLevelUpList. ResultPointController.ShowPointList
    /// (ShowPoints state) never runs in FF2; its postfix is a gil fallback sharing the same guard.
    /// ResultSkillController.ShowLevelUp is NOT hooked: it has no direct caller in GameAssembly (its
    /// body is inlined into ResultMenuController.ShowLevelUpAbilitysInit, which calls SetLevelUpList).
    /// The Touch ResultMenuController is not hooked (its EndWaitInit is a shared empty stub).
    /// </summary>
    public static class BattleResultPatches
    {
        // Per-result guards (reset when a new BattleResultData is shown / a new battle starts).
        private static IntPtr lastResultPtr = IntPtr.Zero;
        private static bool announcedGil = false;
        private static bool announcedSkillLevels = false;
        private static bool announcedItems = false;
        private static bool announcedSpellLevels = false;
        private static int spellLevelUpCalls = 0;
        private static readonly HashSet<IntPtr> announcedStatusUps = new HashSet<IntPtr>();

        /// <summary>
        /// Apply all battle result patches manually.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            PatchPostfix(harmony, typeof(ResultMenuController), "Show", null, nameof(Show_Postfix));
            PatchPostfix(harmony, typeof(ResultPointController), "ShowPointList", null, nameof(ShowPointList_Postfix));
            PatchPostfix(harmony, typeof(ResultPointController), "ShowSkillLevels", null, nameof(ShowSkillLevels_Postfix));
            PatchPostfix(harmony, typeof(ResultStatusUpController), "SetData", null, nameof(StatusUpSetData_Postfix));
            PatchPostfix(harmony, typeof(ResultMenuController), "ShowGetItemsInit", Type.EmptyTypes, nameof(ShowGetItemsInit_Postfix));
            PatchPostfix(harmony, typeof(ResultSkillController), "SetLevelUpList", null, nameof(SetLevelUpList_Postfix));
        }

        private static void PatchPostfix(HarmonyLib.Harmony harmony, Type type, string method, Type[] args, string postfixName)
        {
            try
            {
                var target = AccessTools.DeclaredMethod(type, method, args);
                if (target != null)
                    harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(BattleResultPatches), postfixName)));
                else
                    MelonLogger.Error($"[BattleResult] {type.Name}.{method} not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error patching {type.Name}.{method}: {ex.Message}");
            }
        }

        /// <summary>Forgets the previous result so the next battle's phases all announce.</summary>
        public static void ResetForNewBattle()
        {
            lastResultPtr = IntPtr.Zero;
        }

        /// <summary>Resets the per-phase guards when a different result object is shown.</summary>
        private static void ResetTracking(BattleResultData data)
        {
            IntPtr ptr = data.Pointer;
            if (ptr == lastResultPtr) return;
            lastResultPtr = ptr;
            announcedGil = false;
            announcedSkillLevels = false;
            announcedItems = false;
            announcedSpellLevels = false;
            spellLevelUpCalls = 0;
            announcedStatusUps.Clear();
        }

        #region Show - reset

        /// <summary>
        /// The result screen opened: the battle is over. Clears battle state and latches the
        /// beacon-only BattleResultActive gate (the screen is still up over the field; cleared by the
        /// field transition / OnSceneLoaded). Speaks nothing — each phase speaks as it appears.
        /// isReverse is ignored: it is the back-attack layout flag (BattleController.StartWinResult),
        /// not a close, so back-attack wins must reset too.
        /// </summary>
        public static void Show_Postfix(BattleResultData data)
        {
            try
            {
                if (data == null) return;

                FFII_ScreenReaderMod.ClearBattleState();
                FFII_ScreenReaderMod.BattleResultActive = true;
                ResetTracking(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error in Show postfix: {ex.Message}");
            }
        }

        #endregion

        #region Gil

        /// <summary>
        /// Fallback only: ShowPointList never runs in FF2 (ShowPointsInit is its only caller and the
        /// result screen always starts at ShowLevelUpAbilitys, never at ShowPoints). Gil is spoken
        /// from ShowSkillLevels_Postfix; this shares its announcedGil guard.
        /// </summary>
        public static void ShowPointList_Postfix(BattleResultData __0)
        {
            try
            {
                var data = __0;
                if (data == null) return;
                ResetTracking(data);
                AnnounceGil(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error announcing gil: {ex.Message}");
            }
        }

        /// <summary>"Gained N gil", once per result (announcedGil guard).</summary>
        private static void AnnounceGil(BattleResultData data)
        {
            if (announcedGil) return;
            announcedGil = true;

            int gil = data.GetGil;
            if (gil > 0)
                FFII_ScreenReaderMod.SpeakText(string.Format(T("Gained {0} gil"), gil.ToString("N0")), interrupt: true);
        }

        #endregion

        #region ShowSkillLevels - gil + weapon skills

        /// <summary>
        /// First on-screen phase (KeyInput ResultMenuController.Show always starts at
        /// ShowLevelUpAbilitys, whose Init calls this): gil first, then per character every weapon
        /// skill that gained exp — "Sword lv3" when it leveled up, otherwise "Shield +12 percent"
        /// (gauge growth). Evasion / magic defense have no exp bar; their gains are part of the
        /// stat-gain pages.
        /// </summary>
        public static void ShowSkillLevels_Postfix(BattleResultData __0)
        {
            try
            {
                var data = __0;
                if (data == null) return;
                ResetTracking(data);
                try
                {
                    AnnounceGil(data);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[BattleResult] Error announcing gil: {ex.Message}");
                }
                if (announcedSkillLevels) return;
                announcedSkillLevels = true;

                var characterList = data.CharacterList;
                if (characterList == null) return;

                foreach (var charResult in characterList)
                {
                    if (charResult == null) continue;
                    string line = BuildWeaponSkillLine(charResult);
                    if (!string.IsNullOrEmpty(line))
                        FFII_ScreenReaderMod.SpeakText(line, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error announcing weapon skills: {ex.Message}");
            }
        }

        /// <summary>"Firion: Sword lv3, Shield +12 percent", or null when no skill gained exp.</summary>
        private static string BuildWeaponSkillLine(BattleResultCharacterData charResult)
        {
            var afterData = charResult.AfterData;
            var beforeData = charResult.BeforData;
            if (afterData == null) return null;

            string charName = afterData.Name;
            var afterSkills = afterData.SkillLevelTargets;
            if (string.IsNullOrEmpty(charName) || afterSkills == null) return null;
            var beforeSkills = beforeData?.SkillLevelTargets;

            var parts = new List<string>();
            foreach (var kvp in afterSkills)
            {
                try
                {
                    var skillTarget = kvp.Key;
                    if (skillTarget == SkillLevelTarget.PhysicalAvoidance || skillTarget == SkillLevelTarget.AbilityAvoidance)
                        continue;

                    int afterExp = kvp.Value;
                    int beforeExp = beforeSkills != null && beforeSkills.ContainsKey(skillTarget) ? beforeSkills[skillTarget] : 0;
                    if (afterExp <= beforeExp) continue;

                    string skillName = GetWeaponSkillName(skillTarget);
                    int beforeLevel = beforeData != null ? BattleUtility.GetSkillLevel(beforeData, skillTarget) : 1;
                    int afterLevel = BattleUtility.GetSkillLevel(afterData, skillTarget);

                    if (afterLevel > beforeLevel)
                    {
                        parts.Add(string.Format(T("{0} lv{1}"), skillName, afterLevel));
                    }
                    else
                    {
                        int percentDelta = CalculatePercentInLevel(afterExp) - CalculatePercentInLevel(beforeExp);
                        if (percentDelta > 0)
                            parts.Add(string.Format(T("{0} +{1} percent"), skillName, percentDelta));
                    }
                }
                catch { }
            }

            return parts.Count > 0 ? $"{charName}: {string.Join(", ", parts)}" : null;
        }

        /// <summary>
        /// Percentage progress within a level from the game's exp table (same as its gauge):
        /// progress = 1 - (expToNext / expDifference).
        /// </summary>
        private static int CalculatePercentInLevel(int exp)
        {
            try
            {
                int expToNext = ExpUtility.GetNextExp(1, exp, ExpTableType.LevelExp);
                int expDiff = ExpUtility.GetExpDifference(1, exp, ExpTableType.LevelExp);
                if (expDiff > 0)
                {
                    int progress = (int)((1.0f - ((float)expToNext / expDiff)) * 100);
                    return Math.Clamp(progress, 0, 99);
                }
            }
            catch { }

            // Fallback to old formula if ExpUtility fails
            return exp % 100;
        }

        internal static string GetWeaponSkillName(SkillLevelTarget target)
        {
            return target switch
            {
                SkillLevelTarget.WeaponSword => T("Sword"),
                SkillLevelTarget.WeaponKnife => T("Knife"),
                SkillLevelTarget.WeaponSpear => T("Spear"),
                SkillLevelTarget.WeaponAxe => T("Axe"),
                SkillLevelTarget.WeaponCane => T("Staff"),
                SkillLevelTarget.WeaponBow => T("Bow"),
                SkillLevelTarget.WeaponShield => T("Shield"),
                SkillLevelTarget.WeaponWrestle => T("Unarmed"),
                SkillLevelTarget.PhysicalAvoidance => T("Evasion"),
                SkillLevelTarget.AbilityAvoidance => T("Magic Defense"),
                _ => target.ToString()
            };
        }

        #endregion

        #region ResultStatusUpController.SetData - stat gains per page

        /// <summary>
        /// One stat-gain page per character (StatusUpInit shows the first, StatusUpUpdate / the click
        /// handler each next one). Announces that character's gains as the page appears; guarded per
        /// character so a re-render of the same page doesn't repeat.
        /// </summary>
        public static void StatusUpSetData_Postfix(BattleResultCharacterData __0)
        {
            try
            {
                var charResult = __0;
                if (charResult == null) return;
                if (!announcedStatusUps.Add(charResult.Pointer)) return;

                string line = BuildStatGainLine(charResult);
                if (!string.IsNullOrEmpty(line))
                    FFII_ScreenReaderMod.SpeakText(line, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error announcing stat gains: {ex.Message}");
            }
        }

        /// <summary>"Firion: HP +5, Strength +1", or null when nothing changed.</summary>
        private static string BuildStatGainLine(BattleResultCharacterData charResult)
        {
            var beforeParam = charResult.BeforData?.Parameter;
            var afterData = charResult.AfterData;
            var afterParam = afterData?.Parameter;
            if (beforeParam == null || afterParam == null) return null;

            string charName = afterData.Name;
            if (string.IsNullOrEmpty(charName)) return null;

            var changes = new List<string>();
            // Base stats
            CheckStatChange(changes, T("HP"), beforeParam.AddtionalMaxHp, afterParam.AddtionalMaxHp);
            CheckStatChange(changes, T("MP"), beforeParam.AddtionalMaxMp, afterParam.AddtionalMaxMp);
            CheckStatChange(changes, T("Strength"), beforeParam.AddtionalPower, afterParam.AddtionalPower);
            CheckStatChange(changes, T("Vitality"), beforeParam.AddtionalVitality, afterParam.AddtionalVitality);
            CheckStatChange(changes, T("Agility"), beforeParam.AddtionalAgility, afterParam.AddtionalAgility);
            CheckStatChange(changes, T("Intelligence"), beforeParam.AddtionalIntelligence, afterParam.AddtionalIntelligence);
            CheckStatChange(changes, T("Spirit"), beforeParam.AddtionalSpirit, afterParam.AddtionalSpirit);

            // Derived combat stats
            CheckStatChange(changes, T("Accuracy"), beforeParam.AddtionalAccuracyRate, afterParam.AddtionalAccuracyRate);
            CheckStatChange(changes, T("Defense"), beforeParam.AddtionalDefense, afterParam.AddtionalDefense);

            // Evasion / magic defense use "Nx Y%" on the victory screen - track both count and rate
            CheckStatChange(changes, T("Evasion Count"), beforeParam.AddtionalEvasionCount, afterParam.AddtionalEvasionCount);
            CheckStatChange(changes, T("Evasion"), beforeParam.AddtionalEvasionRate, afterParam.AddtionalEvasionRate);
            CheckStatChange(changes, T("Magic Defense Count"), beforeParam.AddtionalMagicDefenseCount, afterParam.AddtionalMagicDefenseCount);
            CheckStatChange(changes, T("Magic Defense"), beforeParam.AddtionalAbilityDefenseRate, afterParam.AddtionalAbilityDefenseRate);
            CheckStatChange(changes, T("Magic Evasion"), beforeParam.AddtionalAbilityEvasionRate, afterParam.AddtionalAbilityEvasionRate);

            return changes.Count > 0 ? $"{charName}: {string.Join(", ", changes)}" : null;
        }

        /// <summary>Adds "Strength +1" (or "HP -3" — FF2 stats can drop) when a stat changed.</summary>
        private static void CheckStatChange(List<string> changes, string statName, int before, int after)
        {
            int delta = after - before;
            if (delta > 0)
                changes.Add($"{statName} +{delta}");
            else if (delta < 0)
                changes.Add($"{statName} {delta}");
        }

        #endregion

        #region ShowGetItemsInit - item drops

        public static void ShowGetItemsInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                var data = __instance?.targetData;
                if (data == null) return;
                ResetTracking(data);
                if (announcedItems) return;
                announcedItems = true;

                var itemList = data.ItemList;
                if (itemList == null || itemList.Count == 0) return;

                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                // Convert drop items to localized content data
                var contentDataList = ListItemFormatter.GetContentDataList(itemList, messageManager);
                if (contentDataList == null) return;

                foreach (var itemContent in contentDataList)
                {
                    if (itemContent == null) continue;

                    string itemName = TextUtils.StripIconMarkup(itemContent.Name);
                    if (string.IsNullOrEmpty(itemName)) continue;

                    int count = itemContent.Count;
                    string announcement = count > 1
                        ? string.Format(T("Found {0} x{1}"), itemName, count)
                        : string.Format(T("Found {0}"), itemName);
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error announcing items: {ex.Message}");
            }
        }

        #endregion

        #region SetLevelUpList - spell level-ups

        /// <summary>
        /// Spell level-up phase (ResultMenuController.ShowLevelUpAbilitysInit → ResultSkillController
        /// .SetLevelUpList(characters)). Announces, per character, each spell whose level rose, computed
        /// from the before/after ability exp with ExpUtility.GetExpLevel (the game's own table).
        /// </summary>
        public static void SetLevelUpList_Postfix(Il2CppSystem.Collections.Generic.List<BattleResultCharacterData> __0)
        {
            try
            {
                var list = __0;
                if (list == null) return;

                // Diagnostic (unverified in game): confirm this fires once per result screen.
                spellLevelUpCalls++;
                MelonLogger.Msg($"[BattleResult] SetLevelUpList call {spellLevelUpCalls} for this result ({list.Count} characters)");

                if (announcedSpellLevels) return;
                announcedSpellLevels = true;

                foreach (var charResult in list)
                {
                    if (charResult == null) continue;
                    string line = BuildSpellLevelLine(charResult);
                    if (!string.IsNullOrEmpty(line))
                        FFII_ScreenReaderMod.SpeakText(line, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleResult] Error announcing spell level-ups: {ex.Message}");
            }
        }

        /// <summary>"Maria: Fire lv3, Cure lv2", or null when no spell leveled up.</summary>
        private static string BuildSpellLevelLine(BattleResultCharacterData charResult)
        {
            var afterData = charResult.AfterData;
            var afterAbilities = afterData?.OwnedAbilityList;
            if (afterAbilities == null) return null;

            string charName = afterData.Name;
            if (string.IsNullOrEmpty(charName)) return null;

            var beforeAbilities = charResult.BeforData?.OwnedAbilityList;
            var messageManager = MessageManager.Instance;

            var parts = new List<string>();
            foreach (var after in afterAbilities)
            {
                try
                {
                    int abilityId = after?.Ability?.Id ?? -1;
                    if (abilityId < 0) continue;

                    int beforeExp = 0;
                    if (beforeAbilities != null)
                    {
                        foreach (var before in beforeAbilities)
                        {
                            if (before?.Ability != null && before.Ability.Id == abilityId)
                            {
                                beforeExp = before.SkillLevel;
                                break;
                            }
                        }
                    }

                    int beforeLevel = SpellLevel(beforeExp);
                    int afterLevel = SpellLevel(after.SkillLevel);
                    if (afterLevel <= beforeLevel) continue;

                    string name = SpellName(after, messageManager);
                    if (!string.IsNullOrEmpty(name))
                        parts.Add(string.Format(T("{0} lv{1}"), name, afterLevel));
                }
                catch { }
            }

            return parts.Count > 0 ? $"{charName}: {string.Join(", ", parts)}" : null;
        }

        /// <summary>Spell level (1-16) from raw ability exp via the game's LevelExp table.</summary>
        private static int SpellLevel(int rawExp)
        {
            return Math.Clamp(ExpUtility.GetExpLevel(1, rawExp, ExpTableType.LevelExp), 1, 16);
        }

        private static string SpellName(OwnedAbility ability, MessageManager messageManager)
        {
            string mesIdName = ability.MesIdName;
            if (messageManager == null || string.IsNullOrEmpty(mesIdName)) return null;
            return TextUtils.StripIconMarkup(messageManager.GetMessage(mesIdName, false));
        }

        #endregion
    }
}
