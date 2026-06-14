using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

using MainMenuController_KeyInput = Il2CppLast.UI.KeyInput.MainMenuController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Announces the default-focused command (Item / Magic / Status / Equip / Save…)
    /// when the main field menu opens. Without this, the menu's initial focus is silent —
    /// the user only hears subsequent navigation via CommandMenuController.SetFocus.
    /// </summary>
    public static class MainMenuPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                var showMethod = AccessTools.Method(
                    typeof(MainMenuController_KeyInput),
                    "Show",
                    new Type[] { typeof(bool) });

                if (showMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MainMenuPatches), nameof(Show_Postfix));
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[MainMenu] Could not find Show method");
                }

                // Patch Close() to clear the MAIN_MENU container flag so field controls resume.
                var closeMethod = AccessTools.Method(typeof(MainMenuController_KeyInput), "Close");
                if (closeMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MainMenuPatches), nameof(Close_Postfix));
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[MainMenu] Could not find Close method");
                }

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MainMenu] Error applying patches: {ex.Message}");
            }
        }

        public static void Show_Postfix(MainMenuController_KeyInput __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Mark the pause menu active so the controller router stops treating it as
                // field (otherwise right-stick scans entities and the D-pad is consumed for
                // waypoints inside the menu). Re-asserted here because Show re-fires when
                // returning from a submenu to the command bar. Preserved across submenu
                // switches by MenuStateRegistry.SetActiveExclusive; cleared by Close_Postfix.
                MenuStateRegistry.SetActive(MenuStateRegistry.MAIN_MENU, true);

                CoroutineManager.StartManaged(AnnounceInitialFocus(__instance));
            }
            catch { }
        }

        /// <summary>
        /// Clears the MAIN_MENU container flag when the field/pause menu closes so field
        /// controls (entity/waypoint cycling) resume. ControllerRouter's per-frame
        /// revalidation backstops any submenu flag that might leak past Close.
        /// </summary>
        public static void Close_Postfix()
        {
            try
            {
                MenuStateRegistry.SetActive(MenuStateRegistry.MAIN_MENU, false);
            }
            catch { }
        }

        private static IEnumerator AnnounceInitialFocus(MainMenuController_KeyInput controller)
        {
            // Yield a few frames so focusId is settled after Show() completes.
            yield return null;
            yield return null;
            yield return null;

            string commandName = null;
            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                int focusId = ReadFocusId(controller);
                commandName = GetCommandName(focusId);
            }
            catch { }

            if (!string.IsNullOrEmpty(commandName))
                FFII_ScreenReaderMod.SpeakText(commandName, interrupt: true);
        }

        private static unsafe int ReadFocusId(MainMenuController_KeyInput controller)
        {
            IntPtr ptr = controller.Pointer;
            if (ptr == IntPtr.Zero)
                return 0;

            return *(int*)((byte*)ptr.ToPointer() + IL2CppOffsets.MainMenu.OFFSET_FOCUS_ID);
        }

        /// <summary>
        /// MenuCommandId values from dump.cs:303134.
        /// Strings are wrapped by T() at speak time via SpeakText / ModTextTranslator.
        /// </summary>
        private static string GetCommandName(int commandId)
        {
            switch (commandId)
            {
                case 1: return T("Item");
                case 2: return T("Magic");
                case 3: return T("Equipment");
                case 4: return T("Status");
                case 5: return T("Sort");
                case 6: return T("Words");
                case 7: return T("Config");
                case 8: return T("Interruption");
                case 9: return T("Save");
                case 10: return T("Back");
                case 11: return T("Job");
                case 12: return T("Ability");
                case 13: return T("Load");
                default: return null;
            }
        }
    }
}
