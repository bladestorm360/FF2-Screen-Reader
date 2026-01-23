using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Battle;
using Il2CppLast.Data.User;
using Il2CppLast.Management;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using BattlePlayerData = Il2Cpp.BattlePlayerData;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for battle command and target selection.
    /// Ported from FF3 screen reader with FF2-specific adjustments.
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
                // Patch SetCommandData for turn announcements
                var setCommandDataMethod = AccessTools.Method(typeof(BattleCommandSelectController), "SetCommandData");
                if (setCommandDataMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SetCommandData_Postfix));
                    harmony.Patch(setCommandDataMethod, postfix: new HarmonyMethod(postfix));
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
                    var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentPlayer_Prefix));
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
                    var prefix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentEnemy_Prefix));
                    var postfix = AccessTools.Method(typeof(BattleCommandPatches), nameof(SelectContentEnemy_Postfix));
                    harmony.Patch(selectContentEnemyMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BattleCommand] Error applying patches: {ex.Message}");
            }
        }

        #region SetCommandData - Turn Announcements

        private static int lastCharacterId = -1;

        public static void SetCommandData_Postfix(BattleCommandSelectController __instance, OwnedCharacterData data)
        {
            try
            {
                if (data == null) return;

                int characterId = data.Id;
                if (characterId == lastCharacterId) return;
                lastCharacterId = characterId;

                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName)) return;

                // Mark that we're in an active battle (suppresses MenuTextDiscovery)
                FFII_ScreenReaderMod.SetBattleActive();

                // Mark battle command as active (suppresses generic cursor)
                BattleCommandState.SetActive();

                // Reset tracking for new turn
                BattleTargetPatches.ResetState();
                ResetCommandCursorState();

                string announcement = $"{characterName}'s turn";
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetCommandData patch: {ex.Message}");
            }
        }

        public static void ResetTurnState()
        {
            lastCharacterId = -1;
        }

        #endregion

        #region SetCursor - Command Selection

        private static int lastAnnouncedCommandIndex = -1;

        public static void SetCursor_Postfix(BattleCommandSelectController __instance, int index)
        {
            try
            {
                if (__instance == null) return;

                // FIX: When SetCursor is called on the command menu, we're back in command selection.
                // Clear target selection state to fix the backing out bug where the flag gets stuck.
                if (BattleTargetPatches.IsTargetSelectionActive)
                {
                    BattleTargetPatches.SetTargetSelectionActive(false);
                }

                // Skip duplicate announcements
                if (index == lastAnnouncedCommandIndex)
                    return;
                lastAnnouncedCommandIndex = index;

                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0) return;
                if (index < 0 || index >= contentList.Count) return;

                var contentController = contentList[index];
                if (contentController == null || contentController.TargetCommand == null) return;

                string mesIdName = contentController.TargetCommand.MesIdName;
                if (string.IsNullOrWhiteSpace(mesIdName)) return;

                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                string commandName = messageManager.GetMessage(mesIdName);
                if (string.IsNullOrWhiteSpace(commandName)) return;

                // Use delayed speech - allows target selection to activate before speaking
                CoroutineManager.StartManaged(DelayedCommandSpeech(commandName));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetCursor patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedCommandSpeech(string text)
        {
            yield return null; // Wait one frame

            // Check if target selection became active during the delay
            if (BattleTargetPatches.IsTargetSelectionActive)
                yield break;

            // Check if we're still in battle (prevents "Attack" on return to title)
            if (!FFII_ScreenReaderMod.IsInBattle)
                yield break;

            FFII_ScreenReaderMod.SpeakText(text, interrupt: false);
        }

        public static void ResetCommandCursorState()
        {
            lastAnnouncedCommandIndex = -1;
        }

        #endregion

        #region ShowWindow - Target Selection Tracking

        public static void ShowWindow_Prefix(BattleTargetSelectController __instance, bool isShow)
        {
            try
            {
                BattleTargetPatches.SetTargetSelectionActive(isShow);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowWindow patch: {ex.Message}");
            }
        }

        #endregion

        #region SelectContent - Player Target

        public static void SelectContentPlayer_Prefix()
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
                BattleTargetPatches.AnnouncePlayerTarget(list, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent(Player) patch: {ex.Message}");
            }
        }

        #endregion

        #region SelectContent - Enemy Target

        public static void SelectContentEnemy_Prefix()
        {
            BattleTargetPatches.SetTargetSelectionActive(true);
        }

        public static void SelectContentEnemy_Postfix(
            BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> list,
            int index)
        {
            try
            {
                BattleTargetPatches.AnnounceEnemyTarget(list, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent(Enemy) patch: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// State tracker for battle commands - prevents duplicate cursor announcements.
    /// Part of the Active State Pattern ported from FF3.
    /// </summary>
    public static class BattleCommandState
    {
        /// <summary>
        /// True when battle command menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_COMMAND);

        /// <summary>
        /// Called when battle command menu activates.
        /// Clears other menu states to prevent conflicts.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_COMMAND);
        }

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

        /// <summary>
        /// Clear state when battle command menu closes.
        /// </summary>
        public static void ClearState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.BATTLE_COMMAND);
        }
    }

    /// <summary>
    /// Tracks battle target selection state and handles target announcements.
    /// Part of the Active State Pattern ported from FF3.
    /// </summary>
    public static class BattleTargetPatches
    {
        private static int lastAnnouncedPlayerIndex = -1;
        private static int lastAnnouncedEnemyIndex = -1;

        /// <summary>
        /// True when target selection is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsTargetSelectionActive => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_TARGET);

        /// <summary>
        /// Reset target tracking indices.
        /// </summary>
        public static void ResetState()
        {
            lastAnnouncedPlayerIndex = -1;
            lastAnnouncedEnemyIndex = -1;
        }

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
            {
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_TARGET);
                // Only reset target tracking when entering target selection
                ResetState();
            }
            else
            {
                MenuStateRegistry.Reset(MenuStateRegistry.BATTLE_TARGET);
            }
        }

        public static void AnnouncePlayerTarget(Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData> list, int index)
        {
            try
            {
                if (index == lastAnnouncedPlayerIndex) return;
                lastAnnouncedPlayerIndex = index;
                lastAnnouncedEnemyIndex = -1;

                var playerList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>();
                if (playerList == null || playerList.Count == 0) return;
                if (index < 0 || index >= playerList.Count) return;

                var selectedPlayer = playerList[index];
                if (selectedPlayer == null) return;

                string name = "Unknown";
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
                string announcement = $"{name}: HP {currentHp}/{maxHp}, MP {currentMp}/{maxMp}";
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing player target: {ex.Message}");
            }
        }

        public static void AnnounceEnemyTarget(Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> list, int index)
        {
            try
            {
                if (index == lastAnnouncedEnemyIndex) return;
                lastAnnouncedEnemyIndex = index;
                lastAnnouncedPlayerIndex = -1;

                var enemyList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>();
                if (enemyList == null || enemyList.Count == 0) return;
                if (index < 0 || index >= enemyList.Count) return;

                var selectedEnemy = enemyList[index];
                if (selectedEnemy == null) return;

                string name = "Unknown";
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
                announcement += $": HP {currentHp}/{maxHp}";

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing enemy target: {ex.Message}");
            }
        }
    }
}
