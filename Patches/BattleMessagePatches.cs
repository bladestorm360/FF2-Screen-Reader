using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Systems;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleController = Il2CppLast.Battle.BattleController;
using BattlePlugManager = Il2CppLast.Battle.BattlePlugManager;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;
using HitType = Il2CppLast.Systems.HitType;
using BattleBasicFunction = Il2CppLast.Battle.Function.BattleBasicFunction;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Helper for battle message announcements using centralized deduplication.
    /// </summary>
    public static class GlobalBattleMessageTracker
    {
        // Local guard: the same battle message can be posted via more than one code path.
        private static string _lastMessage = null;

        /// <summary>
        /// Try to announce a message, returning false if it duplicates the last one.
        /// </summary>
        public static bool TryAnnounce(string message, string source)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string cleanMessage = message.Trim();

            if (cleanMessage == _lastMessage)
            {
                return false;
            }
            _lastMessage = cleanMessage;

            // Battle actions don't interrupt - they queue
            FFII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            return true;
        }

        /// <summary>
        /// Reset tracking (e.g., when battle ends).
        /// </summary>
        public static void Reset()
        {
            _lastMessage = null;
            BattleMessagePatches.ResetConditionDedup();
        }
    }

    /// <summary>
    /// Patches for battle action and damage announcements.
    /// Ported from FF3 screen reader.
    /// </summary>
    public static class BattleMessagePatches
    {
        #region Damage Source Tracking

        /// <summary>
        /// Tracks the source of damage (e.g., "Poison") for status effect damage.
        /// Set before CreateDamageView is called, consumed when damage is announced.
        /// </summary>
        private static string pendingDamageSource = null;

        /// <summary>
        /// Sets the pending damage source (called before damage generation).
        /// </summary>
        public static void SetDamageSource(string source) => pendingDamageSource = source;

        /// <summary>
        /// Consumes and returns the pending damage source.
        /// </summary>
        public static string ConsumeDamageSource()
        {
            var source = pendingDamageSource;
            pendingDamageSource = null;
            return source;
        }

        /// <summary>
        /// Tracks the last battle command message to prevent duplicates.
        /// </summary>
        private static string lastBattleCommandMessage = "";

        #endregion

        /// <summary>
        /// Apply all battle message patches manually.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch CreateActFunction for action announcements
                var createActFunctionMethod = AccessTools.Method(typeof(ParameterActFunctionManagment), "CreateActFunction");
                if (createActFunctionMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(CreateActFunction_Postfix));
                    harmony.Patch(createActFunctionMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch BattleBasicFunction.CreateDamageView — the single damage/healing handler (FF1
                // parity). Its HitType parameter distinguishes HP recovery (4) from MP recovery (6), and
                // it's the view that actually fires during combat (incl. multi-hit). The old static
                // BattleUtility.CreateDamageView postfix was redundant dead code and has been removed.
                var basicFunctionDamageViewMethod = AccessTools.Method(
                    typeof(BattleBasicFunction),
                    "CreateDamageView",
                    new Type[] { typeof(BattleUnitData), typeof(int), typeof(HitType), typeof(bool), typeof(bool) }
                );
                if (basicFunctionDamageViewMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(CreateDamageViewWithHitType_Postfix));
                    harmony.Patch(basicFunctionDamageViewMethod, postfix: new HarmonyMethod(postfix));
                }

                // Capture the on-screen "xN" multi-hit multiplier so the damage announce can
                // prepend it (Multi-hit Damage setting), e.g. "14x1552 damage".
                PatchHitCount(harmony);

                // Patch BattleConditionController.Add for status effect announcements
                var addConditionMethod = AccessTools.Method(typeof(BattleConditionController), "Add");
                if (addConditionMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(ConditionAdd_Postfix));
                    harmony.Patch(addConditionMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch BattleController.StartPreeMptiveMes for encounter type announcements
                var startPreeMptiveMesMethod = AccessTools.Method(typeof(BattleController), "StartPreeMptiveMes");
                if (startPreeMptiveMesMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleMessagePatches), nameof(StartPreeMptiveMes_Postfix));
                    harmony.Patch(startPreeMptiveMesMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[BattleMessage] Could not find StartPreeMptiveMes method");
                }

                // Patch PoisonConditionFunction.GeneratePoisonDamage for poison damage source tracking
                var poisonType = AccessTools.TypeByName("Il2CppLast.Battle.PoisonConditionFunction");
                if (poisonType != null)
                {
                    var generatePoisonMethod = AccessTools.Method(poisonType, "GeneratePoisonDamage");
                    if (generatePoisonMethod != null)
                    {
                        harmony.Patch(generatePoisonMethod,
                            prefix: new HarmonyMethod(typeof(BattleMessagePatches), nameof(GeneratePoisonDamage_Prefix)),
                            postfix: new HarmonyMethod(typeof(BattleMessagePatches), nameof(GeneratePoisonDamage_Postfix)));
                    }
                    else
                    {
                        MelonLogger.Error("[BattleMessage] Could not find GeneratePoisonDamage method");
                    }
                }
                else
                {
                    MelonLogger.Error("[BattleMessage] Could not find PoisonConditionFunction type");
                }

                // Patch BattleCommandMessageController for system messages like "The party was defeated"
                PatchBattleCommandMessage(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleMessage] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Postfix on Last.UI.DamageViewUIManager.CreateHitCount(int hitCountValue, ...) to capture the
        /// on-screen "xN" multi-hit multiplier so the damage announce can prepend it (e.g. "14x1552 damage").
        /// The multiplier fires just before the matching CreateDamageView; consumed by the damage postfix.
        /// </summary>
        private static void PatchHitCount(HarmonyLib.Harmony harmony)
        {
            try
            {
                var type = FindType("Il2CppLast.UI.DamageViewUIManager");
                if (type == null)
                {
                    MelonLogger.Warning("[BattleMessage] DamageViewUIManager type not found");
                    return;
                }

                MethodInfo method = null;
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.Name == "CreateHitCount")
                    {
                        var ps = m.GetParameters();
                        if (ps.Length >= 1 && ps[0].ParameterType == typeof(int)) { method = m; break; }
                    }
                }
                if (method == null)
                {
                    MelonLogger.Warning("[BattleMessage] CreateHitCount(int,...) not found");
                    return;
                }

                var postfix = typeof(BattleMessagePatches).GetMethod(
                    nameof(CreateHitCount_Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleMessage] Error patching CreateHitCount: {ex.Message}");
            }
        }

        // Multi-hit multiplier captured from DamageViewUIManager.CreateHitCount, which fires just before
        // the matching CreateDamageView. Consumed (and reset to 1) by CreateDamageViewWithHitType_Postfix.
        private static int _pendingHitCount = 1;
        // Frame the multiplier was captured on. Used to reject a stale count that was never
        // consumed by a CreateDamageView (e.g. a fully-evaded multi-hit) so it can't leak into
        // an unrelated later attack's damage announcement.
        private static int _pendingHitCountFrame = -1;

        /// <summary>Captures the hit-count multiplier (__0 = hitCountValue) for the next damage view.</summary>
        public static void CreateHitCount_Postfix(int __0)
        {
            _pendingHitCount = __0;
            _pendingHitCountFrame = UnityEngine.Time.frameCount;
        }

        /// <summary>
        /// Patch BattleCommandMessageController.SetMessage for system messages like "The party was defeated".
        /// </summary>
        private static void PatchBattleCommandMessage(HarmonyLib.Harmony harmony)
        {
            try
            {
                // KeyInput version (primary)
                var keyInputType = FindType("Il2CppLast.UI.KeyInput.BattleCommandMessageController");
                if (keyInputType != null)
                {
                    var setMessageMethod = AccessTools.Method(keyInputType, "SetMessage");
                    if (setMessageMethod != null)
                    {
                        var postfix = typeof(BattleMessagePatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setMessageMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Error("[BattleMessage] KeyInput.BattleCommandMessageController.SetMessage method not found");
                    }
                }
                else
                {
                    MelonLogger.Error("[BattleMessage] KeyInput.BattleCommandMessageController type not found");
                }

                // Touch version (SetSystemMessage)
                var touchType = FindType("Il2CppLast.UI.Touch.BattleCommandMessageController");
                if (touchType != null)
                {
                    var setSystemMsgMethod = AccessTools.Method(touchType, "SetSystemMessage");
                    if (setSystemMsgMethod != null)
                    {
                        var postfix = typeof(BattleMessagePatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setSystemMsgMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Error("[BattleMessage] Touch.BattleCommandMessageController.SetSystemMessage method not found");
                    }
                }
                else
                {
                    MelonLogger.Error("[BattleMessage] Touch.BattleCommandMessageController type not found");
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for BattleCommandMessageController.SetMessage/SetSystemMessage.
        /// Announces battle messages including "The party was defeated".
        /// </summary>
        public static void SetMessage_Postfix(object __0)
        {
            try
            {
                // __0 is the message string (using __0 to avoid IL2CPP string param crash)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message)) return;

                // Deduplicate
                if (message == lastBattleCommandMessage) return;
                lastBattleCommandMessage = message;

                // Clean up the message
                string cleanMessage = TextUtils.StripIconMarkup(message);
                cleanMessage = cleanMessage.Replace("\n", " ").Replace("\r", " ").Trim();
                while (cleanMessage.Contains("  "))
                    cleanMessage = cleanMessage.Replace("  ", " ");

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Bug 6: if a spell/skill act just stashed its caster (CreateActFunction suppressed
                // its own base-name utterance), prepend the caster so this level-bearing message
                // reads "Caster: name level" (e.g. "Balloon: self destruct I"). The first message
                // after an act consumes the pending caster regardless of the window.
                if (!string.IsNullOrEmpty(_pendingActorName))
                {
                    if (UnityEngine.Time.frameCount - _pendingActorFrame <= 30)
                        cleanMessage = $"{_pendingActorName}: {cleanMessage}";
                    _pendingActorName = null;
                }

                // Use interrupt for defeat message
                bool isDefeatMessage = cleanMessage.Contains("defeated", StringComparison.OrdinalIgnoreCase);

                FFII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: isDefeatMessage);
            }
            catch { }
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

                // Bug 6: a spell/skill cast (ability present, not an item) is announced by the
                // game's own message with its level (SetMessage_Postfix → "self destruct I"), so
                // suppress this base-name utterance and stash the caster for SetMessage to prepend.
                bool isAbilityCast = false;
                try
                {
                    var abilityList = battleActData.abilityList;
                    var itemList = battleActData.itemList;
                    isAbilityCast = abilityList != null && abilityList.Count > 0
                                    && (itemList == null || itemList.Count == 0);
                }
                catch { }

                if (isAbilityCast)
                {
                    _pendingActorName = actorName;
                    _pendingActorFrame = UnityEngine.Time.frameCount;
                    return;
                }

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

                // Local guard by the native BattleActData pointer: skip a repeat fire for the
                // same action, but different enemies with the same name (distinct act data)
                // each announce.
                IntPtr actPtr = IntPtr.Zero;
                try { actPtr = battleActData.Pointer; } catch { }
                if (actPtr != _lastActDataPtr)
                {
                    _lastActDataPtr = actPtr;
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
            return null;
        }

        #endregion

        #region CreateDamageView - Damage/Healing Announcements

        /// <summary>
        /// Helper method to get the target name from BattleUnitData.
        /// </summary>
        private static string GetTargetName(BattleUnitData targetUnitData)
        {
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
                            targetName = localizedName;
                    }
                }
            }
            return targetName;
        }

        /// <summary>
        /// Postfix for BattleBasicFunction.CreateDamageView — the single damage/healing handler (FF1
        /// parity). The HitType distinguishes HP damage/recovery from MP recovery, and this is the view
        /// that actually fires during combat (incl. multi-hit). Announces directly — with only one
        /// damage postfix there is no cross-patch duplication to dedup against.
        /// </summary>
        public static void CreateDamageViewWithHitType_Postfix(BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                if (data == null) return;

                // TEMP DIAG (0-damage / buff HitType confirmation): log the raw game damage-view event
                // — value + HitType + isRecovery — so buff-vs-zero-damage HitTypes can be verified.
                // Remove after confirming.
                MelonLogger.Msg($"[DIAG-DMG] CreateDamageView value={value} hitType={hitType}({(int)hitType}) isRecovery={isRecovery}");

                string targetName = GetTargetName(data);
                var damageSource = ConsumeDamageSource();

                // Consume the multi-hit "×N" count captured by CreateHitCount (fires just before this
                // view). Reset to 1 so a stale count can't leak into the next attack.
                bool fresh = UnityEngine.Time.frameCount - _pendingHitCountFrame <= 1;
                int hitCount = fresh ? _pendingHitCount : 1;
                _pendingHitCount = 1;

                string message;

                // Mirror the game's own HitType categorization (Il2CppLast.Systems.HitType):
                // Non=-1, Hit=0, Critical=1, Miss=2, Zero=3, Recovery=4, MPHit=5, MPRecovery=6,
                // RecoveryCondition=7.
                if (hitType == HitType.RecoveryCondition || hitType == HitType.Non)
                {
                    // Condition/buff application (Protect, Haste, Slow, ...) — the condition itself is
                    // announced by ConditionAdd_Postfix, so don't speak a damage number for it.
                    return;
                }
                else if (hitType == HitType.Miss)
                {
                    message = $"{targetName}: Miss";
                }
                else if (hitType == HitType.MPRecovery)
                {
                    message = $"{targetName}: Recovered {value} MP";
                }
                else if (hitType == HitType.Recovery || isRecovery)
                {
                    message = $"{targetName}: Recovered {value} HP";
                }
                else if (hitType == HitType.MPHit)
                {
                    message = $"{targetName}: {value} MP damage";
                }
                else if (!string.IsNullOrEmpty(damageSource))
                {
                    // HP damage from a status source (e.g. Poison).
                    message = (PreferencesManager.DamageDisplay == 1 && hitCount > 1)
                        ? $"{damageSource}: {targetName}: {hitCount}x{value} damage"
                        : $"{damageSource}: {targetName}: {value} damage";
                }
                else
                {
                    // HP damage — Hit / Critical / Zero. A zero-damage hit (HitType.Zero) has value 0
                    // and reads "0 damage", mirroring the game's on-screen "0".
                    message = (PreferencesManager.DamageDisplay == 1 && hitCount > 1)
                        ? $"{targetName}: {hitCount}x{value} damage"
                        : $"{targetName}: {value} damage";
                }

                // Damage/healing doesn't interrupt - queues after the action announcement.
                FFII_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch { }
        }

        #endregion

        #region ConditionAdd - Status Effect Announcements

        // Per-unit dedup: same-named enemies (e.g., two Goblins both poisoned) must each
        // announce, so we key by the unit's native pointer instead of the announcement text.
        private static readonly Dictionary<IntPtr, string> _lastConditionByUnit = new Dictionary<IntPtr, string>();
        // Local guard for CreateActFunction (keyed by the native BattleActData pointer).
        private static IntPtr _lastActDataPtr = IntPtr.Zero;

        // Bug 6: for a spell/skill cast, CreateActFunction suppresses its own base-name utterance
        // (e.g. "Balloon, self destruct") and stashes the caster here; the game's own battle
        // message (SetMessage_Postfix), which includes the spell level ("self destruct I"), then
        // prepends it → "Balloon: self destruct I". Frame-stamped so a stale actor can't leak
        // onto an unrelated later message.
        private static string _pendingActorName = null;
        private static int _pendingActorFrame = -1;

        public static void ResetConditionDedup()
        {
            _lastConditionByUnit.Clear();
            _lastActDataPtr = IntPtr.Zero;
        }

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

                // Per-unit dedup so same-named enemies each announce their own status.
                IntPtr unitPtr = IntPtr.Zero;
                try { unitPtr = battleUnitData.Pointer; } catch { }
                if (unitPtr != IntPtr.Zero)
                {
                    if (_lastConditionByUnit.TryGetValue(unitPtr, out var last) && last == announcement)
                        return;
                    _lastConditionByUnit[unitPtr] = announcement;
                }

                // Status doesn't interrupt
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch { }
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
                catch { }

                // Avoid repeat announcements
                if (preemptiveState == lastPreemptiveState && preemptiveState == 0)
                    return;
                lastPreemptiveState = preemptiveState;

                string announcement = preemptiveState switch
                {
                    FF2Constants.BattleStartStates.STATE_PREEMPTIVE => "Preemptive strike!",
                    FF2Constants.BattleStartStates.STATE_BACK_ATTACK => "Back attack!",
                    FF2Constants.BattleStartStates.STATE_ENEMY_PREEMPTIVE => "Enemy preemptive!",
                    FF2Constants.BattleStartStates.STATE_ENEMY_SIDE_ATTACK => "Enemy side attack!",
                    FF2Constants.BattleStartStates.STATE_SIDE_ATTACK => "Side attack!",
                    _ => null
                };

                if (!string.IsNullOrEmpty(announcement))
                {
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch { }
        }

        #endregion

        #region GeneratePoisonDamage - Poison Source Tracking

        /// <summary>
        /// Prefix for PoisonConditionFunction.GeneratePoisonDamage - sets damage source to "Poison".
        /// </summary>
        public static void GeneratePoisonDamage_Prefix()
        {
            SetDamageSource("Poison");
        }

        /// <summary>
        /// Postfix for PoisonConditionFunction.GeneratePoisonDamage - clears damage source if not consumed.
        /// </summary>
        public static void GeneratePoisonDamage_Postfix()
        {
            // Clear in case CreateDamageView wasn't called (e.g., target died)
            pendingDamageSource = null;
        }

        #endregion

        /// <summary>
        /// Resets message tracking state.
        /// </summary>
        public static void ResetState()
        {
            GlobalBattleMessageTracker.Reset();
            lastPreemptiveState = 0;
            lastBattleCommandMessage = "";
            _pendingActorName = null;
            _pendingActorFrame = -1;
        }
    }
}
