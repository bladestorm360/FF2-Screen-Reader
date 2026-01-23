using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.AnnouncementDeduplicator;

// Type aliases for IL2CPP types
using BattleItemInfomationController = Il2CppLast.UI.KeyInput.BattleItemInfomationController;
using BattleItemInfomationContentController = Il2CppLast.UI.KeyInput.BattleItemInfomationContentController;
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle item menu.
    /// </summary>
    public static class BattleItemPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Item] Applying battle item menu patches...");

                var controllerType = typeof(BattleItemInfomationController);

                MethodInfo selectContentMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            selectContentMethod = m;
                            MelonLogger.Msg($"[Battle Item] Found SelectContent with Cursor parameter");
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleItemSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Item] Patched SelectContent successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Item] SelectContent method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error applying patches: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// State tracking for battle item menu.
    /// </summary>
    public static class BattleItemMenuState
    {
        /// <summary>
        /// True when battle item menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_ITEM);

        /// <summary>
        /// Sets the battle item menu as active, clearing other menu states.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_ITEM);
        }

        // State machine offsets for BattleCommandSelectController
        private const int OFFSET_STATE_MACHINE = 0x48;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        private const int STATE_NORMAL = 1;
        private const int STATE_EXTRA = 2;

        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                var itemController = GameObjectCache.GetOrRefresh<BattleItemInfomationController>();
                if (itemController == null || !itemController.gameObject.activeInHierarchy)
                {
                    Reset();
                    return false;
                }

                var cmdController = GameObjectCache.GetOrRefresh<BattleCommandSelectController>();
                if (cmdController != null && cmdController.gameObject.activeInHierarchy)
                {
                    int state = GetCommandState(cmdController);
                    if (state == STATE_NORMAL || state == STATE_EXTRA)
                    {
                        Reset();
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                Reset();
                return false;
            }
        }

        private static int GetCommandState(BattleCommandSelectController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return -1;

                unsafe
                {
                    IntPtr smPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (smPtr == IntPtr.Zero) return -1;

                    IntPtr currentPtr = *(IntPtr*)((byte*)smPtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentPtr == IntPtr.Zero) return -1;

                    return *(int*)((byte*)currentPtr.ToPointer() + OFFSET_STATE_TAG);
                }
            }
            catch { return -1; }
        }

        public static void Reset()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.BATTLE_ITEM);
            AnnouncementDeduplicator.Reset(AnnouncementDeduplicator.CONTEXT_BATTLE_ITEM);
        }
    }

    /// <summary>
    /// Patch for battle item selection.
    /// </summary>
    public static class BattleItemSelectContent_Patch
    {
        public static void Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as BattleItemInfomationController;
                if (controller == null)
                    return;

                int cursorIndex = targetCursor.Index;
                MelonLogger.Msg($"[Battle Item] SelectContent called, cursor index: {cursorIndex}");

                string announcement = TryGetItemFromContentList(controller, cursorIndex);

                if (string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg("[Battle Item] Could not get item from content list");
                    return;
                }

                if (!ShouldAnnounce(CONTEXT_BATTLE_ITEM, announcement))
                    return;

                BattleItemMenuState.SetActive();

                MelonLogger.Msg($"[Battle Item] Announcing: {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error in SelectContent patch: {ex.Message}");
            }
        }

        // Offset for displayDataList in KeyInput.BattleItemInfomationController (from dump.cs line 432825)
        // This is List<ItemListContentData> which directly contains the data we need
        private const int OFFSET_DISPLAY_DATA_LIST = 0xE0;

        private static string TryGetItemFromContentList(BattleItemInfomationController controller, int cursorIndex)
        {
            try
            {
                // Access displayDataList via pointer offset (List<ItemListContentData> at 0xE0)
                var displayDataList = GetDisplayDataList(controller);
                if (displayDataList != null && displayDataList.Count > 0)
                {
                    MelonLogger.Msg($"[Battle Item] displayDataList found with {displayDataList.Count} items, cursor index: {cursorIndex}");

                    if (cursorIndex >= 0 && cursorIndex < displayDataList.Count)
                    {
                        var data = displayDataList[cursorIndex];
                        if (data != null)
                        {
                            return FormatItemAnnouncement(data);
                        }
                    }
                }
                else
                {
                    MelonLogger.Msg("[Battle Item] displayDataList is null or empty, trying fallback...");

                    // Fallback: search scene for content controllers with IsFocus
                    var allContentControllers = UnityEngine.Object.FindObjectsOfType<BattleItemInfomationContentController>();
                    if (allContentControllers != null && allContentControllers.Length > 0)
                    {
                        foreach (var cc in allContentControllers)
                        {
                            if (cc == null || !cc.gameObject.activeInHierarchy)
                                continue;

                            var data = cc.Data;
                            if (data != null && data.IsFocus)
                            {
                                return FormatItemAnnouncement(data);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error getting item from content list: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the displayDataList from the controller using pointer offset.
        /// KeyInput.BattleItemInfomationController has displayDataList at 0xE0.
        /// </summary>
        private static Il2CppSystem.Collections.Generic.List<ItemListContentData> GetDisplayDataList(BattleItemInfomationController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // Read displayDataList pointer at offset 0xE0
                    IntPtr listPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_DISPLAY_DATA_LIST);
                    if (listPtr == IntPtr.Zero)
                        return null;

                    // Convert to managed List<ItemListContentData>
                    return new Il2CppSystem.Collections.Generic.List<ItemListContentData>(listPtr);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error accessing displayDataList: {ex.Message}");
                return null;
            }
        }

        private static string FormatItemAnnouncement(ItemListContentData data)
        {
            try
            {
                string name = data.Name;
                if (string.IsNullOrEmpty(name))
                    return null;

                name = TextUtils.StripIconMarkup(name);
                if (string.IsNullOrEmpty(name))
                    return null;

                string announcement = name;

                try
                {
                    string description = data.Description;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            announcement += ": " + description;
                        }
                    }
                }
                catch { }

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error formatting announcement: {ex.Message}");
                return null;
            }
        }
    }
}
