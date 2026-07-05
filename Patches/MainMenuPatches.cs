using System;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Menus;

using MainMenuController_KeyInput = Il2CppLast.UI.KeyInput.MainMenuController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Manages the MAIN_MENU container state (controller routing) and arms the field command-bar
    /// open-read on field-menu open (CommandBarPatches.ArmField) so its UpdateController postfix
    /// announces the initially-focused command (Item / Magic / Status / …). Navigation is read by
    /// the generic cursor reader.
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
                    var prefix = AccessTools.Method(typeof(MainMenuPatches), nameof(Show_Prefix));
                    var postfix = AccessTools.Method(typeof(MainMenuPatches), nameof(Show_Postfix));
                    harmony.Patch(showMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
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

        /// <summary>
        /// Runs before Show() so the field command-bar open-read is armed. The armed
        /// UpdateController postfix (CommandBarPatches) then reads focusId once the menu has set it.
        /// Fires on open and on returning to the command bar from a submenu, so it re-announces.
        /// </summary>
        public static void Show_Prefix()
        {
            try { CommandBarPatches.ArmField(); }
            catch { }
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
                // Initial focus is announced by the armed UpdateController open-read (Show_Prefix →
                // CommandBarPatches.Field_UpdateController_Postfix); navigation by the generic reader.
                MenuStateRegistry.SetActive(MenuStateRegistry.MAIN_MENU, true);
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
                CommandBarPatches.ClearField();
            }
            catch { }
        }
    }
}
