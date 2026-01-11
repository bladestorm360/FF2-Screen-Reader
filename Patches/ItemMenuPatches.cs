using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputItemListController = Il2CppLast.UI.KeyInput.ItemListController;
using KeyInputItemUseController = Il2CppLast.UI.KeyInput.ItemUseController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using Condition = Il2CppLast.Data.Master.Condition;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;
using Corps = Il2CppLast.Data.User.Corps;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using KeyInputItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using KeyInputItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Helper for item menu announcements.
    /// </summary>
    public static class ItemMenuState
    {
        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        /// <summary>
        /// True when item list or item target selection is active.
        /// Used to suppress generic cursor navigation announcements.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        /// <summary>
        /// Stores the currently selected item data for 'I' key lookup.
        /// </summary>
        public static ItemListContentData LastSelectedItem { get; set; } = null;

        // State machine offsets (from dump.cs)
        // KeyInput.ItemWindowController has stateMachine at offset 0x70
        private const int OFFSET_STATE_MACHINE = 0x70;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // ItemWindowController.State values
        private const int STATE_NONE = 0;
        private const int STATE_COMMAND_SELECT = 1;  // Command bar (Use/Key Items/Sort)
        private const int STATE_USE_SELECT = 2;       // Item list for Use
        private const int STATE_IMPORTANT_SELECT = 3; // Key Items list
        private const int STATE_ORGANIZE_SELECT = 4;  // Sort list
        private const int STATE_TARGET_SELECT = 5;    // Target selection

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Uses state machine to determine if we're in item list vs command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            try
            {
                // Check the ItemWindowController's state machine
                var windowController = UnityEngine.Object.FindObjectOfType<KeyInputItemWindowController>();
                if (windowController != null)
                {
                    int currentState = GetCurrentState(windowController);

                    // If we're in CommandSelect state, don't suppress - let MenuTextDiscovery handle it
                    if (currentState == STATE_COMMAND_SELECT || currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }

                    // We're in an item list state - suppress
                    if (currentState == STATE_USE_SELECT ||
                        currentState == STATE_IMPORTANT_SELECT ||
                        currentState == STATE_ORGANIZE_SELECT ||
                        currentState == STATE_TARGET_SELECT)
                    {
                        return true;
                    }
                }
            }
            catch { }

            // Fallback: clear state if we can't determine
            ClearState();
            return false;
        }

        /// <summary>
        /// Reads the current state from ItemWindowController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        private static int GetCurrentState(KeyInputItemWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read stateMachine pointer at offset 0x70
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Clears item menu state when menu is closed.
        /// </summary>
        public static void ClearState()
        {
            IsActive = false;
            LastSelectedItem = null;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = UnityEngine.Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Gets the row (Front/Back) for a character.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    return null;
                }

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                {
                    return null;
                }

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null)
                    {
                        if (corps.CharacterId == characterId)
                        {
                            string row = corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                            return row;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error getting character row: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name for an ItemCommandId.
        /// </summary>
        public static string GetItemCommandName(ItemCommandId commandId)
        {
            switch (commandId)
            {
                case ItemCommandId.Use:
                    return GetLocalizedCommand("$menu_item_use") ?? "Use";
                case ItemCommandId.Organize:
                    return GetLocalizedCommand("$menu_item_organize") ?? "Sort";
                case ItemCommandId.Important:
                    return GetLocalizedCommand("$menu_item_important") ?? "Key Items";
                default:
                    return null;
            }
        }

        private static string GetLocalizedCommand(string mesId)
        {
            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string text = messageManager.GetMessage(mesId, false);
                    if (!string.IsNullOrWhiteSpace(text))
                        return TextUtils.StripIconMarkup(text);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets a localized condition/status effect name from a Condition object.
        /// </summary>
        public static string GetConditionName(Condition condition)
        {
            if (condition == null)
                return null;

            try
            {
                string mesId = condition.MesIdName;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return localizedName;
                    }
                }
            }
            catch
            {
                // Fall through
            }

            return null;
        }
    }

    /// <summary>
    /// Manual patch application for item menu.
    /// </summary>
    public static class ItemMenuPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                MelonLogger.Msg("[Item Menu] Applying item menu patches...");

                // Patch ItemListController.SelectContent for item list navigation
                var itemListSelectContent = AccessTools.Method(
                    typeof(KeyInputItemListController),
                    "SelectContent",
                    new Type[] {
                        typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>),
                        typeof(int),
                        typeof(GameCursor),
                        typeof(CustomScrollViewWithinRangeType)
                    });

                if (itemListSelectContent != null)
                {
                    var postfix = AccessTools.Method(typeof(ItemMenuPatches), nameof(ItemListController_SelectContent_Postfix));
                    harmony.Patch(itemListSelectContent, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Item Menu] Patched ItemListController.SelectContent");
                }

                // Patch ItemUseController.SelectContent for character target selection
                var itemUseSelectContent = AccessTools.Method(
                    typeof(KeyInputItemUseController),
                    "SelectContent",
                    new Type[] {
                        typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController>),
                        typeof(GameCursor)
                    });

                if (itemUseSelectContent != null)
                {
                    var postfix = AccessTools.Method(typeof(ItemMenuPatches), nameof(ItemUseController_SelectContent_Postfix));
                    harmony.Patch(itemUseSelectContent, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Item Menu] Patched ItemUseController.SelectContent");
                }

                isPatched = true;
                MelonLogger.Msg("[Item Menu] Item menu patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Item Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for item list selection.
        /// Announces item name: description when navigating items in the menu.
        /// </summary>
        public static void ItemListController_SelectContent_Postfix(
            KeyInputItemListController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData> targets,
            int index,
            GameCursor targetCursor)
        {
            try
            {
                if (targets == null)
                    return;

                // Convert IEnumerable to List for indexed access
                var targetList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(targets);
                if (targetList == null || targetList.Count == 0)
                    return;

                if (index < 0 || index >= targetList.Count)
                    return;

                var itemData = targetList[index];
                if (itemData == null)
                    return;

                // Store selected item for 'I' key lookup
                ItemMenuState.LastSelectedItem = itemData;

                string itemName = itemData.Name;
                if (string.IsNullOrEmpty(itemName))
                    return;

                // Strip icon markup from name
                itemName = TextUtils.StripIconMarkup(itemName);

                if (string.IsNullOrEmpty(itemName))
                    return;

                // Build announcement: "Item Name: Description"
                string announcement = itemName;

                // Add description if available
                string description = itemData.Description;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    description = TextUtils.StripIconMarkup(description);

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        announcement += ": " + description;
                    }
                }

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                FFII_ScreenReaderMod.ClearOtherMenuStates("Item");
                ItemMenuState.IsActive = true;

                MelonLogger.Msg($"[Item Menu] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemListController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for character target selection when using an item.
        /// Announces character name, HP, MP, and status effects.
        /// FF2 uses MP (unlike FF3 which uses spell charges).
        /// </summary>
        public static void ItemUseController_SelectContent_Postfix(
            KeyInputItemUseController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> targetContents,
            GameCursor targetCursor)
        {
            try
            {
                if (targetCursor == null || targetContents == null)
                    return;

                int index = targetCursor.Index;

                // Convert to list for indexed access
                var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(targetContents);
                if (contentList == null || contentList.Count == 0)
                    return;

                if (index < 0 || index >= contentList.Count)
                    return;

                var content = contentList[index];
                if (content == null)
                    return;

                // Get character data from the content controller
                var characterData = content.CurrentData;
                if (characterData == null)
                    return;

                // Build announcement: "Character Name, Level X, HP current/max, MP current/max, Status effects"
                // FF2 uses MP, unlike FF3's spell charges
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return;

                string announcement = charName;

                // Add level, HP, MP, and status information
                try
                {
                    var parameter = characterData.Parameter;
                    if (parameter != null)
                    {
                        // Add level
                        int level = parameter.BaseLevel;
                        if (level > 0)
                        {
                            announcement += $", Level {level}";
                        }

                        // Add HP
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $", HP {currentHp}/{maxHp}";

                        // Add MP (FF2 specific - unlike FF3 which uses spell charges)
                        int currentMp = parameter.currentMP;
                        int maxMp = parameter.ConfirmedMaxMp();
                        announcement += $", MP {currentMp}/{maxMp}";

                        // Add status conditions
                        var conditionList = parameter.CurrentConditionList;
                        if (conditionList != null && conditionList.Count > 0)
                        {
                            var statusNames = new List<string>();
                            foreach (var condition in conditionList)
                            {
                                string conditionName = ItemMenuState.GetConditionName(condition);
                                if (!string.IsNullOrWhiteSpace(conditionName))
                                {
                                    statusNames.Add(conditionName);
                                }
                            }

                            if (statusNames.Count > 0)
                            {
                                announcement += ", " + string.Join(", ", statusNames);
                            }
                        }
                    }
                }
                catch (Exception paramEx)
                {
                    MelonLogger.Warning($"[Item Target] Error getting character parameters: {paramEx.Message}");
                }

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation
                FFII_ScreenReaderMod.ClearOtherMenuStates("Item");
                ItemMenuState.IsActive = true;

                MelonLogger.Msg($"[Item Target] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
