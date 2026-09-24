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
using static FFII_ScreenReader.Utils.ModTextTranslator;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleController = Il2CppLast.Battle.BattleController;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;
using HitType = Il2CppLast.Systems.HitType;
using BattleBasicFunction = Il2CppLast.Battle.Function.BattleBasicFunction;

namespace FFII_ScreenReader.Patches
{
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

                // Status removal announcements ("X: Poison removed")
                PatchConditionRemoval(harmony);

                // Patch BattleController.StartPreeMptiveMes as the battle-start lifecycle hook
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

                // Battle-end lifecycle hooks (win / lose / escape fade-outs + Exit)
                PatchBattleEnd(harmony);
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

        // BattleBaseFunction.<battleActData>k__BackingField — a protected property, so read by offset.
        private const int OFFSET_BATTLE_ACT_DATA = 0x38;
        // Ability.TypeId of weapon attacks. BattleBasicFunction.CreateHitCount only draws the ×N for
        // this type, so the calculated-hit-count fallback follows the same rule.
        private const int WEAPON_ABILITY_TYPE = 4;

        /// <summary>
        /// The attack's own hit count against this target, from the function's calculation results
        /// (ICalcResultDic → ICalcResult.GetHitCount). Used when no on-screen ×N was paired with the
        /// damage view. Weapon attacks only; 1 for anything else or on any failure.
        /// </summary>
        private static int ReadWeaponHitCount(BattleBasicFunction function, BattleUnitData target)
        {
            try
            {
                if (function == null || target == null) return 1;
                IntPtr actPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(function.Pointer, OFFSET_BATTLE_ACT_DATA);
                if (actPtr == IntPtr.Zero) return 1;
                var abilities = new BattleActData(actPtr).abilityList;
                if (abilities == null || abilities.Count == 0 || abilities[0] == null
                    || abilities[0].TypeId != WEAPON_ABILITY_TYPE)
                    return 1;
                var results = function.ICalcResultDic;
                if (results == null || !results.ContainsKey(target)) return 1;
                var result = results[target];
                return result != null ? Math.Max(1, result.GetHitCount()) : 1;
            }
            catch
            {
                return 1;
            }
        }

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

                // Clean up the message
                string cleanMessage = TextUtils.StripIconMarkup(message);
                cleanMessage = cleanMessage.Replace("\n", " ").Replace("\r", " ").Trim();
                while (cleanMessage.Contains("  "))
                    cleanMessage = cleanMessage.Replace("  ", " ");

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Per-frame repeat guard: ScanBattleFunction.IsFunctionEnd re-sends the current line
                // (BattleUIManager.SetCommadnMessage → SetMessage) every frame until its timer advances.
                // Swallow an identical text inside a short frame window, refreshing the stamp on each
                // swallowed repeat, so the stream speaks once but the same text seconds later (a second
                // identical cast) still speaks. Checked on the game's text, before the caster prefix,
                // so a swallowed repeat never consumes _pendingActorName.
                int frame = UnityEngine.Time.frameCount;
                if (cleanMessage == _lastMessageText && frame - _lastMessageFrame < 20)
                {
                    _lastMessageFrame = frame;
                    return;
                }
                _lastMessageText = cleanMessage;
                _lastMessageFrame = frame;

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

