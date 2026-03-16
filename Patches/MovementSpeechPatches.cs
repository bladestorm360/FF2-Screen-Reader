using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Core;
using static FFII_ScreenReader.Utils.ModTextTranslator;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches vehicle boarding/disembarking using multi-layer event-driven hooks.
    /// Uses manual Harmony patching (FF2 requirement - attribute patches may crash).
    ///
    /// Hook Layers (most reliable first):
    /// 1. FieldController.ChangeTransportation - Primary, fires on all transportation changes
    /// 2. FieldPlayer.ChangeMoveState - Backup, catches state machine transitions
    /// 3. FieldPlayer.GetOn/GetOff - Secondary, specific boarding/disembarking events
    /// </summary>
    public static class MovementSpeechPatches
    {
        private static bool isPatched = false;

        // Track previous transportation for change detection (prevents duplicate announcements)
        private static int lastTransportationId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
        private static int lastAnnouncedTransportId = -1;

        /// <summary>
        /// Apply manual Harmony patches for vehicle events.
        /// Called from FFII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Primary hook - most reliable for all transportation changes
                TryPatchChangeTransportation(harmony);

                // Backup hook - catches state machine transitions
                TryPatchChangeMoveState(harmony);

                // Secondary hooks - specific boarding/disembarking events
                TryPatchGetOn(harmony);
                TryPatchGetOff(harmony);

                isPatched = true;
            }
            catch { }
        }

        /// <summary>
        /// Patch FieldController.ChangeTransportation - the central method for all transportation changes.
        /// This is the most reliable hook for detecting vehicle boarding/disembarking.
        /// Signature: public void ChangeTransportation(int transportationId, bool changeCollisionEnable = True, bool isBackground = False)
        /// </summary>
        private static void TryPatchChangeTransportation(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldControllerType = typeof(FieldController);
                MethodInfo targetMethod = null;

                foreach (var method in fieldControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "ChangeTransportation")
                    {
                        var parameters = method.GetParameters();
                        // ChangeTransportation(int transportationId, bool, bool)
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(ChangeTransportation_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[MoveState] Could not find ChangeTransportation method");
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for FieldController.ChangeTransportation - announces transportation changes.
        /// This is the primary hook for detecting vehicle boarding/disembarking.
        /// </summary>
        public static void ChangeTransportation_Postfix(int transportationId)
        {
            try
            {
                // Skip if same as last announced (prevents duplicates)
                if (transportationId == lastAnnouncedTransportId)
                    return;

                int previousId = lastTransportationId;
                lastTransportationId = transportationId;

                // Skip initial state
                if (previousId == -1)
                    return;

                // Skip intermediate/special states (CONTENT, SYMBOL, NONE)
                // These are used during cinematics and transitions, not actual vehicle changes
                if (IsIntermediateTransportation(transportationId))
                    return;

                bool wasOnVehicle = IsVehicleTransportation(previousId);
                bool isOnVehicle = IsVehicleTransportation(transportationId);
                bool isNowOnFoot = (transportationId == IL2CppOffsets.Transport.TRANSPORT_PLAYER);

                string announcement = null;

                if (!wasOnVehicle && isOnVehicle)
                {
                    // Boarding a vehicle
                    string vehicleName = GetTransportationName(transportationId);
                    if (!string.IsNullOrEmpty(vehicleName))
                    {
                        announcement = string.Format(T("On {0}"), vehicleName);
                        MoveStateHelper.SetVehicleState(transportationId);
                    }
                }
                else if (wasOnVehicle && isNowOnFoot)
                {
                    // Disembarking - specifically to TRANSPORT_PLAYER (on foot)
                    announcement = T("On foot");
                    MoveStateHelper.SetOnFoot();
                }

                if (announcement != null)
                {
                    lastAnnouncedTransportId = transportationId;
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in ChangeTransportation patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch FieldPlayer.ChangeMoveState - backup hook for state machine transitions.
        /// Signature: public void ChangeMoveState(MoveState moveState, bool ignoreStatusSwitchConfirm = False)
        /// Note: MoveState is enum but marshals as int in IL2CPP.
        /// </summary>
        private static void TryPatchChangeMoveState(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "ChangeMoveState")
                    {
                        var parameters = method.GetParameters();
                        // ChangeMoveState(MoveState, bool) - MoveState is enum (int)
                        if (parameters.Length >= 1)
                        {
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
                }
                else
                {
                    MelonLogger.Error("[MoveState] Could not find ChangeMoveState method");
                }
            }
            catch { }
        }

        // Track last move state for ChangeMoveState backup hook
        private static int lastMoveState = -1;

        /// <summary>
        /// Postfix for FieldPlayer.ChangeMoveState - backup hook for state transitions.
        /// Catches transitions that might bypass ChangeTransportation.
        /// </summary>
        public static void ChangeMoveState_Postfix(FieldPlayer __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                int currentMoveState = (int)__instance.moveState;

                // Only process if state actually changed
                if (currentMoveState != lastMoveState)
                {
                    int previousState = lastMoveState;
                    lastMoveState = currentMoveState;

                    // Skip first call (initialization)
                    if (previousState == -1)
                        return;

                    // Announce state change (handles both boarding and disembarking)
                    // Cache updates are handled by ChangeTransportation/GetOn/GetOff patches
                    MoveStateHelper.AnnounceStateChange(previousState, currentMoveState);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in ChangeMoveState patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a transportationId is an intermediate/special state (not a real vehicle or on-foot).
        /// These are used during cinematics and transitions.
        /// </summary>
        private static bool IsIntermediateTransportation(int transportationId)
        {
            return transportationId == IL2CppOffsets.Transport.TRANSPORT_NONE ||
                   transportationId == IL2CppOffsets.Transport.TRANSPORT_SYMBOL ||
                   transportationId == IL2CppOffsets.Transport.TRANSPORT_CONTENT;
        }

        /// <summary>
        /// Check if a transportationId represents a vehicle (not on foot or intermediate).
        /// </summary>
        private static bool IsVehicleTransportation(int transportationId)
        {
            return transportationId != IL2CppOffsets.Transport.TRANSPORT_NONE &&
                   transportationId != IL2CppOffsets.Transport.TRANSPORT_PLAYER &&
                   transportationId != IL2CppOffsets.Transport.TRANSPORT_SYMBOL &&
                   transportationId != IL2CppOffsets.Transport.TRANSPORT_CONTENT;
        }

        /// <summary>
        /// Patch GetOn - called when player boards a vehicle.
        /// Signature: public void GetOn(int typeId, bool isBackground = False)
        /// Note: This is a secondary hook; ChangeTransportation is primary.
        /// </summary>
        private static void TryPatchGetOn(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "GetOn")
                    {
                        var parameters = method.GetParameters();
                        // GetOn(int typeId, bool isBackground = False)
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(GetOn_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[MoveState] Could not find GetOn method");
                }
            }
            catch { }
        }

        /// <summary>
        /// Patch GetOff - called when player disembarks a vehicle.
        /// Signature: public void GetOff(int typeId, int layer = -1)
        /// Note: This is a secondary hook; ChangeTransportation is primary.
        /// </summary>
        private static void TryPatchGetOff(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "GetOff")
                    {
                        var parameters = method.GetParameters();
                        // GetOff(int typeId, int layer = -1)
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(GetOff_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[MoveState] Could not find GetOff method");
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for GetOn - announces vehicle boarding.
        /// Note: ChangeTransportation is the primary hook; this is supplementary.
        /// FF2 quirk: GetOn(TRANSPORT_PLAYER) is called when disembarking, not GetOff().
        /// </summary>
        public static void GetOn_Postfix(int typeId)
        {
            try
            {
                // FF2 quirk: GetOn(1) = TRANSPORT_PLAYER means disembarking from vehicle
                if (typeId == IL2CppOffsets.Transport.TRANSPORT_PLAYER)
                {
                    // Skip if already announced as on foot
                    if (lastAnnouncedTransportId == IL2CppOffsets.Transport.TRANSPORT_PLAYER)
                        return;

                    MoveStateHelper.SetOnFoot();
                    lastAnnouncedTransportId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
                    lastTransportationId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
                    FFII_ScreenReaderMod.SpeakText(T("On foot"), interrupt: false);
                    return;
                }

                // Skip if already announced via ChangeTransportation
                if (typeId == lastAnnouncedTransportId)
                    return;

                string vehicleName = GetTransportationName(typeId);
                if (!string.IsNullOrEmpty(vehicleName))
                {
                    MoveStateHelper.SetVehicleState(typeId);
                    lastAnnouncedTransportId = typeId;
                    lastTransportationId = typeId;
                    string announcement = string.Format(T("On {0}"), vehicleName);
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in GetOn patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GetOff - announces vehicle disembarking.
        /// Note: ChangeTransportation is the primary hook; this is supplementary.
        /// </summary>
        public static void GetOff_Postfix(int typeId)
        {
            try
            {
                // Skip if already announced via ChangeTransportation
                if (lastAnnouncedTransportId == IL2CppOffsets.Transport.TRANSPORT_PLAYER)
                    return;

                string vehicleName = GetTransportationName(typeId);
                MoveStateHelper.SetOnFoot();
                lastAnnouncedTransportId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
                lastTransportationId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;

                // Only announce "On foot" if we were on a known vehicle
                if (!string.IsNullOrEmpty(vehicleName))
                {
                    FFII_ScreenReaderMod.SpeakText(T("On foot"), interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in GetOff patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Get human-readable name for TransportationType.
        /// </summary>
        private static string GetTransportationName(int typeId)
        {
            switch (typeId)
            {
                case IL2CppOffsets.Transport.TRANSPORT_SHIP: return "ship";
                case IL2CppOffsets.Transport.TRANSPORT_PLANE: return "airship";
                case IL2CppOffsets.Transport.TRANSPORT_SUBMARINE: return "submarine";
                case IL2CppOffsets.Transport.TRANSPORT_LOWFLYING: return "airship";
                case IL2CppOffsets.Transport.TRANSPORT_SPECIALPLANE: return "airship";
                case IL2CppOffsets.Transport.TRANSPORT_YELLOWCHOCOBO: return "chocobo";
                case IL2CppOffsets.Transport.TRANSPORT_BLACKCHOCOBO: return "chocobo";
                case IL2CppOffsets.Transport.TRANSPORT_BOKO: return "chocobo";
                default: return null;
            }
        }

        /// <summary>
        /// Reset state tracking (call on map transitions).
        /// </summary>
        public static void ResetState()
        {
            lastTransportationId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
            lastAnnouncedTransportId = -1;
            lastMoveState = -1;
            MoveStateHelper.ResetState();
        }

        /// <summary>
        /// Sync tracking to on-foot state (called when entering interior maps).
        /// </summary>
        public static void SyncToOnFoot()
        {
            lastTransportationId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
            lastAnnouncedTransportId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
        }
    }
}
