using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Systems;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.AnnouncementDeduplicator;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleUtility = Il2CppLast.Battle.BattleUtility;
using BattleController = Il2CppLast.Battle.BattleController;
using BattlePlugManager = Il2CppLast.Battle.BattlePlugManager;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Helper for battle message announcements using centralized deduplication.
    /// </summary>
    public static class GlobalBattleMessageTracker
    {
        /// <summary>
        /// Try to announce a message, returning false if it was recently announced.
        /// Uses centralized AnnouncementDeduplicator for deduplication.
        /// </summary>
        public static bool TryAnnounce(string message, string source)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string cleanMessage = message.Trim();

            // Use centralized deduplication
            if (!ShouldAnnounce(CONTEXT_BATTLE_MESSAGE, cleanMessage))
            {
                return false;
            }

            MelonLogger.Msg($"[{source}] {cleanMessage}");
            // Battle actions don't interrupt - they queue
            FFII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            return true;
        }

        /// <summary>
        /// Reset tracking (e.g., when battle ends).
        /// </summary>
        public static void Reset()
        {
            AnnouncementDeduplicator.Reset(CONTEXT_BATTLE_ACTION, CONTEXT_BATTLE_MESSAGE, CONTEXT_BATTLE_CONDITION);
        }
    }

    /// <summary>
    /// Patches for battle action and damage announcements.
    /// Ported from FF3 screen reader.
    /// </summary>
    public static class BattleMessagePatches
    {
        /// <summary>
        /// Apply all battle message patches manually.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[BattleMessage] Applying battle message patches...");

                // Patch CreateActFunction for action announcements
                var createActFunctionMethod = AccessTools.Method(typeof(ParameterActFunctionManagment), "CreateActFunction");
                if (createActFunctionMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(CreateActFunction_Postfix));
                    harmony.Patch(createActFunctionMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[BattleMessage] Patched CreateActFunction");
                }

                // Patch static BattleUtility.CreateDamageView for damage/healing display
                // Note: BattleBasicFunction.CreateDamageView was removed - it fired redundantly with incorrect isRecovery flag
                var utilityDamageViewMethod = AccessTools.Method(
                    typeof(BattleUtility),
                    "CreateDamageView",
                    new Type[] { typeof(BattleUnitData), typeof(int), typeof(bool), typeof(bool), typeof(bool) }
                );
                if (utilityDamageViewMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(CreateDamageViewUtility_Postfix));
                    harmony.Patch(utilityDamageViewMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[BattleMessage] Patched CreateDamageView");
                }

                // Patch BattleConditionController.Add for status effect announcements
                var addConditionMethod = AccessTools.Method(typeof(BattleConditionController), "Add");
                if (addConditionMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(ConditionAdd_Postfix));
                    harmony.Patch(addConditionMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[BattleMessage] Patched BattleConditionController.Add");
                }

                // Patch BattleController.StartPreeMptiveMes for encounter type announcements
                var startPreeMptiveMesMethod = AccessTools.Method(typeof(BattleController), "StartPreeMptiveMes");
                if (startPreeMptiveMesMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(StartPreeMptiveMes_Postfix));
                    harmony.Patch(startPreeMptiveMesMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[BattleMessage] Patched StartPreeMptiveMes");
                }
                else
                {
                    MelonLogger.Warning("[BattleMessage] Could not find StartPreeMptiveMes method");
                }

                // Patch BattleController.StartEscape for escape announcements
                var startEscapeMethod = AccessTools.Method(typeof(BattleController), "StartEscape");
                if (startEscapeMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(StartEscape_Postfix));
                    harmony.Patch(startEscapeMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[BattleMessage] Patched StartEscape");
                }
                else
                {
                    MelonLogger.Warning("[BattleMessage] Could not find StartEscape method");
                }

                MelonLogger.Msg("[BattleMessage] Battle message patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleMessage] Error applying patches: {ex.Message}");
            }
        }

        #region CreateActFunction - Action Announcements

        public static void CreateActFunction_Postfix(BattleActData battleActData)
        {
            try
            {
                if (battleActData == null) return;

                string actorName = GetActorName(battleActData);
                string actionName = GetActionName(battleActData);

                if (string.IsNullOrEmpty(actorName)) return;

                string announcement;
                if (!string.IsNullOrEmpty(actionName))
                {
                    string actionLower = actionName.ToLower();
                    if (actionLower == "attack" || actionLower == "fight")
                    {
                        announcement = $"{actorName} attacks";
                    }
                    else if (actionLower == "defend" || actionLower == "guard")
                    {
                        announcement = $"{actorName} defends";
                    }
                    else if (actionLower == "item")
                    {
                        announcement = $"{actorName} uses item";
                    }
                    else
                    {
                        announcement = $"{actorName}, {actionName}";
                    }
                }
                else
                {
                    announcement = $"{actorName} attacks";
                }

                // Use object-based deduplication so different enemies with same name
                // attacking in sequence are both announced (each has unique BattleActData)
                if (AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_BATTLE_ACTION, battleActData))
                {
                    MelonLogger.Msg($"[BattleAction] {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateActFunction patch: {ex.Message}");
            }
        }

        private static string GetActorName(BattleActData battleActData)
        {
            try
            {
                var attackUnit = battleActData.AttackUnitData;
                if (attackUnit == null) return null;

                // Check if attacker is a player character
                var playerData = attackUnit.TryCast<BattlePlayerData>();
                if (playerData != null && playerData.ownedCharacterData != null)
                {
                    return playerData.ownedCharacterData.Name;
                }

                // Check if attacker is an enemy
                var enemyData = attackUnit.TryCast<BattleEnemyData>();
                if (enemyData != null)
                {
                    string mesIdName = enemyData.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            return localizedName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting actor name: {ex.Message}");
            }
            return null;
        }

        private static string GetActionName(BattleActData battleActData)
        {
            try
            {
                // Try to get item name first (for Item command)
                var itemList = battleActData.itemList;
                if (itemList != null && itemList.Count > 0)
                {
                    var ownedItem = itemList[0];
                    if (ownedItem != null)
                    {
                        string itemName = GetItemName(ownedItem);
                        if (!string.IsNullOrEmpty(itemName))
                        {
                            return itemName;
                        }
                    }
                }

                // Try to get the ability name (spells, skills)
                var abilityList = battleActData.abilityList;
                if (abilityList != null && abilityList.Count > 0)
                {
                    var ability = abilityList[0];
                    if (ability != null)
                    {
                        string abilityName = ContentUtitlity.GetAbilityName(ability);
                        if (!string.IsNullOrEmpty(abilityName))
                        {
                            // Strip icon markup (e.g., <IC_WMGC> for white magic, <IC_BMGC> for black magic)
                            return TextUtils.StripIconMarkup(abilityName);
                        }
                    }
                }

                // Fall back to command name (Attack, Defend, etc.)
                var command = battleActData.Command;
                if (command != null)
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string commandMesId = command.MesIdName;
                        if (!string.IsNullOrEmpty(commandMesId))
                        {
                            string localizedName = messageManager.GetMessage(commandMesId);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                // Strip icon markup from command names too
                                return TextUtils.StripIconMarkup(localizedName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting action name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name of an item from OwnedItemData.
        /// </summary>
        private static string GetItemName(OwnedItemData ownedItem)
        {
            try
            {
                // OwnedItemData has a Name property that returns the localized name
                string itemName = ownedItem.Name;
                if (!string.IsNullOrEmpty(itemName))
                {
                    // Strip icon markup (e.g., "[ICB]") from item name
                    itemName = TextUtils.StripIconMarkup(itemName);
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        return itemName;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting item name: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region CreateDamageView - Damage/Healing Announcements

        /// <summary>
        /// Postfix for static BattleUtility.CreateDamageView - handles both damage and healing display.
        /// Signature: CreateDamageView(BattleUnitData targetUnitData, int damage, bool isRecovery, bool isMiss, bool isPlaySe)
        /// </summary>
        public static void CreateDamageViewUtility_Postfix(BattleUnitData targetUnitData, int damage, bool isRecovery, bool isMiss)
        {
            try
            {
                if (targetUnitData == null) return;

                string targetName = "Unknown";

                var playerData = targetUnitData.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }
                else
                {
                    var enemyData = targetUnitData.TryCast<BattleEnemyData>();
                    if (enemyData != null)
                    {
                        string mesIdName = enemyData.GetMesIdName();
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                        {
                            string localizedName = messageManager.GetMessage(mesIdName);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                targetName = localizedName;
                            }
                        }
                    }
                }

                string message;
                if (isMiss || damage == 0)
                {
                    message = $"{targetName}: Miss";
                }
                else if (isRecovery)
                {
                    message = $"{targetName}: Recovered {damage} HP";
                }
                else
                {
                    message = $"{targetName}: {damage} damage";
                }

                MelonLogger.Msg($"[Damage] {message}");
                // Damage/healing doesn't interrupt - queues after action announcement
                FFII_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateDamageViewUtility patch: {ex.Message}");
            }
        }

        #endregion

        #region ConditionAdd - Status Effect Announcements

        public static void ConditionAdd_Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                // Get target name
                string targetName = "Unknown";
                var playerData = battleUnitData.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }
                else
                {
                    var enemyData = battleUnitData.TryCast<BattleEnemyData>();
                    if (enemyData != null)
                    {
                        string mesIdName = enemyData.GetMesIdName();
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                        {
                            string localizedName = messageManager.GetMessage(mesIdName);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                targetName = localizedName;
                            }
                        }
                    }
                }

                // Get condition name from ID
                string conditionName = null;
                try
                {
                    var unitDataInfo = battleUnitData.BattleUnitDataInfo;
                    if (unitDataInfo?.Parameter != null)
                    {
                        var confirmedList = unitDataInfo.Parameter.ConfirmedConditionList();
                        if (confirmedList != null && confirmedList.Count > 0)
                        {
                            foreach (var condition in confirmedList)
                            {
                                if (condition != null && condition.Id == id)
                                {
                                    string conditionMesId = condition.MesIdName;

                                    // Skip conditions with no message ID (internal/hidden statuses)
                                    if (string.IsNullOrEmpty(conditionMesId) || conditionMesId == "None")
                                    {
                                        return;
                                    }

                                    var messageManager = MessageManager.Instance;
                                    if (messageManager != null)
                                    {
                                        string localizedConditionName = messageManager.GetMessage(conditionMesId);
                                        if (!string.IsNullOrEmpty(localizedConditionName))
                                        {
                                            conditionName = localizedConditionName;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    if (conditionName == null)
                    {
                        // Don't announce unknown statuses
                        return;
                    }
                }
                catch
                {
                    return;
                }

                string announcement = $"{targetName}: {conditionName}";

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_BATTLE_CONDITION, announcement)) return;

                MelonLogger.Msg($"[Status] {announcement}");
                // Status doesn't interrupt
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.Add patch: {ex.Message}");
            }
        }

        #endregion

        #region StartPreeMptiveMes - Encounter Type Announcements

        private static int lastPreemptiveState = 0;

        /// <summary>
        /// Postfix for BattleController.StartPreeMptiveMes - announces encounter type.
        /// PreeMptiveState enum: Non=-1, Normal=0, PreeMptive=1, BackAttack=2,
        /// EnemyPreeMptive=3, EnemySideAttack=4, SideAttack=5
        /// </summary>
        public static void StartPreeMptiveMes_Postfix(BattleController __instance)
        {
            try
            {
                // Try to get the preemptive state via BattlePlugManager
                int preemptiveState = 0;

                try
                {
                    var battlePlugManager = BattlePlugManager.Instance();
                    if (battlePlugManager != null)
                    {
                        // Get BattlePopPlug and call GetResult()
                        var battlePopPlug = battlePlugManager.BattlePopPlug;
                        if (battlePopPlug != null)
                        {
                            preemptiveState = (int)battlePopPlug.GetResult();
                        }
                        else
                        {
                            // Alternatively, get BattleProgress and call GetNowPreetive if it's a BattleProgressTurn
                            var battleProgress = battlePlugManager.BattleProgress;
                            if (battleProgress != null)
                            {
                                var getNowPreetiveMethod = battleProgress.GetType().GetMethod("GetNowPreetive");
                                if (getNowPreetiveMethod != null)
                                {
                                    var result = getNowPreetiveMethod.Invoke(battleProgress, null);
                                    preemptiveState = Convert.ToInt32(result);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[BattleMessage] Could not get preemptive state: {ex.Message}");
                }

                // Avoid repeat announcements
                if (preemptiveState == lastPreemptiveState && preemptiveState == 0)
                    return;
                lastPreemptiveState = preemptiveState;

                string announcement = preemptiveState switch
                {
                    1 => "Preemptive strike!",      // PreeMptive
                    2 => "Back attack!",            // BackAttack
                    3 => "Enemy preemptive!",      // EnemyPreeMptive
                    4 => "Enemy side attack!",     // EnemySideAttack
                    5 => "Side attack!",           // SideAttack
                    _ => null                       // Normal (0) or Non (-1) - no announcement
                };

                if (!string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg($"[BattleMessage] Encounter: {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in StartPreeMptiveMes patch: {ex.Message}");
            }
        }

        #endregion

        #region StartEscape - Escape Announcements

        /// <summary>
        /// Postfix for BattleController.StartEscape - announces party escape.
        /// </summary>
        public static void StartEscape_Postfix()
        {
            try
            {
                string announcement = "Party escaped!";
                MelonLogger.Msg($"[BattleMessage] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in StartEscape patch: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Resets message tracking state.
        /// </summary>
        public static void ResetState()
        {
            GlobalBattleMessageTracker.Reset();
            lastPreemptiveState = 0;
        }
    }
}
