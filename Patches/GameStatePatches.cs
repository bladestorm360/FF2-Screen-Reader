using System;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Field;
using static FFII_ScreenReader.Utils.ModTextTranslator;
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

        /// <summary>
        /// Called when game state changes (field, battle, menu, etc.).
        /// Handles map transition announcements, battle state clearing,
        /// and config menu bestiary dispatch (states 18/19).
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

                    // If we were in config bestiary, handle exit. Returning from the bestiary lands back
                    // on the config menu, which does not re-announce its focused row by itself — arm the
                    // re-announce (its deferred read speaks only while a config menu is open, so an exit
                    // that really went to the field stays silent).
                    if (ConfigBestiaryStateHandler.WasInConfigBestiary)
                    {
                        ConfigBestiaryStateHandler.HandleExit();
                        ConfigMenuPatches.ReannounceFocusedConfigOption();
                    }

                    // Check for map transition
                    CheckMapTransition();
                }
                // Config menu bestiary states
                else if (stateValue == ConfigBestiaryStateHandler.STATE_MENU_LIBRARY_UI
                      || stateValue == ConfigBestiaryStateHandler.STATE_MENU_LIBRARY_INFO)
                {
                    ConfigBestiaryStateHandler.HandleStateChange(stateValue);
                }
                // Exiting config bestiary to another non-field state (back to the config menu)
                else if (ConfigBestiaryStateHandler.WasInConfigBestiary)
                {
                    ConfigBestiaryStateHandler.HandleExit();
                    ConfigMenuPatches.ReannounceFocusedConfigOption();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks for map transitions and triggers entity rescan when map changes.
        /// Announces the new map name — including the first map after loading a save (FF1 parity) —
        /// and clears stale entity cache.
        /// </summary>
        private static void CheckMapTransition()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return;

                int currentMapId = userDataManager.CurrentMapId;
                if (currentMapId <= 0 || currentMapId == lastAnnouncedMapId)
                    return;

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

                // An unresolvable map name isn't worth announcing.
                string mapName = MapNameResolver.GetCurrentMapName();
                if (string.IsNullOrEmpty(mapName) || mapName == T("Unknown"))
                    return;

                string announcement = string.Format(T("Entering {0}"), mapName);

                // Record for deduplication before announcing
                // This prevents the game's fade message (e.g., "Altair - 1F") from also being announced
                LocationMessageTracker.SetLastMapTransition(announcement);

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
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
            FFII_ScreenReaderMod.ClearBattleState();
            // Result screen is done — re-enable the audio beacon now the player is back on the field.
            FFII_ScreenReaderMod.BattleResultActive = false;
        }

    }
}
