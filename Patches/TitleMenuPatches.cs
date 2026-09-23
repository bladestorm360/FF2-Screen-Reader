using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;

using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;
using TitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Announces the initially-focused item of the title menus on entry (FF1 TitleMenuPatches port).
    /// The main menu, Options list and Extras list are all rendered by TitleWindowController's
    /// commandController (TitleMenuCommandController @0x50, dump.cs:463207). Navigation already flows
    /// through the generic cursor reader, but the initial cursor placement never fires Cursor.NextIndex,
    /// so the per-state Init callbacks (real-bodied, unique RVAs) read the focus with the same reader:
    ///   InitSelect → main menu (first entry and every back-out), InitializeOption → Options list,
    ///   InitializeExtra → Extras list.
    /// </summary>
    public static class TitleMenuPatches
    {
        // TitleMenuCommandController.activeContents (List<TitleCommandContentView>) — the visible commands.
        private const int OFFSET_ACTIVE_CONTENTS = 0x28;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            foreach (var method in new[] { "InitSelect", "InitializeOption", "InitializeExtra" })
            {
                try
                {
                    var target = AccessTools.Method(typeof(TitleWindowController), method, Type.EmptyTypes);
                    if (target != null)
                        harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(TitleMenuPatches), nameof(Init_Postfix))));
                    else
                        MelonLogger.Error($"[Title] TitleWindowController.{method} not found");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Title] Error patching TitleWindowController.{method}: {ex.Message}");
                }
            }
        }

        public static void Init_Postfix(TitleWindowController __instance)
        {
            try
            {
                // The main / Options / Extras lists are TitleMenuCommandController lists, never a config
                // menu or the naming screen: drop those states so their generic-reader suppression can't
                // outlive them (e.g. backing out of the title Configuration into the Options list).
                if (ConfigMenuState.IsActive)
                {
                    ConfigMenuState.ResetState();
                    ConfigMenuPatches.CancelReannounce();
                }
                NewGameNamingState.Clear();

                if (__instance == null || __instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                    return;
                var cursor = __instance.commandController?.selectCursor;
                if (cursor == null) return;
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error reading initial title-menu focus: {ex.Message}");
            }
        }

        /// <summary>
        /// Visible title-command count for the "(X of Y)" suffix, or -1 when <paramref name="cursor"/> is
        /// not the title menu's cursor (inert on every other menu). Uses activeContents so hidden commands
        /// (e.g. Continue with no save) are excluded, matching what the cursor navigates.
        /// </summary>
        internal static int TryGetActiveCommandCount(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return -1;
                var cmd = UnityEngine.Object.FindObjectOfType<TitleMenuCommandController>();
                if (cmd == null || cmd.gameObject == null || !cmd.gameObject.activeInHierarchy) return -1;
                var titleCursor = cmd.selectCursor;
                if (titleCursor == null || titleCursor.Pointer != cursor.Pointer) return -1;
                IntPtr listPtr = Marshal.ReadIntPtr(cmd.Pointer, OFFSET_ACTIVE_CONTENTS);
                if (listPtr == IntPtr.Zero) return -1;
                return Marshal.ReadInt32(listPtr, 0x18); // List._size
            }
            catch { return -1; } // best-effort; -1 → no suffix
        }
    }
}
