using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Entity.Field;
using UnityEngine;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches FieldPlayer.ChangeMoveState to announce when entering/exiting vehicles.
    /// Uses manual Harmony patching (required for IL2CPP - attribute patches crash).
    /// Ported from FF3/FF5 screen reader.
    ///
    /// FF2 vehicles: Canoe (rivers), Ship (ocean), Airship (air travel)
    /// </summary>
    public static class MovementSpeechPatches
    {
        private static bool isPatched = false;
        private static int lastMoveState = -1;

        /// <summary>
        /// Apply manual Harmony patches for movement state changes.
        /// Called from FFII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchChangeMoveState(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ChangeMoveState - called when player changes movement mode (walk, ship, airship, etc.)
        /// </summary>
        private static void TryPatchChangeMoveState(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);

                // Find ChangeMoveState(FieldPlayerConstants.MoveState, bool)
                var methods = fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "ChangeMoveState")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1)
                        {
                            MelonLogger.Msg($"[MoveState] Found ChangeMoveState with {parameters.Length} params");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(ChangeMoveState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MoveState] Patched ChangeMoveState successfully");
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find ChangeMoveState method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching ChangeMoveState: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ChangeMoveState - announces vehicle state changes.
        /// </summary>
        public static void ChangeMoveState_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var fieldPlayer = __instance as FieldPlayer;
                if (fieldPlayer == null)
                    return;

                int currentMoveState = (int)fieldPlayer.moveState;

                // Only announce if state actually changed
                if (currentMoveState != lastMoveState)
                {
                    // Update cached state in MoveStateHelper (this will also announce the change)
                    MoveStateHelper.UpdateCachedMoveState(currentMoveState);
                    lastMoveState = currentMoveState;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in ChangeMoveState patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state tracking (call on map transitions)
        /// </summary>
        public static void ResetState()
        {
            lastMoveState = -1;
            MoveStateHelper.ResetState();
        }
    }

    /// <summary>
    /// Proactive state monitoring for world map contexts (where ships/vehicles are available).
    /// Uses coroutine to check state every 0.5 seconds, announcing changes immediately.
    /// </summary>
    public static class MoveStateMonitor
    {
        private static object stateMonitorCoroutine = null;
        private static int lastKnownState = -1;
        private const float STATE_CHECK_INTERVAL = 0.5f;

        /// <summary>
        /// Coroutine that monitors move state changes every 0.5 seconds.
        /// Only runs when on world map (where ships/vehicles are available).
        /// </summary>
        private static IEnumerator MonitorMoveStateChanges()
        {
            while (true)
            {
                yield return new WaitForSeconds(STATE_CHECK_INTERVAL);

                try
                {
                    // Read current state and detect changes
                    int currentState = MoveStateHelper.GetCurrentMoveState();

                    // Only announce if state actually changed (skip initial -1 state)
                    if (currentState != lastKnownState && lastKnownState != -1)
                    {
                        MoveStateHelper.AnnounceStateChange(lastKnownState, currentState);
                    }

                    lastKnownState = currentState;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[MoveState] Error in state monitoring coroutine: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Start state monitoring coroutine when entering world map.
        /// </summary>
        public static void StartStateMonitoring()
        {
            if (stateMonitorCoroutine == null)
            {
                lastKnownState = -1; // Reset state tracking
                stateMonitorCoroutine = MelonCoroutines.Start(MonitorMoveStateChanges());
                MelonLogger.Msg("[MoveState] Started state monitoring coroutine");
            }
        }

        /// <summary>
        /// Stop state monitoring coroutine when leaving world map.
        /// </summary>
        public static void StopStateMonitoring()
        {
            if (stateMonitorCoroutine != null)
            {
                MelonCoroutines.Stop(stateMonitorCoroutine);
                stateMonitorCoroutine = null;
                lastKnownState = -1;
                MelonLogger.Msg("[MoveState] Stopped state monitoring coroutine");
            }
        }

        /// <summary>
        /// Reset state (call on map transitions)
        /// </summary>
        public static void ResetState()
        {
            lastKnownState = -1;
        }
    }
}
