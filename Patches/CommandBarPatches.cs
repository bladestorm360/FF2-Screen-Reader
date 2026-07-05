using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;

using MainMenuController = Il2CppLast.UI.KeyInput.MainMenuController;
using ItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using EquipmentCommandController = Il2CppLast.UI.KeyInput.EquipmentCommandController;
using EquipmentCommandView = Il2CppLast.UI.KeyInput.EquipmentCommandView;
using MenuCommandId = Il2CppLast.Defaine.MenuCommandId;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Announces the INITIALLY-focused command when the field / item / equipment command bar opens.
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

        private static bool _fieldArmed = false;
        private static bool _itemArmed = false;
        private static bool _equipArmed = false;

        // Armed when a command bar (re)opens; cleared on leave / a successful read.
        public static void ArmField() => _fieldArmed = true;
        public static void ArmItem() => _itemArmed = true;
        public static void ArmEquip() => _equipArmed = true;
        public static void ClearField() => _fieldArmed = false;
        public static void ClearItem() => _itemArmed = false;
        public static void ClearEquip() => _equipArmed = false;
        public static void ClearAll() { _fieldArmed = false; _itemArmed = false; _equipArmed = false; }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatch(harmony, typeof(MainMenuController), nameof(Field_UpdateController_Postfix));
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

        // Field menu: focusId (MenuCommandId) @ 0x90 on MainMenuController.
        public static void Field_UpdateController_Postfix(object __instance)
        {
            if (!_fieldArmed)
                return;
            try
            {
                IntPtr p = (__instance as MainMenuController)?.Pointer ?? IntPtr.Zero;
                if (p == IntPtr.Zero)
                    return;
                var id = (MenuCommandId)Marshal.ReadInt32(p, IL2CppOffsets.CommandBar.FIELD_FOCUS_ID);
                string name = CommandBarReader.GetMenuCommandName(id);
                if (string.IsNullOrEmpty(name))
                    return;   // focus not set yet — stay armed, retry next frame
                _fieldArmed = false;
                CommandBarReader.Announce(name);
            }
            catch { }
        }

        // Item command bar: the controller caches the focused command in CommandIdCash (public).
        public static void Item_UpdateController_Postfix(object __instance)
        {
            if (!_itemArmed)
                return;
            try
            {
                IntPtr p = (__instance as ItemCommandController)?.Pointer ?? IntPtr.Zero;
                if (p == IntPtr.Zero)
                    return;
                var id = (ItemCommandId)Marshal.ReadInt32(p, IL2CppOffsets.CommandBar.ITEM_COMMAND_ID_CACHE);
                string name = CommandBarReader.GetItemCommandName(id);
                if (string.IsNullOrEmpty(name))
                    return;
                _itemArmed = false;
                CommandBarReader.Announce(name);
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
