using System;
using System.Collections;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;

using ItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using ItemCommandContentView = Il2CppLast.UI.KeyInput.ItemCommandContentView;
using EquipmentCommandController = Il2CppLast.UI.KeyInput.EquipmentCommandController;
using EquipmentCommandView = Il2CppLast.UI.KeyInput.EquipmentCommandView;
using KeyInputItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using KeyInputEquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Announces the focused command when the item / equipment command bar is (re)entered (the field
    /// menu's open read is MainMenuPatches' FieldMenuReader). Event-driven (CLAUDE.md rule 3): the
    /// window controllers' command-state entries arm a deferred read —
    ///   KeyInput ItemWindowController.CommandSelectInit (unique RVA 0x7CDEB0),
    ///   KeyInput EquipmentWindowController.CommandInit (unique RVA 0x5217A0) —
    /// both state-machine Inits, so they fire on menu open and on every return from a list. The read
    /// runs a frame later and retries once per frame (capped) until the focused command's id is set;
    /// it gives up if the window left its command state. The command identity is read from the
    /// controller's ENUM/data (never on-screen Text), so it is immune to the pre-localization
    /// placeholder. Navigation is untouched: it flows through the generic cursor reader.
    /// (Replaces the per-frame ItemCommandController/EquipmentCommandController.UpdateController
    /// postfixes, which were armed from SetNextState — a folded body with no direct callers, inlined
    /// at every call site, so the arm never came from the real state change.)
    /// </summary>
    public static class CommandBarPatches
    {
        private static bool isPatched = false;

        // ItemWindowController / EquipmentWindowController.commandController (KeyInput, dump.cs:452355 / 448736)
        private const int WINDOW_COMMAND_CONTROLLER = 0x38;
        private const int MAX_RETRY_FRAMES = 30;

        // Bumped on every arm and by ClearAll; an older deferred read exits when superseded.
        private static int _itemGen;
        private static int _equipGen;

        /// <summary>Cancels any pending command-bar read (map transition leak safety).</summary>
        public static void ClearAll() { _itemGen++; _equipGen++; }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatch(harmony, typeof(KeyInputItemWindowController), "CommandSelectInit", nameof(ItemCommandSelectInit_Postfix));
                TryPatch(harmony, typeof(KeyInputEquipmentWindowController), "CommandInit", nameof(EquipCommandInit_Postfix));
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CommandBar] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatch(HarmonyLib.Harmony harmony, Type type, string method, string postfixName)
        {
            try
            {
                var target = AccessTools.Method(type, method, Type.EmptyTypes);
                if (target != null)
                {
                    var postfix = AccessTools.Method(typeof(CommandBarPatches), postfixName);
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error($"[CommandBar] {type.Name}.{method} not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CommandBar] Error patching {type.Name}.{method}: {ex.Message}");
            }
        }

        public static void ItemCommandSelectInit_Postfix(KeyInputItemWindowController __instance)
        {
            try
            {
                if (__instance != null)
                    CoroutineManager.StartManaged(DeferredItemRead(__instance, ++_itemGen));
            }
            catch { }
        }

        public static void EquipCommandInit_Postfix(KeyInputEquipmentWindowController __instance)
        {
            try
            {
                if (__instance != null)
                    CoroutineManager.StartManaged(DeferredEquipRead(__instance, ++_equipGen));
            }
            catch { }
        }

        // Result of one read attempt.
        private enum ReadResult { Spoken, NotReady, Abandon }

        // yield stays outside the try (yield-in-try-with-catch is illegal).
        private static IEnumerator DeferredItemRead(KeyInputItemWindowController window, int gen)
        {
            for (int frame = 0; frame < MAX_RETRY_FRAMES; frame++)
            {
                yield return null;
                if (gen != _itemGen) yield break;

                ReadResult result;
                try { result = TryReadItemCommand(window); }
                catch { result = ReadResult.NotReady; }
                if (result != ReadResult.NotReady) yield break;
            }
        }

        private static IEnumerator DeferredEquipRead(KeyInputEquipmentWindowController window, int gen)
        {
            for (int frame = 0; frame < MAX_RETRY_FRAMES; frame++)
            {
                yield return null;
                if (gen != _equipGen) yield break;

                ReadResult result;
                try { result = TryReadEquipCommand(window); }
                catch { result = ReadResult.NotReady; }
                if (result != ReadResult.NotReady) yield break;
            }
        }

        // Item command bar: contentList[selectCursor.Index].Data.Id (ItemCommandId). The KeyInput
        // controller has no command-id cache (<CommandIdCash> exists only on the Touch variant).
        private static ReadResult TryReadItemCommand(KeyInputItemWindowController window)
        {
            if (window == null || window.gameObject == null || !window.gameObject.activeInHierarchy || !IsMenuOpen())
                return ReadResult.NotReady;
            int state = StateReaderHelper.ReadStateTag(window.Pointer, StateReaderHelper.OFFSET_ITEM_WINDOW);
            if (state != IL2CppOffsets.Item.STATE_COMMAND_SELECT)
                return state < 0 ? ReadResult.NotReady : ReadResult.Abandon;   // left the command bar

            IntPtr p = Marshal.ReadIntPtr(window.Pointer, WINDOW_COMMAND_CONTROLLER);
            if (p == IntPtr.Zero)
                return ReadResult.NotReady;
            IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ITEM_SELECT_CURSOR);
            IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ITEM_CONTENT_LIST);
            if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                return ReadResult.NotReady;
            int index = new GameCursor(cursorPtr).Index;
            var contents = new Il2CppSystem.Collections.Generic.List<ItemCommandContentView>(listPtr);
            if (index < 0 || index >= contents.Count)
                return ReadResult.NotReady;
            var data = contents[index]?.Data;
            if (data == null)
                return ReadResult.NotReady;   // focus not set yet — retry next frame
            string name = CommandBarReader.GetItemCommandName(data.Id);
            if (string.IsNullOrEmpty(name))
                return ReadResult.NotReady;
            CommandBarReader.Announce(name, index, contents.Count);
            return ReadResult.Spoken;
        }

        // Equipment command bar: contents[selectCursor.Index].Data.Id (EquipmentCommandId).
        private static ReadResult TryReadEquipCommand(KeyInputEquipmentWindowController window)
        {
            if (window == null || window.gameObject == null || !window.gameObject.activeInHierarchy || !IsMenuOpen())
                return ReadResult.NotReady;
            int state = StateReaderHelper.ReadStateTag(window.Pointer, StateReaderHelper.OFFSET_EQUIP_WINDOW);
            if (state != IL2CppOffsets.Equipment.STATE_COMMAND)
                return state < 0 ? ReadResult.NotReady : ReadResult.Abandon;   // left the command bar

            IntPtr p = Marshal.ReadIntPtr(window.Pointer, WINDOW_COMMAND_CONTROLLER);
            if (p == IntPtr.Zero)
                return ReadResult.NotReady;
            IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EQUIP_SELECT_CURSOR);
            IntPtr contentsPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EQUIP_CONTENTS);
            if (cursorPtr == IntPtr.Zero || contentsPtr == IntPtr.Zero)
                return ReadResult.NotReady;
            int index = new GameCursor(cursorPtr).Index;
            var contents = new Il2CppSystem.Collections.Generic.List<EquipmentCommandView>(contentsPtr);
            if (index < 0 || index >= contents.Count)
                return ReadResult.NotReady;
            var data = contents[index]?.Data;
            if (data == null)
                return ReadResult.NotReady;
            string name = CommandBarReader.GetEquipmentCommandName(data.Id);
            if (string.IsNullOrEmpty(name))
                return ReadResult.NotReady;
            CommandBarReader.Announce(name, index, contents.Count);
            return ReadResult.Spoken;
        }

        // MenuManager.IsOpen is false during a map/asset load's scene-construction flurry.
        private static bool IsMenuOpen()
        {
            try { var mm = Il2CppLast.UI.MenuManager.Instance; return mm != null && mm.IsOpen; }
            catch { return false; }
        }
    }
}