                // "Actor: Action" for every action (FF1 parity), using the game's own localized
                // command/item name — no English command-word matching.
                string announcement = string.IsNullOrEmpty(actionName)
                    ? actorName
                    : $"{actorName}: {actionName}";

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
            string targetName = T("Unknown");
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
        public static void CreateDamageViewWithHitType_Postfix(BattleBasicFunction __instance, BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                if (data == null) return;

                string targetName = GetTargetName(data);
                var damageSource = ConsumeDamageSource();

                // Consume the multi-hit "×N" count captured by CreateHitCount (fires just before this
                // view). Reset to 1 so a stale count can't leak into the next attack. When no ×N was
                // paired with this view, fall back to the attack's own calculated hit count.
                bool fresh = UnityEngine.Time.frameCount - _pendingHitCountFrame <= 1;
                int hitCount = fresh ? _pendingHitCount : 1;
                _pendingHitCount = 1;
                if (hitCount <= 1)
                    hitCount = ReadWeaponHitCount(__instance, data);

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
                    message = string.Format(T("{0}: Miss"), targetName);
                }
                else if (hitType == HitType.MPRecovery)
                {
                    message = string.Format(T("{0}: Recovered {1} MP"), targetName, value);
                }
                else if (hitType == HitType.Recovery || isRecovery)
                {
                    message = string.Format(T("{0}: Recovered {1} HP"), targetName, value);
                }
                else if (hitType == HitType.MPHit)
                {
                    message = string.Format(T("{0}: {1} MP damage"), targetName, value);
                }
                else
                {
                    // HP damage — Hit / Critical / Zero. A zero-damage hit (HitType.Zero) has value 0
                    // and reads "0 damage", mirroring the game's on-screen "0".
                    message = (PreferencesManager.DamageDisplay == 1 && hitCount > 1)
                        ? string.Format(T("{0}: {1}x{2} damage"), targetName, hitCount, value)
                        : string.Format(T("{0}: {1} damage"), targetName, value);

                    // HP damage from a status source (e.g. Poison) is prefixed with the source.
                    if (!string.IsNullOrEmpty(damageSource))
                        message = $"{damageSource}: {message}";
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

        // SetMessage_Postfix per-frame repeat guard (last spoken message text + frame it was last seen).
        private static string _lastMessageText = null;
        private static int _lastMessageFrame = -1;

        public static void ResetConditionDedup()
        {
            _lastConditionByUnit.Clear();
            _lastActDataPtr = IntPtr.Zero;
            _trackedConditions.Clear();
            _lastRemovalUnit = IntPtr.Zero;
            _lastRemovalId = -1;
            _lastRemovalFrame = -1;
        }

        public static void ConditionAdd_Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                // Get target name
                string targetName = T("Unknown");
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
                int conditionType = -1;
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
                                    conditionType = condition.ConditionType;
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
                    // Named, announceable condition: its removal is announced too (UpdateTempParamter_Postfix).
                    TrackCondition(unitPtr, id, conditionName, conditionType, announcement);

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

        #region Condition removal - "X: Poison removed"

        // FF2 does not route cures through BattleConditionController.Remove (0x6BAB70): its only callers
        // are InterruptRemoveCondition (condition-driven), BattleEndRecoveryCondition and one ActSelect.
        // Cures (RecoveryConditionFunction & co. UpdateParameter), wear-off (BattleConditionFunction.
        // NaturalRemove) and revive edit Parameter.CurrentConditionList directly; the controller then
        // reconciles each unit's BattleConditionFunction list (CheckConditionFunction →
        // RemoveConditionFunction), and every function it drops is followed by
        // BattleStatusControl.UpdateTempParamter(unit) (0x90C4C0). That list is only ever changed by
        // BattleConditionController.Add (list.Add, then UpdateTempParamter) and that removal loop, so a
        // tracked condition missing from it inside UpdateTempParamter has just been removed, whatever
        // removed it (cure, wear-off, revive, Remove, conflict). Only conditions whose Add was announced
        // (named) are tracked, so a negated or hidden condition never reads "removed".

        // Last.Systems.ConditionType.UnableFight (KO).
        private const int CONDITION_TYPE_UNABLE_FIGHT = 5;

        private sealed class TrackedCondition
        {
            public string Name;
            public int Type;
            public string AddAnnouncement;
        }

        // unit pointer → condition id → the announced Add (name, type, text).
        private static readonly Dictionary<IntPtr, Dictionary<int, TrackedCondition>> _trackedConditions =
            new Dictionary<IntPtr, Dictionary<int, TrackedCondition>>();

        // Set by BattleConditionController.BattleEndRecoveryCondition (victory / escape clean-up);
        // cleared at the next battle start and by ResetState.
        private static bool _battleEnding = false;

        // Same (unit, condition) removal repeated in one frame is spoken once.
        private static IntPtr _lastRemovalUnit = IntPtr.Zero;
        private static int _lastRemovalId = -1;
        private static int _lastRemovalFrame = -1;

        /// <summary>
        /// BattleStatusControl.UpdateTempParamter(BattleUnitData) (unique RVA 0x90C4C0; callers:
        /// BattleConditionController.Add, RemoveConditionFunction, AttackSpeedConflict,
        /// AfterAttackSpeedConflict) and BattleConditionController.BattleEndRecoveryCondition (unique RVA
        /// 0x6B77D0; callers BattleController.StartWinResult / EndEscapeFadeOut).
        /// </summary>
        private static void PatchConditionRemoval(HarmonyLib.Harmony harmony)
        {
            try
            {
                var updateTemp = AccessTools.Method(typeof(BattleStatusControl), "UpdateTempParamter", new Type[] { typeof(BattleUnitData) });
                if (updateTemp != null)
                    harmony.Patch(updateTemp, postfix: new HarmonyMethod(typeof(BattleMessagePatches), nameof(UpdateTempParamter_Postfix)));
                else
                    MelonLogger.Error("[BattleMessage] BattleStatusControl.UpdateTempParamter(BattleUnitData) not found");

                var endRecovery = AccessTools.Method(typeof(BattleConditionController), "BattleEndRecoveryCondition", Type.EmptyTypes);
                if (endRecovery != null)
                    harmony.Patch(endRecovery, prefix: new HarmonyMethod(typeof(BattleMessagePatches), nameof(BattleEndRecoveryCondition_Prefix)));
                else
                    MelonLogger.Error("[BattleMessage] BattleConditionController.BattleEndRecoveryCondition not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleMessage] Error patching condition removal: {ex.Message}");
            }
        }

        private static void TrackCondition(IntPtr unitPtr, int id, string name, int type, string addAnnouncement)
        {
            if (!_trackedConditions.TryGetValue(unitPtr, out var byId))
            {
                byId = new Dictionary<int, TrackedCondition>();
                _trackedConditions[unitPtr] = byId;
            }
            byId[id] = new TrackedCondition { Name = name, Type = type, AddAnnouncement = addAnnouncement };
        }

        /// <summary>Battle-end clean-up begins: its removals are never spoken.</summary>
        public static void BattleEndRecoveryCondition_Prefix() => _battleEnding = true;

        /// <summary>
        /// A condition function was added to or removed from the unit (see the region comment). Speaks
        /// "{unit}: {condition} removed" for every tracked condition the unit no longer has, except during
        /// battle-end clean-up, outside battle, or for a unit that is down (KO / 0 HP: death clears the
        /// other statuses) unless the removed condition is KO itself (revive).
        /// </summary>
        public static void UpdateTempParamter_Postfix(BattleUnitData __0)
        {
            try
            {
                if (__0 == null) return;
                IntPtr unitPtr = __0.Pointer;
                if (unitPtr == IntPtr.Zero || !_trackedConditions.TryGetValue(unitPtr, out var tracked) || tracked.Count == 0)
                    return;

                var info = __0.BattleUnitDataInfo;
                var live = new HashSet<int>();
                var functions = info?.BattleConditionFunction;
                if (functions != null)
                {
                    for (int i = 0; i < functions.Count; i++)
                    {
                        var condition = functions[i]?.condition;
                        if (condition != null)
                            live.Add(condition.Id);
                    }
                }

                List<int> removed = null;
                foreach (var kv in tracked)
                {
                    if (!live.Contains(kv.Key))
                        (removed ??= new List<int>()).Add(kv.Key);
                }
                if (removed == null) return;

                bool silent = _battleEnding || !FFII_ScreenReaderMod.IsInBattle;
                bool unitDown = IsUnitDown(info);
                int frame = UnityEngine.Time.frameCount;
                string unitName = null;

                foreach (int id in removed)
                {
                    var entry = tracked[id];
                    tracked.Remove(id);

                    // A re-application must be announced again.
                    if (_lastConditionByUnit.TryGetValue(unitPtr, out var lastAdd) && lastAdd == entry.AddAnnouncement)
                        _lastConditionByUnit.Remove(unitPtr);

                    if (silent)
                        continue;
                    if (unitDown && entry.Type != CONDITION_TYPE_UNABLE_FIGHT)
                        continue;
                    if (unitPtr == _lastRemovalUnit && id == _lastRemovalId && frame == _lastRemovalFrame)
                        continue;
                    _lastRemovalUnit = unitPtr;
                    _lastRemovalId = id;
                    _lastRemovalFrame = frame;

                    unitName ??= GetTargetName(__0);
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("{0}: {1} removed"), unitName, entry.Name), interrupt: false);
                }
            }
            catch { }
        }

        /// <summary>KO'd or at 0 HP: the unit's statuses are being cleared by its death.</summary>
        private static bool IsUnitDown(BattleUnitDataInfo info)
        {
            try
            {
                var parameter = info?.Parameter;
                if (parameter == null)
                    return false;
                if (parameter.CurrentHP <= 0)
                    return true;
                var current = parameter.CurrentConditionList;
                if (current != null)
                {
                    for (int i = 0; i < current.Count; i++)
                    {
                        var c = current[i];
                        if (c != null && c.ConditionType == CONDITION_TYPE_UNABLE_FIGHT)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region Battle lifecycle

        /// <summary>
        /// Postfix for BattleController.StartPreeMptiveMes - the battle-start lifecycle hook. The
        /// encounter condition ("Preemptive strike!", "Back attack!") is the game's own message:
        /// StartPreeMptiveMes → BattleUtility.SetCommandMessageAtKey → BattleUIManager.SetCommadnMessage
        /// → BattleCommandMessageController.SetMessage (verified in GameAssembly), which SetMessage_Postfix
        /// already speaks, so nothing is synthesized here (FF1 parity).
        /// </summary>
        public static void StartPreeMptiveMes_Postfix()
        {
            try
            {
                FFII_ScreenReaderMod.SetBattleActive();
                BattleResultPatches.ResetForNewBattle();
                _battleEnding = false;
            }
            catch { }
        }

        /// <summary>
        /// Patches the BattleController end-of-battle callbacks (win / lose / escape fade-outs) and
        /// Exit(bool) so battle state clears on every exit path (FF1 BattleControllerPatches). All are
        /// real-bodied, unique-RVA methods (dump.cs:469532-469580).
        /// </summary>
        private static void PatchBattleEnd(HarmonyLib.Harmony harmony)
        {
            var endPostfix = new HarmonyMethod(AccessTools.Method(typeof(BattleMessagePatches), nameof(BattleEnd_Hook)));
            foreach (var name in new[] { "EndWinFadeOutCallback", "EndLoseFadeOutCallback", "EndEscapeFadeOut", "EndFadeOutCallback" })
            {
                try
                {
                    var method = AccessTools.Method(typeof(BattleController), name, Type.EmptyTypes);
                    if (method != null)
                        harmony.Patch(method, postfix: endPostfix);
                    else
                        MelonLogger.Warning($"[BattleMessage] BattleController.{name} not found");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[BattleMessage] Error patching {name}: {ex.Message}");
                }
            }

            try
            {
                var exitMethod = AccessTools.Method(typeof(BattleController), "Exit", new Type[] { typeof(bool) });
                if (exitMethod != null)
                    harmony.Patch(exitMethod, prefix: endPostfix);
                else
                    MelonLogger.Warning("[BattleMessage] BattleController.Exit not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleMessage] Error patching Exit: {ex.Message}");
            }
        }

        /// <summary>
        /// Battle ended (any path): clear battle state, guarded on IsInBattle so the callbacks can't
        /// clear anything outside a real battle. Leaves BattleResultActive to the field transition.
        /// </summary>
        public static void BattleEnd_Hook()
        {
            try
            {
                if (FFII_ScreenReaderMod.IsInBattle)
                    FFII_ScreenReaderMod.ClearBattleState();
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
            SetDamageSource(T("Poison"));
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
            ResetConditionDedup();
            _battleEnding = false;
            _pendingActorName = null;
            _pendingActorFrame = -1;
            _lastMessageText = null;
            _lastMessageFrame = -1;
        }
    }
}
