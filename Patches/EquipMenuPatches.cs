using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.AnnouncementDeduplicator;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputEquipmentInfoWindowController = Il2CppLast.UI.KeyInput.EquipmentInfoWindowController;
using KeyInputEquipmentSelectWindowController = Il2CppLast.UI.KeyInput.EquipmentSelectWindowController;
using EquipSlotType = Il2CppLast.Defaine.EquipSlotType;
using EquipUtility = Il2CppLast.Systems.EquipUtility;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using KeyInputEquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using System.Reflection;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for equipment menu - prevents duplicate cursor announcements.
    /// Part of the Active State Pattern ported from FF3.
    /// </summary>
    public static class EquipMenuState
    {
        /// <summary>
        /// True when equipment menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.EQUIP_MENU);

        // State constants from dump.cs (KeyInput.EquipmentWindowController.State)
        private const int STATE_NONE = 0;     // Menu closed
        private const int STATE_COMMAND = 1;  // Command bar (Equip/Remove/etc.)
        private const int STATE_INFO = 2;     // Slot selection
        private const int STATE_SELECT = 3;   // Item selection


        /// <summary>
        /// Called when equipment menu activates (slot or item list focused).
        /// Clears other menu states to prevent conflicts.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.EQUIP_MENU);
            AnnouncementDeduplicator.Reset(CONTEXT_EQUIP_MENU);
        }

        /// <summary>
        /// Check if GenericCursor announcements should be suppressed.
        /// Validates state machine to auto-clear when backing to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            // Validate we're actually in a submenu, not command bar
            var windowController = GameObjectCache.GetOrRefresh<KeyInputEquipmentWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_EQUIP_WINDOW);
            if (state == STATE_COMMAND || state == STATE_NONE)
            {
                ClearState();
                return false;  // Don't suppress - let generic cursor handle command bar
            }
            return true;  // In submenu - suppress generic cursor
        }

        /// <summary>
        /// Clear state when menu closes or switching to another menu.
        /// </summary>
        public static void ClearState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.EQUIP_MENU);
            AnnouncementDeduplicator.Reset(CONTEXT_EQUIP_MENU);
        }

        /// <summary>
        /// Get localized slot name from EquipSlotType.
        /// </summary>
        public static string GetSlotName(EquipSlotType slot)
        {
            try
            {
                string messageId = EquipUtility.GetSlotMessageId(slot);
                if (!string.IsNullOrEmpty(messageId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(messageId);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return localizedName;
                    }
                }
            }
            catch
            {
                // Fall through to defaults
            }

            // Fallback to English slot names
            return slot switch
            {
                EquipSlotType.Slot1 => "Right Hand",
                EquipSlotType.Slot2 => "Left Hand",
                EquipSlotType.Slot3 => "Head",
                EquipSlotType.Slot4 => "Body",
                EquipSlotType.Slot5 => "Accessory",
                EquipSlotType.Slot6 => "Accessory 2",
                _ => $"Slot {(int)slot}"
            };
        }

        /// <summary>
        /// Reset state (for testing or scene changes). Alias for ClearState.
        /// </summary>
        public static void Reset()
        {
            ClearState();
        }
    }

    /// <summary>
    /// Patches for equipment menu announcements.
    /// Ported from FF3 screen reader.
    /// </summary>
    public static class EquipMenuPatches
    {
        /// <summary>
        /// Apply all equipment menu patches manually.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch EquipmentInfoWindowController.SelectContent for slot selection
                var selectContentSlotMethod = AccessTools.Method(
                    typeof(KeyInputEquipmentInfoWindowController),
                    "SelectContent",
                    new Type[] { typeof(GameCursor) }
                );
                if (selectContentSlotMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(EquipMenuPatches), nameof(EquipmentInfoWindowController_SelectContent_Postfix));
                    harmony.Patch(selectContentSlotMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch EquipmentSelectWindowController.SelectContent for item selection
                var selectContentItemMethod = AccessTools.Method(
                    typeof(KeyInputEquipmentSelectWindowController),
                    "SelectContent",
                    new Type[] { typeof(GameCursor), typeof(CustomScrollViewWithinRangeType) }
                );
                if (selectContentItemMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(EquipMenuPatches), nameof(EquipmentSelectWindowController_SelectContent_Postfix));
                    harmony.Patch(selectContentItemMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch EquipmentWindowController.SetNextState for state transition detection
                TryPatchSetNextState(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EquipMenu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches EquipmentWindowController.SetNextState for state transition detection.
        /// </summary>
        private static void TryPatchSetNextState(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(KeyInputEquipmentWindowController);

                MethodInfo setNextStateMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetNextState")
                    {
                        setNextStateMethod = method;
                        break;
                    }
                }

                if (setNextStateMethod != null)
                {
                    var postfix = typeof(EquipMenuPatches).GetMethod(nameof(SetNextState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Postfix for SetNextState - clears state when returning to command bar or closing menu.
        /// </summary>
        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                // STATE_NONE = 0 (menu closing), STATE_COMMAND = 1 (command bar)
                if ((state == 0 || state == 1) && EquipMenuState.IsActive)
                {
                    EquipMenuState.ClearState();
                }
            }
            catch { }
        }

        #region EquipmentInfoWindowController - Slot Selection

        public static void EquipmentInfoWindowController_SelectContent_Postfix(
            KeyInputEquipmentInfoWindowController __instance,
            GameCursor targetCursor)
        {
            try
            {
                if (targetCursor == null) return;

                // Mark equipment menu as active (suppresses generic cursor)
                EquipMenuState.SetActive();

                int index = targetCursor.Index;

                // Access contentList directly (IL2CppInterop exposes private fields)
                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    return;
                }

                if (index < 0 || index >= contentList.Count)
                {
                    return;
                }

                var contentView = contentList[index];
                if (contentView == null)
                {
                    return;
                }

                // Get slot name from partText
                string slotName = null;
                if (contentView.partText != null)
                {
                    slotName = contentView.partText.text;
                }

                // Fallback to localized slot name if partText is empty
                if (string.IsNullOrWhiteSpace(slotName))
                {
                    EquipSlotType slotType = contentView.Slot;
                    slotName = EquipMenuState.GetSlotName(slotType);
                }

                // Get equipped item from Data property
                string equippedItem = null;
                var itemData = contentView.Data;
                if (itemData != null)
                {
                    try
                    {
                        equippedItem = itemData.Name;

                        // Add parameter message (ATK +12, DEF +5, etc.)
                        string paramMsg = itemData.ParameterMessage;
                        if (!string.IsNullOrWhiteSpace(paramMsg))
                        {
                            equippedItem += ", " + paramMsg;
                        }
                    }
                    catch { }
                }

                // Build announcement
                string announcement = "";
                if (!string.IsNullOrWhiteSpace(slotName))
                {
                    announcement = slotName;
                }

                if (!string.IsNullOrWhiteSpace(equippedItem))
                {
                    if (!string.IsNullOrWhiteSpace(announcement))
                    {
                        announcement += ": " + equippedItem;
                    }
                    else
                    {
                        announcement = equippedItem;
                    }
                }
                else
                {
                    announcement += ": Empty";
                }

                if (string.IsNullOrWhiteSpace(announcement))
                {
                    return;
                }

                // Strip icon markup
                announcement = TextUtils.StripIconMarkup(announcement);

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_EQUIP_MENU, announcement))
                    return;

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch
            {
            }
        }

        #endregion

        #region EquipmentSelectWindowController - Item Selection

        public static void EquipmentSelectWindowController_SelectContent_Postfix(
            KeyInputEquipmentSelectWindowController __instance,
            GameCursor targetCursor)
        {
            try
            {
                if (targetCursor == null) return;

                // Mark equipment menu as active (suppresses generic cursor)
                EquipMenuState.SetActive();

                int index = targetCursor.Index;

                // Access ContentDataList (public property)
                var contentDataList = __instance.ContentDataList;
                if (contentDataList == null || contentDataList.Count == 0)
                {
                    return; // Empty list is normal for empty slots
                }

                if (index < 0 || index >= contentDataList.Count)
                {
                    return;
                }

                var itemData = contentDataList[index];
                if (itemData == null)
                {
                    return;
                }

                // Get item name - handle empty/remove entries
                string itemName = itemData.Name;
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    // This might be a "Remove" or empty entry
                    itemName = "Remove";
                }

                // Strip icon markup from name
                itemName = TextUtils.StripIconMarkup(itemName);

                // Build announcement with item details
                string announcement = itemName;

                // Add parameter info (ATK +15, DEF +8, etc.)
                try
                {
                    string paramMessage = itemData.ParameterMessage;
                    if (!string.IsNullOrWhiteSpace(paramMessage))
                    {
                        paramMessage = TextUtils.StripIconMarkup(paramMessage);
                        announcement += $", {paramMessage}";
                    }
                }
                catch { }

                // Add description
                try
                {
                    string description = itemData.Description;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        announcement += $", {description}";
                    }
                }
                catch { }

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_EQUIP_MENU, announcement))
                    return;

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch
            {
            }
        }

        #endregion
    }
}
