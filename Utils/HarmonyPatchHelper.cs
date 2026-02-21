using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for applying common Harmony patches.
    /// Consolidates the repeated SetActive/SetNextState patching boilerplate across multiple files.
    /// </summary>
    internal static class HarmonyPatchHelper
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// Patches SetActive(bool) method with a postfix.
        /// Used to detect menu open/close events.
        /// </summary>
        public static bool PatchSetActive(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            string postfixName = "SetActive_Postfix", string logPrefix = null)
        {
            try
            {
                var method = controllerType.GetMethod("SetActive", PublicInstance, null, new[] { typeof(bool) }, null);
                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} SetActive method not found on {controllerType.Name}");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} {postfixName} method not found on {patchType.Name}");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Error($"{logPrefix} Failed to patch SetActive: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Patches SetNextState method with a postfix.
        /// Used to detect state transitions within menus.
        /// </summary>
        public static bool PatchSetNextState(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            string postfixName = "SetNextState_Postfix", string logPrefix = null)
        {
            try
            {
                MethodInfo setNextStateMethod = null;
                foreach (var method in controllerType.GetMethods(AllInstance))
                {
                    if (method.Name == "SetNextState")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            setNextStateMethod = method;
                            break;
                        }
                    }
                }

                if (setNextStateMethod == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} SetNextState method not found on {controllerType.Name}");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} {postfixName} method not found on {patchType.Name}");
                    return false;
                }

                harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Error($"{logPrefix} Failed to patch SetNextState: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Patches SelectContent method with a postfix.
        /// Common in list controllers for item selection.
        /// </summary>
        public static bool PatchSelectContent(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            Type[] paramTypes = null, string postfixName = "SelectContent_Postfix", string logPrefix = null)
        {
            try
            {
                paramTypes ??= new[] { typeof(int), typeof(bool) };

                var method = controllerType.GetMethod("SelectContent", PublicInstance, null, paramTypes, null);
                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} SelectContent method not found on {controllerType.Name}");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} {postfixName} method not found on {patchType.Name}");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Error($"{logPrefix} Failed to patch SelectContent: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generic method patcher for any method by name.
        /// </summary>
        public static bool PatchMethod(HarmonyLib.Harmony harmony, Type targetType, string methodName, Type patchType,
            string postfixName, Type[] paramTypes = null, string logPrefix = null)
        {
            try
            {
                MethodInfo method;
                if (paramTypes != null)
                {
                    method = targetType.GetMethod(methodName, AllInstance, null, paramTypes, null);
                }
                else
                {
                    method = targetType.GetMethod(methodName, AllInstance);
                }

                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} {methodName} method not found on {targetType.Name}");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Error($"{logPrefix} {postfixName} method not found on {patchType.Name}");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Error($"{logPrefix} Failed to patch {methodName}: {ex.Message}");
                return false;
            }
        }
    }
}
