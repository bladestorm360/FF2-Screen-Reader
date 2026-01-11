using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Systems;
using FFII_ScreenReader.Core;
using BattlePlayerData = Il2Cpp.BattlePlayerData;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Global message deduplication to prevent the same message being spoken by multiple patches.
    /// </summary>
    public static class GlobalBattleMessageTracker
    {
        private static string lastMessage = "";
        private static float lastMessageTime = 0f;
        private const float MESSAGE_THROTTLE_SECONDS = 1.5f;

        /// <summary>
        /// Try to announce a message, returning false if it was recently announced.
        /// </summary>
        public static bool TryAnnounce(string message, string source)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string cleanMessage = message.Trim();
            float currentTime = UnityEngine.Time.time;

            // Skip if same message within throttle window
            if (cleanMessage == lastMessage && (currentTime - lastMessageTime) < MESSAGE_THROTTLE_SECONDS)
            {
                return false;
            }

            lastMessage = cleanMessage;
            lastMessageTime = currentTime;

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
            lastMessage = "";
            lastMessageTime = 0f;
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

                // Patch CreateDamageView for damage announcements
                var createDamageViewMethod = AccessTools.Method(
                    typeof(BattleBasicFunction),
                    "CreateDamageView",
                    new Type[] { typeof(BattleUnitData), typeof(int), typeof(HitType), typeof(bool), typeof(bool) }
                );
                if (createDamageViewMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(CreateDamageView_Postfix));
                    harmony.Patch(createDamageViewMethod, postfix: new HarmonyMethod(postfix));
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
                GlobalBattleMessageTracker.TryAnnounce(announcement, "BattleAction");
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
                // Try to get the ability name first (spells, skills)
                var abilityList = battleActData.abilityList;
                if (abilityList != null && abilityList.Count > 0)
                {
                    var ability = abilityList[0];
                    if (ability != null)
                    {
                        string abilityName = ContentUtitlity.GetAbilityName(ability);
                        if (!string.IsNullOrEmpty(abilityName))
                        {
                            return abilityName;
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
                                return localizedName;
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

        #endregion

        #region CreateDamageView - Damage Announcements

        public static void CreateDamageView_Postfix(BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                string targetName = "Unknown";

                var playerData = data.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }

                var enemyData = data.TryCast<BattleEnemyData>();
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

                string message;
                if (hitType == HitType.Miss || value == 0)
                {
                    message = $"{targetName}: Miss";
                }
                else if (isRecovery)
                {
                    message = $"{targetName}: Recovered {value} HP";
                }
                else
                {
                    message = $"{targetName}: {value} damage";
                }

                MelonLogger.Msg($"[Damage] {message}");
                // Damage doesn't interrupt - queues after action announcement
                FFII_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateDamageView patch: {ex.Message}");
            }
        }

        #endregion

        #region ConditionAdd - Status Effect Announcements

        private static string lastConditionAnnouncement = "";

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

                // Skip duplicates
                if (announcement == lastConditionAnnouncement) return;
                lastConditionAnnouncement = announcement;

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

        /// <summary>
        /// Resets message tracking state.
        /// </summary>
        public static void ResetState()
        {
            GlobalBattleMessageTracker.Reset();
            lastConditionAnnouncement = "";
        }
    }
}
