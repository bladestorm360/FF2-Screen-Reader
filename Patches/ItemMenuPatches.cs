using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP types
using ItemWindowView = Il2CppLast.UI.ItemWindowView;
using KeyInputItemEquipmentDetailController = Il2CppLast.UI.KeyInput.ItemEquipmentDetailController;
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
    /// Announces item-menu detail when the I key / right-stick-up is pressed. Reads the live
    /// UI panel (FF1 pattern) instead of master data: the description comes from the live
    /// ItemWindowView.descriptionText; for equipment in the stats panel the visible stat rows
    /// (ItemEquipmentDetailView) are read. Which panel is shown is driven by the game's own
    /// toggle (ItemWindowView.isFrontTextVisible). Logs both sources for in-game verification.
    /// </summary>
    public static class ItemDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                var view = UnityEngine.Object.FindObjectOfType<ItemWindowView>();
                if (view == null || view.Pointer == IntPtr.Zero)
                {
                    FFII_ScreenReaderMod.SpeakText(T("No details"), interrupt: true);
                    return;
                }

                // isFrontTextVisible == true → description panel shown; false → parameter/stats panel.
                bool descriptionShown = Marshal.ReadByte(view.Pointer + IL2CppOffsets.ItemPanel.WindowViewIsFrontTextVisible) != 0;

                string description = ReadText(Marshal.ReadIntPtr(view.Pointer + IL2CppOffsets.ItemPanel.WindowViewDescriptionText));
                string stats = ReadEquipmentStatsPanel();

                MelonLogger.Msg($"[ItemDetails] descriptionShown={descriptionShown} desc='{description}' stats='{stats}'");

                string announcement = descriptionShown
                    ? (description ?? stats)
                    : (stats ?? description);

                FFII_ScreenReaderMod.SpeakText(
                    string.IsNullOrWhiteSpace(announcement) ? T("No details") : announcement,
                    interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error: {ex.Message}");
            }
        }

        /// <summary>Reads all visible stat rows from the live equipment detail panel, if shown.</summary>
        private static string ReadEquipmentStatsPanel()
        {
            try
            {
                var detail = UnityEngine.Object.FindObjectOfType<KeyInputItemEquipmentDetailController>();
                if (detail == null || detail.Pointer == IntPtr.Zero) return null;

                IntPtr viewPtr = Marshal.ReadIntPtr(detail.Pointer + IL2CppOffsets.ItemPanel.EquipmentDetailControllerView);
                if (viewPtr == IntPtr.Zero) return null;

                var view = new MonoBehaviour(viewPtr);
                if (view.gameObject == null || !view.gameObject.activeInHierarchy) return null;

                var texts = view.GetComponentsInChildren<Text>(false);
                if (texts == null) return null;

                var sb = new StringBuilder();
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    string s = t.text;
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(TextUtils.StripIconMarkup(s).Trim());
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadText(IntPtr textPtr)
        {
            if (textPtr == IntPtr.Zero) return null;
            try
            {
                var t = new Text(textPtr);
                string raw = t?.text;
                return string.IsNullOrWhiteSpace(raw) ? null : TextUtils.StripIconMarkup(raw).Trim();
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Helper for item menu announcements.
    /// </summary>
    public static class ItemMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.ITEM_MENU);

        static ItemMenuState()
        {
            _helper.RegisterResetHandler(() => { LastSelectedItem = null; });
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        public static ItemListContentData LastSelectedItem { get; set; } = null;

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Validates state machine to auto-clear when backing to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            var windowController = GameObjectCache.GetOrRefresh<KeyInputItemWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_ITEM_WINDOW);
            if (state == IL2CppOffsets.Item.STATE_COMMAND_SELECT || state == IL2CppOffsets.Item.STATE_NONE)
            {
                ClearState();
                return false;
            }
            return true;
        }

        public static void ClearState() => _helper.IsActive = false;

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
                }

                // Patch ItemWindowController.SetNextState for state transition detection
                TryPatchSetNextState(harmony);

                isPatched = true;
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

                // Build announcement: "Item Name (quantity)" — description appended only when
                // AutoDetail is on; otherwise the I key reads it on demand.
                int quantity = itemData.Count;
                string announcement = quantity > 1 ? $"{itemName} ({quantity})" : itemName;

                string description = itemData.Description;
                if (!string.IsNullOrWhiteSpace(description))
                    description = TextUtils.StripIconMarkup(description);

                // Cache the detail for the I key (item-menu items are consumables, no U-key).
                MenuDetailCache.Set(description);

                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(description))
                    announcement += ": " + description;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                ItemMenuState.SetActive();

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch
            {
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

                // Context gate on the ItemUseController's OWN state machine. Entry/navigation
                // happen in the Single/All target-select states; confirming a target transitions
                // to a Learning* state (which opens the learn popup) and re-fires SelectContent
                // with the cursor reset to index 0 — the spurious top-of-list re-read. The OUTER
                // ItemWindowController state stays TARGET_SELECT throughout, so gating on it never
                // caught the confirm. nextState (set synchronously on confirm, before the re-read)
                // covers the transition frame where the current state is still Single.
                int useState = StateReaderHelper.ReadStateTag(__instance.Pointer, IL2CppOffsets.ItemUse.OFFSET_STATE_MACHINE);
                int nextState = Marshal.ReadInt32(__instance.Pointer + IL2CppOffsets.ItemUse.OFFSET_NEXT_STATE);
                if ((useState != IL2CppOffsets.ItemUse.STATE_SINGLE && useState != IL2CppOffsets.ItemUse.STATE_ALL)
                    || nextState >= IL2CppOffsets.ItemUse.STATE_LEARNING_VERIFICATION)
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
                catch
                {
                }

                // Set active state AFTER validation
                ItemMenuState.SetActive();

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch
            {
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
                // STATE_NONE = 0 (menu closing), STATE_COMMAND_SELECT = 1 (command bar)
                if ((state == 0 || state == 1) && ItemMenuState.IsActive)
                {
                    ItemMenuState.ClearState();
                }
            }
            catch { }
        }
    }
}
