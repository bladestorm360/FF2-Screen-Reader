using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Menus;

using MainMenuController_KeyInput = Il2CppLast.UI.KeyInput.MainMenuController;
using CommandMenuController = Il2CppLast.UI.CommandMenuController;
using MenuManager = Il2CppLast.UI.MenuManager;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Manages the MAIN_MENU container state (controller routing) and announces the field menu's
    /// focused command on open and on every back-out to the command bar (FieldMenuReader, FF1 port).
    /// Navigation is read by the generic cursor reader.
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

                // InitNone: the command-select state, re-entered on cancel from every sub-menu and the
                // quicksave popup (dump.cs:443841, real-bodied) → re-announce the focused command.
                var initNoneMethod = AccessTools.Method(typeof(MainMenuController_KeyInput), "InitNone");
                if (initNoneMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MainMenuPatches), nameof(InitNone_Postfix));
                    harmony.Patch(initNoneMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[MainMenu] Could not find InitNone method");
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

                FieldMenuReader.AnnounceFocus(__instance);
            }
            catch { }
        }

        public static void InitNone_Postfix(MainMenuController_KeyInput __instance)
        {
            FieldMenuReader.AnnounceFocus(__instance);
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
    }

    /// <summary>
    /// Announces the field menu's FOCUSED command (FF1 FieldMenuReader port). Reads the live
    /// Last.UI.CommandMenuController.selectCursor (MainMenuController.commandMenuController, dump.cs
    /// 390885: contents 0x30, selectCursor 0x38) through the same reader navigation uses. Never reads
    /// MainMenuController.focusId — that is the SELECTED command, not the focused one. Gated on
    /// MenuManager.IsOpen so it never reads during a map/asset load's scene-construction flurry.
    /// </summary>
    internal static class FieldMenuReader
    {
        // Bumped on every AnnounceFocus; a delayed read aborts when a newer call superseded it, so the
        // Show + InitNone pair on open collapses to a single announce.
        private static int _gen;

        internal static void AnnounceFocus(MainMenuController_KeyInput inst)
        {
            if (inst == null) return;
            int gen = ++_gen;
            try { CoroutineManager.StartManaged(DelayedAnnounce(inst, gen)); }
            catch (Exception ex) { MelonLogger.Warning($"[MainMenu] Error scheduling focus read: {ex.Message}"); }
        }

        // yield stays outside the try (yield-in-try-with-catch is illegal).
        private static IEnumerator DelayedAnnounce(MainMenuController_KeyInput inst, int gen)
        {
            yield return null; // let the cursor settle AND MenuManager.IsOpen flip true

            if (gen != _gen) yield break;

            GameCursor cursor = null;
            try
            {
                if (inst != null && inst.gameObject != null && inst.gameObject.activeInHierarchy
                    && IsFieldMenuOpen())
                {
                    var cmd = inst.commandMenuController;
                    if (cmd != null) cursor = cmd.selectCursor;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MainMenu] Error reading field-menu focus: {ex.Message}");
            }

            if (cursor != null)
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
        }

        private static bool IsFieldMenuOpen()
        {
            try { var mm = MenuManager.Instance; return mm != null && mm.IsOpen; }
            catch { return false; } // MenuManager not constructed yet during early load
        }

        /// <summary>
        /// Field-command count for the "(X of Y)" suffix, or -1 when <paramref name="cursor"/> is not the
        /// field menu's cursor (inert on every other menu). The field menu keeps its commands in a C#
        /// list, not a Content transform, so the generic reader can't count them itself.
        /// </summary>
        internal static int TryGetFieldCommandCount(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return -1;
                var cmd = UnityEngine.Object.FindObjectOfType<CommandMenuController>();
                if (cmd == null || cmd.gameObject == null || !cmd.gameObject.activeInHierarchy) return -1;
                var fieldCursor = cmd.selectCursor;
                if (fieldCursor == null || fieldCursor.Pointer != cursor.Pointer) return -1;
                var contents = cmd.contents;
                return contents != null ? contents.Count : -1;
            }
            catch { return -1; } // best-effort; -1 → no suffix
        }
    }
}
