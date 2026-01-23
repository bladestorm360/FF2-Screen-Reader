using System;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Field;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for game state transitions (field, battle, menu, etc.).
    /// Hooks SubSceneManagerMainGame.ChangeState for event-driven map transition
    /// and battle state management instead of per-frame polling.
    /// </summary>
    public static class GameStatePatches
    {
        // Field states that indicate player is on the field map
        private const int STATE_CHANGE_MAP = 1;
        private const int STATE_FIELD_READY = 2;
        private const int STATE_PLAYER = 3;
        private const int STATE_BATTLE = 13;

        private static int lastAnnouncedMapId = -1;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SubSceneManagerMainGame.ChangeState(State state)
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerMainGame),
                    "ChangeState",
                    new Type[] { typeof(SubSceneManagerMainGame.State) }
                );

                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GameStatePatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[GameState] Could not find SubSceneManagerMainGame.ChangeState method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when game state changes (field, battle, menu, etc.).
        /// Handles map transition announcements and battle state clearing.
        /// </summary>
        public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
        {
            try
            {
                int stateValue = (int)state;

                // When transitioning to field states, check for map changes and clear battle state
                if (stateValue == STATE_FIELD_READY || stateValue == STATE_PLAYER || stateValue == STATE_CHANGE_MAP)
                {
                    // Clear battle state if we were in battle
                    if (FFII_ScreenReaderMod.IsInBattle)
                    {
                        ClearAllBattleState();
                    }

                    // Check for map transition
                    CheckMapTransition();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks for map transitions and triggers entity rescan when map changes.
        /// Announces new map name and clears stale entity cache.
        /// </summary>
        private static void CheckMapTransition()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return;

                int currentMapId = userDataManager.CurrentMapId;

                if (currentMapId != lastAnnouncedMapId && lastAnnouncedMapId != -1)
                {
                    // Map has changed - announce new map
                    string mapName = MapNameResolver.GetCurrentMapName();
                    string announcement = $"Entering {mapName}";

                    // Record for deduplication before announcing
                    // This prevents the game's fade message (e.g., "Altair - 1F") from also being announced
                    LocationMessageTracker.SetLastMapTransition(announcement);

                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                    lastAnnouncedMapId = currentMapId;

                    // Clear vehicle type map so it gets repopulated with new map's vehicles
                    FieldNavigationHelper.ResetTransportationDebug();

                    // Force entity rescan to clear stale entities from previous map
                    FFII_ScreenReaderMod.Instance?.ForceEntityRescan();
                }
                else if (lastAnnouncedMapId == -1)
                {
                    // First run - store current map without announcing
                    lastAnnouncedMapId = currentMapId;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error in CheckMapTransition: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all battle-related state when transitioning out of battle.
        /// Handles victory, flee, defeat, and scripted battle exits.
        /// </summary>
        private static void ClearAllBattleState()
        {
            FFII_ScreenReaderMod.ClearBattleActive();
            BattleCommandState.ClearState();
            BattleTargetPatches.SetTargetSelectionActive(false);
            BattleCommandPatches.ResetTurnState();
            BattleCommandPatches.ResetCommandCursorState();
            BattleMagicMenuState.Reset();
            BattleItemMenuState.Reset();
            BattleMessagePatches.ResetState();
        }

    }
}
