using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Utils;
using FieldKeyController = Il2CppLast.OutGame.Library.FieldKeyController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches FieldKeyController.SetDashFlag to track walk/run toggle state.
    /// The game calls SetDashFlag when the player presses F1 to toggle walk/run.
    /// </summary>
    public static class DashFlagPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patch for SetDashFlag.
        /// Called from FFII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchSetDashFlag(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[DashFlag] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SetDashFlag - called when player toggles walk/run with F1.
        /// </summary>
        private static void TryPatchSetDashFlag(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldKeyControllerType = typeof(FieldKeyController);
                MethodInfo targetMethod = null;

                foreach (var method in fieldKeyControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "SetDashFlag")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
                            MelonLogger.Msg("[DashFlag] Found SetDashFlag(bool)");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(DashFlagPatches).GetMethod(nameof(SetDashFlag_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[DashFlag] Patched SetDashFlag successfully");
                }
                else
                {
                    MelonLogger.Warning("[DashFlag] Could not find SetDashFlag method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[DashFlag] Error patching SetDashFlag: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetDashFlag - caches the dashFlag value for use by GetDashFlag.
        /// Uses __0 parameter naming for IL2CPP compatibility.
        /// </summary>
        public static void SetDashFlag_Postfix(bool __0)
        {
            try
            {
                MoveStateHelper.SetCachedDashFlag(__0);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DashFlag] Error in SetDashFlag patch: {ex.Message}");
            }
        }
    }
}
