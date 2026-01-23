using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.AnnouncementDeduplicator;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputItemListController = Il2CppLast.UI.KeyInput.ItemListController;
using KeyInputItemUseController = Il2CppLast.UI.KeyInput.ItemUseController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using KeyInputItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using KeyInputItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;
using System.Reflection;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Helper for item menu announcements.
    /// </summary>
    public static class ItemMenuState
    {

        /// <summary>
        /// True when item list or item target selection is active.
        /// Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.ITEM_MENU);

        /// <summary>
        /// Sets the item menu as active, clearing other menu states.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.ITEM_MENU);
        }

        /// <summary>
        /// Stores the currently selected item data for 'I' key lookup.
        /// </summary>
        public static ItemListContentData LastSelectedItem { get; set; } = null;

        // State constants from dump.cs (KeyInput.ItemWindowController.State)
        private const int STATE_NONE = 0;            // Menu closed
        private const int STATE_COMMAND_SELECT = 1;  // Command bar (Use/Key Items/Sort)
        private const int STATE_USE_SELECT = 2;      // Regular item list
        private const int STATE_IMPORTANT_SELECT = 3; // Key items list
        private const int STATE_ORGANIZE_SELECT = 4;  // Organize/Sort mode
        private const int STATE_TARGET_SELECT = 5;    // Character target selection


        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Validates state machine to auto-clear when backing to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            // Validate we're actually in a submenu, not command bar
            var windowController = GameObjectCache.GetOrRefresh<KeyInputItemWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_ITEM_WINDOW);
            if (state == STATE_COMMAND_SELECT || state == STATE_NONE)
            {
                ClearState();
                return false;  // Don't suppress - let generic cursor handle command bar
            }
            return true;  // In submenu - suppress generic cursor
        }

        /// <summary>
        /// Clears item menu state when menu is closed.
        /// </summary>
        public static void ClearState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.ITEM_MENU);
            LastSelectedItem = null;
            AnnouncementDeduplicator.Reset(CONTEXT_ITEM_MENU);
        }

        /// <summary>
        /// Gets the localized name for an ItemCommandId.
        /// </summary>
        public static string GetItemCommandName(ItemCommandId commandId)
        {
            switch (commandId)
            {
                case ItemCommandId.Use:
                    return LocalizationUtility.GetLocalizedCommand("$menu_item_use") ?? "Use";
                case ItemCommandId.Organize:
                    return LocalizationUtility.GetLocalizedCommand("$menu_item_organize") ?? "Sort";
                case ItemCommandId.Important:
                    return LocalizationUtility.GetLocalizedCommand("$menu_item_important") ?? "Key Items";
                default:
                    return null;
            }
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

                // Patch ItemWindowController.SetNextState for state transition detection
                TryPatchSetNextState(harmony);

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

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_ITEM_MENU, announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                ItemMenuState.SetActive();

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
                                string conditionName = LocalizationUtility.GetConditionName(condition);
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

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_ITEM_MENU, announcement))
                    return;

                // Set active state AFTER validation
                ItemMenuState.SetActive();

                MelonLogger.Msg($"[Item Target] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches ItemWindowController.SetNextState for state transition detection.
        /// </summary>
        private static void TryPatchSetNextState(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(KeyInputItemWindowController);

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
                    var postfix = typeof(ItemMenuPatches).GetMethod(nameof(SetNextState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Item Menu] Patched ItemWindowController.SetNextState");
                }
                else
                {
                    MelonLogger.Warning("[Item Menu] ItemWindowController.SetNextState not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Item Menu] Error patching SetNextState: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetNextState - clears state when returning to command bar or closing menu.
        /// </summary>
        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                // STATE_NONE = 0 (menu closing), STATE_COMMAND_SELECT = 1 (command bar)
                if ((state == 0 || state == 1) && ItemMenuState.IsActive)
                {
                    MelonLogger.Msg($"[Item Menu] SetNextState called with state={state}, clearing IsActive");
                    ItemMenuState.ClearState();
                }
            }
            catch { }
        }
    }
}
