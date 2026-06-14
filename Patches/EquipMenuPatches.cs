using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP types
using KeyInputEquipmentInfoWindowController = Il2CppLast.UI.KeyInput.EquipmentInfoWindowController;
using KeyInputEquipmentSelectWindowController = Il2CppLast.UI.KeyInput.EquipmentSelectWindowController;
using EquipSlotType = Il2CppLast.Defaine.EquipSlotType;
using EquipUtility = Il2CppLast.Systems.EquipUtility;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using KeyInputEquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using KeyInputEquipmentDescriptionWindowController = Il2CppLast.UI.KeyInput.EquipmentDescriptionWindowController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Announces equipment detail when the I key / right-stick-up is pressed. Reads the live
    /// UI panel text (FF1 pattern): the game renders the active panel (stats OR description)
    /// into EquipmentDescriptionWindowView.descriptionText, so this reads the full stats panel
    /// and is inherently panel-sensitive — no master-data lookups.
    /// </summary>
    public static class EquipDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                if (!EquipMenuState.IsActive)
                    return;

                string announcement = GetActivePanelFromUI();
                if (string.IsNullOrWhiteSpace(announcement))
                    announcement = T("No description available");

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Equipment] Error announcing details: {ex.Message}");
            }
        }

        /// <summary>
        /// EquipmentDescriptionWindowController -> view (0x20) -> descriptionText (0x18) -> .text
        /// </summary>
        private static string GetActivePanelFromUI()
        {
            try
            {
                var descController = UnityEngine.Object.FindObjectOfType<KeyInputEquipmentDescriptionWindowController>();
                if (descController == null) return null;

                IntPtr ctrlPtr = descController.Pointer;
                if (ctrlPtr == IntPtr.Zero) return null;

                IntPtr viewPtr = Marshal.ReadIntPtr(ctrlPtr + IL2CppOffsets.Equipment.DescriptionView);
                if (viewPtr == IntPtr.Zero) return null;

                IntPtr textPtr = Marshal.ReadIntPtr(viewPtr + IL2CppOffsets.Equipment.DescriptionText);
                if (textPtr == IntPtr.Zero) return null;

                var text = new UnityEngine.UI.Text(textPtr);
                string raw = text?.text;
                return string.IsNullOrWhiteSpace(raw) ? null : TextUtils.StripIconMarkup(raw).Trim();
            }
            catch
            {
                return null;
            }
        }
    }
    /// <summary>
    /// State tracker for equipment menu - prevents duplicate cursor announcements.
    /// Part of the Active State Pattern ported from FF3.
    /// </summary>
    public static class EquipMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.EQUIP_MENU);

        static EquipMenuState()
        {
            _helper.RegisterResetHandler();
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        /// <summary>
        /// Check if GenericCursor announcements should be suppressed.
        /// Validates state machine to auto-clear when backing to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            var windowController = GameObjectCache.GetOrRefresh<KeyInputEquipmentWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_EQUIP_WINDOW);
            if (state == IL2CppOffsets.Equipment.STATE_COMMAND || state == IL2CppOffsets.Equipment.STATE_NONE)
            {
                ClearState();
                return false;
            }
            return true;
        }

        public static void ClearState() => _helper.IsActive = false;

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
            }

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

        public static void Reset() => ClearState();
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

                        // Append parameter message (ATK +12, DEF +5, etc.) only when
                        // AutoDetail is on; otherwise the slot panel reads just the name
                        // and stats come from the I key / right stick up.
                        if (PreferencesManager.AutoDetailEnabled)
                        {
                            string paramMsg = itemData.ParameterMessage;
                            if (!string.IsNullOrWhiteSpace(paramMsg))
                            {
                                equippedItem += ", " + paramMsg;
                            }
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

                // Base is the item name; the stat change ("ATK +15") and description are
                // the detail — gated behind AutoDetail and reachable via the I key.
                string announcement = itemName;

                string detail = null;
                try
                {
                    string paramMessage = itemData.ParameterMessage;
                    if (!string.IsNullOrWhiteSpace(paramMessage))
                        detail = TextUtils.StripIconMarkup(paramMessage);
                }
                catch { }
                try
                {
                    string description = itemData.Description;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        detail = string.IsNullOrWhiteSpace(detail) ? description : $"{detail}, {description}";
                    }
                }
                catch { }

                MenuDetailCache.Set(detail);

                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(detail))
                    announcement += $": {detail}";

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch
            {
            }
        }

        #endregion
    }
}
