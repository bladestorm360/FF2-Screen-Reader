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
    /// 2. FieldPlayer.GetOn/GetOff - Secondary, specific boarding/disembarking events
    /// (The FieldPlayer.ChangeMoveState "backup" hook is gone: its only callers are the per-frame
    /// FieldPlayerKeyController/TouchBase OnTouchPadCallback movement paths, which only ever pass
    /// Walk/Dash — CLAUDE.md rule 3; FF1 has no such hook.)
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
                    string boarding = GetBoardingAnnouncement(transportationId);
                    if (!string.IsNullOrEmpty(boarding))
                    {
                        announcement = boarding;
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

                string announcement = GetBoardingAnnouncement(typeId);
                if (!string.IsNullOrEmpty(announcement))
                {
                    MoveStateHelper.SetVehicleState(typeId);
                    lastAnnouncedTransportId = typeId;
                    lastTransportationId = typeId;
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

                bool wasKnownVehicle = GetBoardingAnnouncement(typeId) != null;
                MoveStateHelper.SetOnFoot();
                lastAnnouncedTransportId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;
                lastTransportationId = IL2CppOffsets.Transport.TRANSPORT_PLAYER;

                // Only announce "On foot" if we were on a known vehicle
                if (wasKnownVehicle)
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
        private static string GetBoardingAnnouncement(int typeId)
        {
            switch (typeId)
            {
                case IL2CppOffsets.Transport.TRANSPORT_SHIP:
                case IL2CppOffsets.Transport.TRANSPORT_SUBMARINE: return T("On ship");
                case IL2CppOffsets.Transport.TRANSPORT_CONTENT: return T("On canoe");  // FF1 parity: canoe rides the Content slot
                case IL2CppOffsets.Transport.TRANSPORT_PLANE:
                case IL2CppOffsets.Transport.TRANSPORT_LOWFLYING:
                case IL2CppOffsets.Transport.TRANSPORT_SPECIALPLANE: return T("On airship");
                case IL2CppOffsets.Transport.TRANSPORT_YELLOWCHOCOBO:
                case IL2CppOffsets.Transport.TRANSPORT_BLACKCHOCOBO:
                case IL2CppOffsets.Transport.TRANSPORT_BOKO: return T("On chocobo");
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
