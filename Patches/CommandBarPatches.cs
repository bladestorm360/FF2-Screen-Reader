using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;

using ItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using ItemCommandContentView = Il2CppLast.UI.KeyInput.ItemCommandContentView;
using EquipmentCommandController = Il2CppLast.UI.KeyInput.EquipmentCommandController;
using EquipmentCommandView = Il2CppLast.UI.KeyInput.EquipmentCommandView;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Announces the INITIALLY-focused command when the item / equipment command bar opens (the field
    /// menu's open read is MainMenuPatches' FieldMenuReader).
    /// Purely additive: each controller's per-frame <c>UpdateController</c> is patched, but it only
    /// reads while the bar's arm flag is set (one read per open) and clears the flag ONLY after a
    /// successful read — so it polls harmlessly until the focused command's ID is set, then announces
    /// once. The command identity is read from the controller's ENUM/data (never on-screen Text), so
    /// it is immune to the pre-localization placeholder. Navigation is untouched: it keeps flowing
    /// through the generic cursor reader (NextIndex/PrevIndex); this fires only on open.
    /// </summary>
    public static class CommandBarPatches
    {
        private static bool isPatched = false;

        private static bool _itemArmed = false;
        private static bool _equipArmed = false;

        // Armed when a command bar (re)opens; cleared on leave / a successful read.
        public static void ArmItem() => _itemArmed = true;
        public static void ArmEquip() => _equipArmed = true;
        public static void ClearItem() => _itemArmed = false;
        public static void ClearEquip() => _equipArmed = false;
        public static void ClearAll() { _itemArmed = false; _equipArmed = false; }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatch(harmony, typeof(ItemCommandController), nameof(Item_UpdateController_Postfix));
                TryPatch(harmony, typeof(EquipmentCommandController), nameof(Equip_UpdateController_Postfix));
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CommandBar] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatch(HarmonyLib.Harmony harmony, Type type, string postfixName)
        {
            try
            {
                var target = AccessTools.Method(type, "UpdateController", Type.EmptyTypes);
                if (target != null)
                {
                    var postfix = AccessTools.Method(typeof(CommandBarPatches), postfixName);
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error($"[CommandBar] {type.Name}.UpdateController not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CommandBar] Error patching {type.Name}.UpdateController: {ex.Message}");
            }
        }

        // Item command bar: contentList[selectCursor.Index].Data.Id (ItemCommandId). The KeyInput
        // controller has no command-id cache (<CommandIdCash> exists only on the Touch variant).
        public static void Item_UpdateController_Postfix(object __instance)
        {
            if (!_itemArmed)
                return;
            try
            {
                IntPtr p = (__instance as ItemCommandController)?.Pointer ?? IntPtr.Zero;
                if (p == IntPtr.Zero)
                    return;
                IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ITEM_SELECT_CURSOR);
                IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ITEM_CONTENT_LIST);
                if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                    return;
                int index = new GameCursor(cursorPtr).Index;
                var contents = new Il2CppSystem.Collections.Generic.List<ItemCommandContentView>(listPtr);
                if (index < 0 || index >= contents.Count)
                    return;
                var data = contents[index]?.Data;
                if (data == null)
                    return;   // focus not set yet — stay armed, retry next frame
                string name = CommandBarReader.GetItemCommandName(data.Id);
                if (string.IsNullOrEmpty(name))
                    return;
                _itemArmed = false;
                CommandBarReader.Announce(name, index, contents.Count);
            }
            catch { }
        }

        // Equipment command bar: contents[selectCursor.Index].Data.Id (EquipmentCommandId).
        public static void Equip_UpdateController_Postfix(object __instance)
        {
            if (!_equipArmed)
                return;
            try
            {
                IntPtr p = (__instance as EquipmentCommandController)?.Pointer ?? IntPtr.Zero;
                if (p == IntPtr.Zero)
                    return;
                IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EQUIP_SELECT_CURSOR);
                IntPtr contentsPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EQUIP_CONTENTS);
                if (cursorPtr == IntPtr.Zero || contentsPtr == IntPtr.Zero)
                    return;
                int index = new GameCursor(cursorPtr).Index;
                var contents = new Il2CppSystem.Collections.Generic.List<EquipmentCommandView>(contentsPtr);
                if (index < 0 || index >= contents.Count)
                    return;
                var view = contents[index];
                if (view == null)
                    return;
                var data = view.Data;
                if (data == null)
                    return;
                string name = CommandBarReader.GetEquipmentCommandName(data.Id);
                if (string.IsNullOrEmpty(name))
                    return;
                _equipArmed = false;
                CommandBarReader.Announce(name, index, contents.Count);
            }
            catch { }
        }
    }
}
