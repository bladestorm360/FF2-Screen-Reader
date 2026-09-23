using System;
using System.Collections;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Battle;
using Il2CppLast.Data.User;
using Il2CppLast.Management;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for battle command and target selection (FF1 model).
    /// "X's turn" on SetCommandData; the focused command on SetCursor, only inside the actor's
    /// command window (opened by the SetCommandData postfix, closed by its prefix and by the target
    /// window's hide on commit) and deduplicated on the command's identity; a one-frame-deferred
    /// re-announce when the player backs out of targeting / the spell or item list.
    /// </summary>
    public static class BattleCommandPatches
    {
        /// <summary>
        /// Apply all battle command patches manually.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SetCommandData for turn announcements (single overload, dump.cs BattleCommandSelectController)
                var setCommandDataMethod = AccessTools.Method(typeof(BattleCommandSelectController), "SetCommandData");
                if (setCommandDataMethod != null)
                {
                    var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SetCommandData_Prefix));
                    var postfix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SetCommandData_Postfix));
                    harmony.Patch(setCommandDataMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                }

                // Patch SetCursor for command selection
                var setCursorMethod = AccessTools.Method(typeof(BattleCommandSelectController), "SetCursor", new Type[] { typeof(int) });
                if (setCursorMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SetCursor_Postfix));
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch ShowWindow for target selection tracking
                var showWindowMethod = AccessTools.Method(typeof(BattleTargetSelectController), "ShowWindow");
                if (showWindowMethod != null)
                {
                    var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(ShowWindow_Prefix));
                    harmony.Patch(showWindowMethod, prefix: new HarmonyMethod(prefix));
                }

                // Patch SelectContent for player targets
                var selectContentPlayerMethod = AccessTools.Method(
                    typeof(BattleTargetSelectController),
                    "SelectContent",
                    new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData>), typeof(int) }
                );
                if (selectContentPlayerMethod != null)
                {
                    var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentTarget_Prefix));
                    var postfix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentPlayer_Postfix));
                    harmony.Patch(selectContentPlayerMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                }

                // Patch SelectContent for enemy targets
                var selectContentEnemyMethod = AccessTools.Method(
                    typeof(BattleTargetSelectController),
                    "SelectContent",
                    new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData>), typeof(int) }
                );
                if (selectContentEnemyMethod != null)
                {
                    var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentTarget_Prefix));
                    var postfix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentEnemy_Postfix));
                    harmony.Patch(selectContentEnemyMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                }

                // Single-target state entry (real-bodied; dump.cs BattleTargetSelectController): re-arm the
                // initial-target read, then read the focused target one frame later if SelectContent hasn't.
                // EnemysInit calls SelectContent itself (verified in GameAssembly), PlayerInit does not.
                PatchTargetInit(harmony, "EnemysInit", nameof(EnemysInit_Postfix));
                PatchTargetInit(harmony, "PlayerInit", nameof(PlayerInit_Postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleCommand] Error applying patches: {ex.Message}");
            }
        }

        private static void PatchTargetInit(HarmonyLib.Harmony harmony, string methodName, string postfixName)
        {
            try
            {
                var method = AccessTools.Method(typeof(BattleTargetSelectController), methodName, Type.EmptyTypes);
                if (method == null)
                {
                    MelonLogger.Warning($"[BattleCommand] BattleTargetSelectController.{methodName} not found");
                    return;
                }
                var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(TargetInit_Prefix));
                var postfix = AccessTools.Method(typeof(BattleCommandPatches), postfixName);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleCommand] Error patching {methodName}: {ex.Message}");
            }
        }

        #region SetCommandData - Turn Announcements

        private static int lastCharacterId = -1;

        // Command-announce window: opened by the SetCommandData postfix ("X's turn"), closed by the
        // SetCommandData prefix and by the target window's hide (the actor's commit — where the game's
        // spurious cursor resets fire). SetCursor only announces while this is true.
        private static bool commandTurnReady;

        // Message id of the last command announced. Keyed on the command identity, not the cursor
        // index: left/right switches command pages with the index unchanged. Reset every turn.
        private static string lastAnnouncedCmdMesId;

        // Command back-out re-announce one-shot: armed on entering targeting or the spell/item list.
        // The next SetCursor defers one frame and speaks only if no commit signal appeared (a commit's
        // teardown burst and a cancel-return are identical at the SetCursor instant).
        private static bool commandReannouncePending;

        // Bumped on every SetCursor so a later cursor event supersedes a pending deferred re-announce.
        private static int reannounceGen;

        // Bumped in the SetCommandData prefix: a change while a re-announce is deferred means a new
        // turn started — a commit.
        private static int setCommandDataSeq;

        /// <summary>The party member whose command is being selected (H key).</summary>
        public static OwnedCharacterData CurrentActor { get; private set; }

        /// <summary>
        /// Prefix for SetCommandData - closes the command-announce window before the body runs, so the
        /// cursor resets fired during the actor handoff stay silent.
        /// </summary>
        public static void SetCommandData_Prefix()
        {
            commandTurnReady = false;
            lastAnnouncedCmdMesId = null;
            commandReannouncePending = false;
            setCommandDataSeq++;
        }

        public static void SetCommandData_Postfix(BattleCommandSelectController __instance, OwnedCharacterData data)
        {
            try
            {
                if (data == null) return;

                // Open the window before the same-character early return: re-entry after cancelling
                // back to the command menu is still that actor's input turn.
                commandTurnReady = true;
                defaultTargetAnnounced = false;
                CurrentActor = data;

                // Mark that we're in an active battle (suppresses MenuTextDiscovery)
                FFII_ScreenReaderMod.SetBattleActive();

                int characterId = data.Id;
                if (characterId == lastCharacterId) return;
                lastCharacterId = characterId;

                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName)) return;

                // Mark battle command as active (suppresses generic cursor)
                BattleCommandState.SetActive();

                FFII_ScreenReaderMod.SpeakText(string.Format(T("{0}'s turn"), characterName), interrupt: true);
            }
            catch { }
        }

        /// <summary>
        /// Resets all turn/command/target tracking (battle end, scene change, title return).
        /// </summary>
        public static void ResetTurnState()
        {
            lastCharacterId = -1;
            commandTurnReady = false;
            lastAnnouncedCmdMesId = null;
            commandReannouncePending = false;
            defaultTargetAnnounced = false;
            CurrentActor = null;
        }

        /// <summary>
        /// Called when the battle spell/item list announces its focused entry — the player is in their
        /// input phase under the command menu. Re-opens the command window (a target cancel's hide closed
        /// it) and arms the back-out re-announce. The list never announces during a commit.
        /// </summary>
        public static void NotifyCommandSubmenuActive()
        {
            commandTurnReady = true;
            commandReannouncePending = true;
        }

        #endregion

        #region SetCursor - Command Selection

        public static void SetCursor_Postfix(BattleCommandSelectController __instance, int index)
        {
            try
            {
                if (__instance == null) return;

                int myGen = ++reannounceGen;

                // The cursor resets to index 0 one frame before the command menu goes inactive at
                // end-of-turn; without this guard that reset speaks "Attack" every turn transition.
                if (!__instance.gameObject.activeInHierarchy) return;

                if (!commandTurnReady) return;

                // Back on the command menu: it owns the generic-cursor suppression again.
                MenuStateRegistry.SetActive(MenuStateRegistry.BATTLE_COMMAND, true);
                BattleMagicMenuState.Reset();
                BattleItemMenuState.Reset();

                if (BattleTargetPatches.CheckTargetSelectionActive())
                    return;

                if (commandReannouncePending)
                {
                    commandReannouncePending = false;
                    CoroutineManager.StartManaged(DeferredCommandReannounce(__instance, index, myGen, setCommandDataSeq));
                    return;
                }

                AnnounceCommandAt(__instance, index);
            }
            catch { }
        }

        /// <summary>
        /// One-frame-deferred back-out re-announce: speaks only when no commit signal appeared in the
        /// meantime (the target window's hide closes the command window; a turn handoff bumps the
        /// SetCommandData sequence) and no later cursor event superseded it.
        /// </summary>
        private static IEnumerator DeferredCommandReannounce(BattleCommandSelectController controller, int index, int gen, int seq)
        {
            yield return null;

            if (gen != reannounceGen) yield break;
            if (!commandTurnReady) yield break;
            if (seq != setCommandDataSeq) yield break;
            if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy) yield break;

            // Confirmed cancel-return: clear the dedup once so the focused command speaks again.
            lastAnnouncedCmdMesId = null;
            AnnounceCommandAt(controller, index);
        }

        /// <summary>
        /// Announces contentList[index] with its "(X of Y)" among the page's active commands,
        /// deduplicated on the command's message id.
        /// </summary>
        private static void AnnounceCommandAt(BattleCommandSelectController controller, int index)
        {
            var contentList = controller.contentList;
            if (contentList == null || contentList.Count == 0) return;
            if (index < 0 || index >= contentList.Count) return;

            var contentController = contentList[index];
            if (contentController == null || contentController.TargetCommand == null) return;

            string mesIdName = contentController.TargetCommand.MesIdName;
            if (string.IsNullOrWhiteSpace(mesIdName)) return;
            if (mesIdName == lastAnnouncedCmdMesId) return;

            var messageManager = MessageManager.Instance;
            if (messageManager == null) return;

            string commandName = messageManager.GetMessage(mesIdName);
            if (string.IsNullOrWhiteSpace(commandName)) return;
            commandName = TextUtils.StripIconMarkup(commandName);

            // contentList is a fixed slot list — count only populated, active slots on this page.
            int visibleCount = 0;
            for (int i = 0; i < contentList.Count; i++)
            {
                try
                {
                    var cc = contentList[i];
                    if (cc != null && cc.TargetCommand != null && cc.gameObject != null && cc.gameObject.activeInHierarchy)
                        visibleCount++;
                }
                catch { }
            }
            if (visibleCount <= 0) visibleCount = contentList.Count;

            lastAnnouncedCmdMesId = mesIdName;
            // Queues after the turn announcement.
            FFII_ScreenReaderMod.SpeakText(MenuPosition.Format(commandName, index, visibleCount), interrupt: false);
        }

        #endregion

        #region ShowWindow / SelectContent - Target Selection

        // One-shot: the focused target was announced for this target session (by SelectContent or the
        // delayed initial read). Re-armed on each single-target state entry and each turn.
        private static bool defaultTargetAnnounced;

        public static void ShowWindow_Prefix(BattleTargetSelectController __instance, bool isShow)
        {
            try
            {
                // The native body (RVA 0x2CAE40) is shared with nine other bool setters
                // (CharacterContentController.SetEnableCharacterContent, ResultSkillView.SetEnableSkillView,
                // ShopCharaStatusContentController.SetActive, ...), so this detour fires for all of them.
                // Act only when the instance really is the target-select controller.
                if (!IsTargetSelectController(__instance))
                    return;

                BattleTargetPatches.SetTargetSelectionActive(isShow);

                // Hide = the actor committed / targeting torn down: close the command window so the
                // handoff cursor resets stay silent.
                if (!isShow)
                    commandTurnReady = false;
            }
            catch { }
        }

        /// <summary>True when the native object's IL2CPP class is (or derives from) the KeyInput
        /// BattleTargetSelectController — the managed wrapper type alone proves nothing on a shared body.</summary>
        private static bool IsTargetSelectController(BattleTargetSelectController instance)
        {
            if (instance == null || instance.Pointer == IntPtr.Zero)
                return false;
            IntPtr cls = Il2CppInterop.Runtime.Il2CppClassPointerStore<BattleTargetSelectController>.NativeClassPtr;
            return cls != IntPtr.Zero
                && Il2CppInterop.Runtime.IL2CPP.il2cpp_class_is_assignable_from(
                    cls, Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance.Pointer));
        }

        public static void SelectContentTarget_Prefix()
        {
            BattleTargetPatches.SetTargetSelectionActive(true);
        }

        public static void SelectContentPlayer_Postfix(
            BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData> list,
            int index)
        {
            try
            {
                defaultTargetAnnounced = true;
                AnnouncePlayerTarget(list?.TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>(), index);
            }
            catch { }
        }

        public static void SelectContentEnemy_Postfix(
            BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> list,
            int index)
        {
            try
            {
                defaultTargetAnnounced = true;
                AnnounceEnemyTarget(list?.TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>(), index);
            }
            catch { }
        }

        /// <summary>Re-arms the initial-target read BEFORE the Init body (which may call SelectContent).</summary>
        public static void TargetInit_Prefix()
        {
            defaultTargetAnnounced = false;
        }

        public static void EnemysInit_Postfix(BattleTargetSelectController __instance)
            => CoroutineManager.StartManaged(DelayedInitialTarget(__instance, isEnemy: true));

        public static void PlayerInit_Postfix(BattleTargetSelectController __instance)
            => CoroutineManager.StartManaged(DelayedInitialTarget(__instance, isEnemy: false));

        /// <summary>
        /// One frame after a single-target state opens, announces the focused target unless SelectContent
        /// already did (ally targeting never calls it on open). Reads the controller's selectCursor and
        /// working list (dump.cs:434914 — same layout as FF1: playerDataList 0x30 / enemyDataList 0x38,
        /// TargetPlayerList 0x88 / TargetEnamyList 0x90 fallbacks, selectCursor 0xC0).
        /// </summary>
        private static IEnumerator DelayedInitialTarget(BattleTargetSelectController controller, bool isEnemy)
        {
            yield return null;

            if (defaultTargetAnnounced) yield break;
            try
            {
                IntPtr ptr = controller?.Pointer ?? IntPtr.Zero;
                if (ptr == IntPtr.Zero) yield break;

                IntPtr cursorPtr = Marshal.ReadIntPtr(ptr, IL2CppOffsets.BattleTarget.SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero) yield break;
                int index = new GameCursor(cursorPtr).Index;
                if (index < 0) yield break;

                if (isEnemy)
                {
                    var list = ReadList<BattleEnemyData>(ptr, IL2CppOffsets.BattleTarget.ENEMY_DATA_LIST)
                               ?? ReadList<BattleEnemyData>(ptr, IL2CppOffsets.BattleTarget.TARGET_ENEMY_LIST);
                    if (list == null || index >= list.Count) yield break;
                    defaultTargetAnnounced = true;
                    AnnounceEnemyTarget(list, index);
                }
                else
                {
                    var list = ReadList<BattlePlayerData>(ptr, IL2CppOffsets.BattleTarget.PLAYER_DATA_LIST)
                               ?? ReadList<BattlePlayerData>(ptr, IL2CppOffsets.BattleTarget.TARGET_PLAYER_LIST);
                    if (list == null || index >= list.Count) yield break;
                    defaultTargetAnnounced = true;
                    AnnouncePlayerTarget(list, index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleCommand] Error reading initial target: {ex.Message}");
            }
        }

        /// <summary>Reads a non-empty target-list field as List&lt;T&gt; (null if absent, empty or not a List).</summary>
        private static Il2CppSystem.Collections.Generic.List<T> ReadList<T>(IntPtr ctrlPtr, int offset)
        {
            IntPtr p = Marshal.ReadIntPtr(ctrlPtr, offset);
            if (p == IntPtr.Zero) return null;
            var list = new Il2CppSystem.Object(p).TryCast<Il2CppSystem.Collections.Generic.List<T>>();
            return list != null && list.Count > 0 ? list : null;
        }

        public static void AnnouncePlayerTarget(Il2CppSystem.Collections.Generic.List<BattlePlayerData> playerList, int index)
        {
            try
            {
                if (playerList == null || playerList.Count == 0) return;
                if (index < 0 || index >= playerList.Count) return;

                var selectedPlayer = playerList[index];
                if (selectedPlayer == null) return;

                string name = T("Unknown");
                int currentHp = 0, maxHp = 0;
                int currentMp = 0, maxMp = 0;

                var ownedCharData = selectedPlayer.ownedCharacterData;
                if (ownedCharData != null)
                {
                    name = ownedCharData.Name;
                    var charParam = ownedCharData.Parameter;
                    if (charParam != null)
                    {
                        try
                        {
                            maxHp = charParam.ConfirmedMaxHp();
                            maxMp = charParam.ConfirmedMaxMp();
                        }
                        catch { }
                    }
                }

                var battleInfo = selectedPlayer.BattleUnitDataInfo;
                if (battleInfo?.Parameter != null)
                {
                    currentHp = battleInfo.Parameter.CurrentHP;
                    currentMp = battleInfo.Parameter.CurrentMP;
                    if (maxHp == 0)
                    {
                        try
                        {
                            maxHp = battleInfo.Parameter.ConfirmedMaxHp();
                        }
                        catch
                        {
                            maxHp = battleInfo.Parameter.BaseMaxHp;
                        }
                    }
                    if (maxMp == 0)
                    {
                        try
                        {
                            maxMp = battleInfo.Parameter.ConfirmedMaxMp();
                        }
                        catch
                        {
                            maxMp = battleInfo.Parameter.BaseMaxMp;
                        }
                    }
                }

                // FF2 uses MP (unlike FF3 which uses spell charges)
                string announcement = string.Format(T("{0}: HP {1}/{2}, MP {3}/{4}"), name, currentHp, maxHp, currentMp, maxMp);
                announcement += BuildStatusSuffix(battleInfo?.Parameter);
                announcement = MenuPosition.Format(announcement, index, playerList.Count);

                // Entering targeting = left the command menu: arm the back-out re-announce (commit-safe:
                // SetCursor returns on the still-active target window before consuming it).
                commandReannouncePending = true;
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch { }
        }

        public static void AnnounceEnemyTarget(Il2CppSystem.Collections.Generic.List<BattleEnemyData> enemyList, int index)
        {
            try
            {
                if (enemyList == null || enemyList.Count == 0) return;
                if (index < 0 || index >= enemyList.Count) return;

                var selectedEnemy = enemyList[index];
                if (selectedEnemy == null) return;

                string name = T("Unknown");
                int currentHp = 0, maxHp = 0;

                try
                {
                    string mesIdName = selectedEnemy.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            name = localizedName;
                        }
                    }
                }
                catch { }

                var battleInfo = selectedEnemy.BattleUnitDataInfo;
                if (battleInfo?.Parameter != null)
                {
                    currentHp = battleInfo.Parameter.CurrentHP;
                    try
                    {
                        maxHp = battleInfo.Parameter.ConfirmedMaxHp();
                    }
                    catch
                    {
                        maxHp = battleInfo.Parameter.BaseMaxHp;
                    }
                }

                // Check for multiple enemies with same name
                int sameNameCount = 0;
                int positionInGroup = 0;
                var messageManagerForCount = MessageManager.Instance;

                for (int i = 0; i < enemyList.Count; i++)
                {
                    var enemy = enemyList[i];
                    if (enemy != null)
                    {
                        try
                        {
                            string enemyMesId = enemy.GetMesIdName();
                            if (!string.IsNullOrEmpty(enemyMesId) && messageManagerForCount != null)
                            {
                                string enemyName = messageManagerForCount.GetMessage(enemyMesId);
                                if (enemyName == name)
                                {
                                    sameNameCount++;
                                    if (i < index) positionInGroup++;
                                }
                            }
                        }
                        catch { }
                    }
                }

                string announcement = name;
                if (sameNameCount > 1)
                {
                    char letter = (char)('A' + positionInGroup);
                    announcement += $" {letter}";
                }

                // Enemy HP display mode (0=Numbers, 1=Percentage, 2=Hidden)
                switch (PreferencesManager.EnemyHPDisplay)
                {
                    case 1:
                        int pct = maxHp > 0 ? currentHp * 100 / maxHp : 0;
                        announcement = string.Format(T("{0}: HP {1} percent"), announcement, pct);
                        break;
                    case 2:
                        // Hidden — announce name only
                        break;
                    default:
                        announcement = string.Format(T("{0}: HP {1}/{2}"), announcement, currentHp, maxHp);
                        break;
                }

                announcement += BuildStatusSuffix(battleInfo?.Parameter);
                announcement = MenuPosition.Format(announcement, index, enemyList.Count);

                // Entering targeting = left the command menu: arm the back-out re-announce.
                commandReannouncePending = true;
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch { }
        }

        /// <summary>
        /// ", Poison, Blind" suffix from a battle unit's current conditions (empty when none).
        /// </summary>
        internal static string BuildStatusSuffix(Il2CppLast.Data.CharacterParameterBase parameter)
        {
            if (parameter == null) return string.Empty;
            try
            {
                var conditionList = parameter.CurrentConditionList;
                if (conditionList == null || conditionList.Count == 0) return string.Empty;

                var names = new System.Collections.Generic.List<string>();
                foreach (var condition in conditionList)
                {
                    string n = LocalizationUtility.GetConditionName(condition);
                    if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n)) names.Add(n);
                }
                return names.Count == 0 ? string.Empty : ", " + string.Join(", ", names);
            }
            catch { return string.Empty; }
        }

        #endregion
    }

    /// <summary>
    /// State tracker for battle commands - prevents duplicate cursor announcements.
    /// Part of the Active State Pattern ported from FF3.
    /// </summary>
    public static class BattleCommandState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.BATTLE_COMMAND);

        static BattleCommandState()
        {
            _helper.RegisterResetHandler();
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        /// <summary>
        /// Check if GenericCursor announcements should be suppressed.
        /// Returns false if controller is gone (auto-resets stuck flags).
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                var controller = GameObjectCache.GetOrRefresh<BattleCommandSelectController>();
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    ClearState();
                    return false;
                }
                return true;
            }
            catch
            {
                ClearState();
                return false;
            }
        }

        public static void ClearState() => _helper.IsActive = false;
    }

    /// <summary>
    /// Tracks battle target selection state.
    /// Part of the Active State Pattern ported from FF3.
    /// </summary>
    public static class BattleTargetPatches
    {
        /// <summary>
        /// True when target selection is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsTargetSelectionActive => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_TARGET);

        /// <summary>
        /// Check if GenericCursor announcements should be suppressed.
        /// Returns false if controller is gone (auto-resets stuck flags).
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsTargetSelectionActive) return false;

            try
            {
                var controller = GameObjectCache.GetOrRefresh<BattleTargetSelectController>();
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    MenuStateRegistry.Reset(MenuStateRegistry.BATTLE_TARGET);
                    return false;
                }
                return true;
            }
            catch
            {
                MenuStateRegistry.Reset(MenuStateRegistry.BATTLE_TARGET);
                return false;
            }
        }

        /// <summary>
        /// Set target selection active state and clear other menus if activating.
        /// </summary>
        public static void SetTargetSelectionActive(bool active)
        {
            if (active)
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_TARGET);
            else
                MenuStateRegistry.Reset(MenuStateRegistry.BATTLE_TARGET);
        }

        /// <summary>
        /// Target-selection state for the command cursor (FF1 CheckAndUpdateTargetSelectionActive): trusts
        /// a cleared flag; a set flag is re-verified against the target controller actually showing its
        /// view (any active child), clearing it when the window is gone.
        /// </summary>
        public static bool CheckTargetSelectionActive()
        {
            if (!IsTargetSelectionActive) return false;
            try
            {
                var controller = GameObjectCache.GetOrRefresh<BattleTargetSelectController>();
                bool shown = false;
                if (controller != null)
                {
                    var children = controller.GetComponentsInChildren<UnityEngine.Transform>(false);
                    foreach (var child in children)
                    {
                        if (child != null && child.gameObject != controller.gameObject) { shown = true; break; }
                    }
                }
                if (!shown)
                    SetTargetSelectionActive(false);
                return shown;
            }
            catch
            {
                return IsTargetSelectionActive;
            }
        }
    }
}
