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
                    MelonLogger.Error("[GameState] Could not find SubSceneManagerMainGame.ChangeState method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        // Config menu bestiary states (SubSceneManagerMainGame): FF2 uses MenuLibraryUi=17, MenuLibraryInfo=18.
        private const int STATE_MENU_LIBRARY_UI = 17;
        private const int STATE_MENU_LIBRARY_INFO = 18;

        /// <summary>
        /// Called when game state changes (field, battle, menu, etc.).
        /// Handles map transition announcements, battle state clearing,
        /// and config menu bestiary dispatch (states 17/18).
        /// </summary>
        public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
        {
            try
            {
                int stateValue = (int)state;

                // When transitioning to field states, check for map changes and clear battle state
                if (stateValue == IL2CppOffsets.GameState.STATE_FIELD_READY || stateValue == IL2CppOffsets.GameState.STATE_PLAYER || stateValue == IL2CppOffsets.GameState.STATE_CHANGE_MAP)
                {
                    // Clear battle state if we were in battle
                    if (FFII_ScreenReaderMod.IsInBattle)
                    {
                        ClearAllBattleState();
                    }

                    // If we were in config bestiary, handle exit
                    if (ConfigBestiaryStateHandler.WasInConfigBestiary)
                    {
                        ConfigBestiaryStateHandler.HandleExit();
                    }

                    // Check for map transition
                    CheckMapTransition();
                }
                // Config menu bestiary states
                else if (stateValue == STATE_MENU_LIBRARY_UI || stateValue == STATE_MENU_LIBRARY_INFO)
                {
                    ConfigBestiaryStateHandler.HandleStateChange(stateValue);
                }
                // Exiting config bestiary to another non-field state
                else if (ConfigBestiaryStateHandler.WasInConfigBestiary)
                {
                    ConfigBestiaryStateHandler.HandleExit();
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

                    // Map actually changed — clear any menu/popup flag that got stuck due to a
                    // set/clear patch mismatch, which would otherwise silently block field
                    // navigation on the new map.
                    FFII_ScreenReaderMod.ClearMenuFlagsForMapTransition();

                    // Clear vehicle type map so it gets repopulated with new map's vehicles
                    FieldNavigationHelper.ResetTransportationDebug();

                    // Force entity rescan to clear stale entities from previous map (silent —
                    // ForceEntityRescan now announces and is reserved for the manual ` key).
                    FFII_ScreenReaderMod.Instance?.RescanEntitiesSilent();
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
