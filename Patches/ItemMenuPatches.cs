using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Menus;
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

                // (No ItemWindowController.SetNextState hook: its body 0x4A9B40 is folded with seven
                // setters and has no direct callers, so it never fired. ItemMenuState clears itself in
                // ShouldSuppress on the command bar / None, and on SetActive(false).)

                // Item list / item-use target (re)entry reads
                FieldItemReannouncePatches.ApplyPatches(harmony);

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

                if (AnnounceItemListData(itemData, index, targetList.Count))
                    FieldItemReannouncePatches.ItemListAnnounced();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Announces an item-list row: "Item Name, quantity" (+ ": description" with AutoDetail),
        /// with its "(X of Y)" position. Shared by navigation and the list (re)entry read.
        /// Returns true when spoken.
        /// </summary>
        internal static bool AnnounceItemListData(ItemListContentData itemData, int index, int count)
        {
            try
            {
                // Store selected item for 'I' key lookup
                ItemMenuState.LastSelectedItem = itemData;

                string itemName = itemData.Name;
                if (string.IsNullOrEmpty(itemName))
                    return false;

                // Strip icon markup from name
                itemName = TextUtils.StripIconMarkup(itemName);

                if (string.IsNullOrEmpty(itemName))
                    return false;

                // Build announcement: "Item Name, quantity" — description appended only when
                // AutoDetail is on; otherwise the I key reads it on demand. (A comma, not parentheses,
                // so the quantity can't run into the "(X of Y)" position suffix.)
                int quantity = itemData.Count;
                string announcement = quantity > 1 ? $"{itemName}, {quantity}" : itemName;

                string description = itemData.Description;
                if (!string.IsNullOrWhiteSpace(description))
                    description = TextUtils.StripIconMarkup(description);

                // Cache the detail for the I key (item-menu items are consumables, no U-key).
                MenuDetailCache.Set(description);

                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(description))
                    announcement += ": " + description;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                ItemMenuState.SetActive();

                announcement = MenuPosition.Format(announcement, index, count);
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                return true;
            }
            catch
            {
                return false;
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

                if (!IsSelectingTarget(__instance))
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

                if (AnnounceItemUseTarget(content, index, contentList.Count))
                    FieldItemReannouncePatches.ItemTargetAnnounced();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Context gate on the ItemUseController's OWN state machine. Entry/navigation happen in the
        /// Single/All target-select states; confirming a target transitions to a Learning* state (which
        /// opens the learn popup) and re-fires SelectContent with the cursor reset to index 0 — the
        /// spurious top-of-list re-read. The OUTER ItemWindowController state stays TARGET_SELECT
        /// throughout, so gating on it never caught the confirm. nextState (set synchronously on
        /// confirm, before the re-read) covers the transition frame where the current state is still Single.
        /// </summary>
        internal static bool IsSelectingTarget(KeyInputItemUseController controller)
        {
            int useState = StateReaderHelper.ReadStateTag(controller.Pointer, IL2CppOffsets.ItemUse.OFFSET_STATE_MACHINE);
            int nextState = Marshal.ReadInt32(controller.Pointer + IL2CppOffsets.ItemUse.OFFSET_NEXT_STATE);
            return (useState == IL2CppOffsets.ItemUse.STATE_SINGLE || useState == IL2CppOffsets.ItemUse.STATE_ALL)
                && nextState < IL2CppOffsets.ItemUse.STATE_LEARNING_VERIFICATION;
        }

        /// <summary>
        /// Announces an item-use target: "Character Name, Level X, HP current/max, MP current/max,
        /// Status effects" with its "(X of Y)". Shared by navigation and the target (re)entry read.
        /// Returns true when spoken.
        /// </summary>
        internal static bool AnnounceItemUseTarget(ItemTargetSelectContentController content, int index, int count)
        {
            try
            {
                // Get character data from the content controller
                var characterData = content.CurrentData;
                if (characterData == null)
                    return false;

                // FF2 uses MP, unlike FF3's spell charges
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return false;

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
                            announcement += ", " + string.Format(T("Level {0}"), level);
                        }

                        // Add HP
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $", {T("HP")} {currentHp}/{maxHp}";

                        // Add MP (FF2 specific - unlike FF3 which uses spell charges)
                        int currentMp = parameter.currentMP;
                        int maxMp = parameter.ConfirmedMaxMp();
                        announcement += $", {T("MP")} {currentMp}/{maxMp}";

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

                announcement = MenuPosition.Format(announcement, index, count);
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Announces the focused row when the item LIST or the item-use TARGET list (re)gains focus — on
    /// entry and on back-out from a deeper screen (FF1 FieldItemReannouncePatches port). The list's
    /// SelectContent only fires on cursor movement (UseSelectInit & co. never call it), and AllInit
    /// never selects either. Each state-entry Init arms a one-shot in a PREFIX — so a SelectContent
    /// the Init body itself makes (SingleInit does) announces and disarms it — and its POSTFIX starts
    /// a deferred read one frame later that retries (capped) until the list is built. No per-frame
    /// hook (CLAUDE.md rule 3). All five Inits have unique RVAs (dump.cs:450812-450830, 451880-451889).
    /// </summary>
    public static class FieldItemReannouncePatches
    {
        // KeyInput ItemListController (dump.cs:450675) / ItemUseController (dump.cs:451796)
        private const int ITEM_LIST_SELECT_CURSOR = 0x60;
        private const int ITEM_LIST_DATA_LIST = 0x78;     // IEnumerable<ItemListContentData>
        private const int ITEM_USE_CONTENT_LIST = 0x40;   // List<ItemTargetSelectContentController>
        private const int ITEM_USE_SELECT_CURSOR = 0x50;
        private const int MAX_RETRY_FRAMES = 30;

        private static bool _pendingItemList;
        private static int _itemListGen;
        private static bool _pendingItemTarget;
        private static int _itemTargetGen;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            foreach (var init in new[] { "UseSelectInit", "ImportantSelectInit", "OrganizeSelectInit" })
                Patch(harmony, typeof(KeyInputItemListController), init, nameof(ItemList_Init_Prefix), nameof(ItemList_Init_Postfix));

            foreach (var init in new[] { "SingleInit", "AllInit" })
                Patch(harmony, typeof(KeyInputItemUseController), init, nameof(ItemTarget_Init_Prefix), nameof(ItemTarget_Init_Postfix));
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, string prefixName, string postfixName)
        {
            try
            {
                var target = AccessTools.Method(type, method, Type.EmptyTypes);
                if (target == null)
                {
                    MelonLogger.Error($"[Item Menu] {type.Name}.{method} not found");
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(FieldItemReannouncePatches), prefixName)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(FieldItemReannouncePatches), postfixName)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Item Menu] Error patching {type.Name}.{method}: {ex.Message}");
            }
        }

        public static void ItemList_Init_Prefix() { _pendingItemList = true; _itemListGen++; }
        public static void ItemTarget_Init_Prefix() { _pendingItemTarget = true; _itemTargetGen++; }

        /// <summary>Navigation spoke the row — the pending entry read is satisfied.</summary>
        internal static void ItemListAnnounced() => _pendingItemList = false;
        internal static void ItemTargetAnnounced() => _pendingItemTarget = false;

        /// <summary>
        /// The item command bar has the focus again (CommandSelectInit): a list / target read still
        /// retrying belongs to a screen that was left, so it must not speak over the command read.
        /// </summary>
        internal static void CancelPending()
        {
            _pendingItemList = false;
            _pendingItemTarget = false;
            _itemListGen++;
            _itemTargetGen++;
        }

        /// <summary>Starts the deferred list read unless the Init body's own SelectContent already spoke.</summary>
        public static void ItemList_Init_Postfix(KeyInputItemListController __instance)
        {
            try
            {
                if (_pendingItemList && __instance != null)
                    CoroutineManager.StartManaged(DeferredItemListRead(__instance, _itemListGen));
            }
            catch { }
        }

        /// <summary>Starts the deferred target read unless the Init body's own SelectContent already spoke.</summary>
        public static void ItemTarget_Init_Postfix(KeyInputItemUseController __instance)
        {
            try
            {
                if (_pendingItemTarget && __instance != null)
                    CoroutineManager.StartManaged(DeferredItemTargetRead(__instance, _itemTargetGen));
            }
            catch { }
        }

        // Each read waits a frame, then retries once per frame (capped) until the row is spoken, a
        // navigation SelectContent satisfied it, or a newer Init superseded it. yield stays outside
        // the try (yield-in-try-with-catch is illegal).
        private static IEnumerator DeferredItemListRead(KeyInputItemListController controller, int gen)
        {
            for (int frame = 0; frame < MAX_RETRY_FRAMES; frame++)
            {
                yield return null;
                if (!_pendingItemList || gen != _itemListGen) yield break;

                bool done = false;
                try
                {
                    if (controller != null && controller.gameObject.activeInHierarchy)
                        done = TryAnnounceItemList(controller);
                }
                catch { }
                if (done)
                {
                    _pendingItemList = false;
                    yield break;
                }
            }
            if (gen == _itemListGen) _pendingItemList = false;
        }

        private static IEnumerator DeferredItemTargetRead(KeyInputItemUseController controller, int gen)
        {
            for (int frame = 0; frame < MAX_RETRY_FRAMES; frame++)
            {
                yield return null;
                if (!_pendingItemTarget || gen != _itemTargetGen) yield break;

                bool done = false;
                try
                {
                    if (controller != null && controller.gameObject.activeInHierarchy)
                        done = TryAnnounceItemTarget(controller);
                }
                catch { }
                if (done)
                {
                    _pendingItemTarget = false;
                    yield break;
                }
            }
            if (gen == _itemTargetGen) _pendingItemTarget = false;
        }

        private static bool TryAnnounceItemList(KeyInputItemListController controller)
        {
            if (!IsMenuOpen()) return false;
            IntPtr ptr = controller.Pointer;

            IntPtr dataPtr = Marshal.ReadIntPtr(ptr, ITEM_LIST_DATA_LIST);
            IntPtr cursorPtr = Marshal.ReadIntPtr(ptr, ITEM_LIST_SELECT_CURSOR);
            if (dataPtr == IntPtr.Zero || cursorPtr == IntPtr.Zero) return false;

            var enumerable = new Il2CppSystem.Object(dataPtr)
                .TryCast<Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>>();
            if (enumerable == null) return false;
            var list = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(enumerable);

            int index = new GameCursor(cursorPtr).Index;
            if (index < 0 || index >= list.Count || list[index] == null) return false;
            return ItemMenuPatches.AnnounceItemListData(list[index], index, list.Count);
        }

        private static bool TryAnnounceItemTarget(KeyInputItemUseController controller)
        {
            if (!IsMenuOpen() || !ItemMenuPatches.IsSelectingTarget(controller)) return false;
            IntPtr ptr = controller.Pointer;

            IntPtr listPtr = Marshal.ReadIntPtr(ptr, ITEM_USE_CONTENT_LIST);
            IntPtr cursorPtr = Marshal.ReadIntPtr(ptr, ITEM_USE_SELECT_CURSOR);
            if (listPtr == IntPtr.Zero || cursorPtr == IntPtr.Zero) return false;

            var list = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(listPtr);
            int index = new GameCursor(cursorPtr).Index;
            if (index < 0 || index >= list.Count || list[index] == null) return false;
            return ItemMenuPatches.AnnounceItemUseTarget(list[index], index, list.Count);
        }

        // MenuManager.IsOpen is false during a map/asset load's scene-construction flurry.
        private static bool IsMenuOpen()
        {
            try { var mm = Il2CppLast.UI.MenuManager.Instance; return mm != null && mm.IsOpen; }
            catch { return false; }
        }
    }
}
