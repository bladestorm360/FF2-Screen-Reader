using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// FF2 Shop UI types
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
using ShopListMainContentController = Il2CppLast.UI.KeyInput.ShopListMainContentController;
using ShopInfoController = Il2CppLast.UI.KeyInput.ShopInfoController;
using ShopCommandMenuController = Il2CppLast.UI.KeyInput.ShopCommandMenuController;
using ShopCommandMenuContentController = Il2CppLast.UI.KeyInput.ShopCommandMenuContentController;
using ShopCommandId = Il2CppLast.Defaine.ShopCommandId;
using ShopTradeWindowController = Il2CppLast.UI.KeyInput.ShopTradeWindowController;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;
using GameCursor = Il2CppLast.UI.Cursor;

// Master data types for item stats
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Weapon = Il2CppLast.Data.Master.Weapon;
using Armor = Il2CppLast.Data.Master.Armor;
using Content = Il2CppLast.Data.Master.Content;
using ContentType = Il2CppLast.Defaine.Content.ContentType;
using MessageManager = Il2CppLast.Management.MessageManager;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu state for 'I' key description access and suppression.
    /// State is cleared by transition patch when menu closes.
    /// </summary>
    public static class ShopMenuTracker
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.SHOP_MENU);

        static ShopMenuTracker()
        {
            _helper.RegisterResetHandler(() =>
            {
                LastItemName = null;
                LastItemDescription = null;
                LastItemPrice = null;
                LastItemStats = null;
                ShopPatches.ResetGuards();
            });
        }

        public static bool IsActive => _helper.IsActive;

        public static bool IsShopMenuActive => IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        public static void ResetState() => ClearState();

        /// <summary>
        /// Check if GenericCursor should be suppressed. The command bar and the item
        /// lists are now both announced by dedicated postfixes (CommandSetCursor /
        /// SetDescription), so suppress the generic reader for the whole active shop
        /// and only auto-clear when the state machine returns to None.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            var windowController = GameObjectCache.GetOrRefresh<KeyInputShopController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_SHOP_CONTROLLER);
            if (state == IL2CppOffsets.Shop.STATE_NONE)
            {
                ClearState();
                return false;
            }
            return true;
        }

        /// <summary>Reads the live ShopController.State tag (-1/None if unavailable).</summary>
        public static int GetState()
        {
            var windowController = GameObjectCache.GetOrRefresh<KeyInputShopController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
                return IL2CppOffsets.Shop.STATE_NONE;
            return StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_SHOP_CONTROLLER);
        }

        public static bool ValidateState() => ShouldSuppress();

        public static string LastItemName { get; set; }
        public static string LastItemDescription { get; set; }
        public static string LastItemPrice { get; set; }
        public static string LastItemStats { get; set; }

        public static void ClearState() => _helper.IsActive = false;
    }

    /// <summary>
    /// Announces shop item details when the I key / right-stick-up is pressed.
    /// Reads the live UI panel text directly (FF1 pattern): the game renders whichever
    /// panel is active (stats OR description) into ShopInfoView.descriptionText, so this
    /// is inherently panel-sensitive and reads the full stats panel — no master-data lookups.
    /// </summary>
    public static class ShopDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                if (!ShopMenuTracker.ValidateState())
                    return;

                string announcement = GetActivePanelFromUI();
                if (string.IsNullOrWhiteSpace(announcement))
                    announcement = T("No item details available");

                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] Error announcing details: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the live shop info panel text:
        /// ShopInfoController -> view (0x18) -> descriptionText (0x38) -> .text
        /// </summary>
        private static string GetActivePanelFromUI()
        {
            try
            {
                var infoController = UnityEngine.Object.FindObjectOfType<ShopInfoController>();
                if (infoController == null) return null;

                IntPtr ctrlPtr = infoController.Pointer;
                if (ctrlPtr == IntPtr.Zero) return null;

                IntPtr viewPtr = Marshal.ReadIntPtr(ctrlPtr + IL2CppOffsets.ShopInfo.InfoView);
                if (viewPtr == IntPtr.Zero) return null;

                IntPtr textPtr = Marshal.ReadIntPtr(viewPtr + IL2CppOffsets.ShopInfo.DescriptionText);
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
    /// Shop menu patches using manual Harmony patching.
    /// </summary>
    public static class ShopPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                PatchSetDescription(harmony);
                PatchCommandSetCursor(harmony);
                PatchTradeWindow(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to apply shop patches: {ex.Message}");
            }
        }

        // ShopInfoController.SetDescription(string) fires on every cursor move in the buy/sell
        // item list regardless of affordability (unlike ShopListItemContentController.SetFocus,
        // which never fires for greyed/unaffordable items). Use it as the cursor-moved signal
        // and read the focused item from ShopListMainContentController.
        private static void PatchSetDescription(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = typeof(ShopInfoController).GetMethod("SetDescription", new Type[] { typeof(string) });
                if (method != null)
                {
                    harmony.Patch(method,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(SetDescription_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopInfoController.SetDescription(string)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch SetDescription: {ex.Message}");
            }
        }

        // ShopCommandMenuController.SetCursor(int) fires when the Buy/Sell/Equipment/Back
        // command bar is focused — on shop open AND on navigation — so it announces the
        // initial command without an arrow press.
        private static void PatchCommandSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = typeof(ShopCommandMenuController).GetMethod(
                    "SetCursor",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(int) },
                    null);
                if (method != null)
                {
                    harmony.Patch(method,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(CommandSetCursor_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopCommandMenuController.SetCursor(int)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch command SetCursor: {ex.Message}");
            }
        }

        private static void PatchTradeWindow(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type tradeType = typeof(ShopTradeWindowController);
                var updateMethod = tradeType.GetMethod("UpdateCotroller", new Type[] { typeof(bool) });

                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(UpdateCotroller_Postfix)));
                }
            }
            catch
            {
            }
        }

        private static ShopListMainContentController _cachedMainList;
        // Local value/index guards replace the central deduplicator: announce only when the
        // focused item / command / quantity actually changed. Re-armed at level boundaries
        // (see CommandSetCursor / SetDescription) and cleared on shop close (ResetGuards).
        private static int _lastAnnouncedListIndex = -1;
        private static int _lastCommandIndex = -1;
        private static int _lastQuantity = -1;

        internal static void ResetGuards()
        {
            _lastAnnouncedListIndex = -1;
            _lastCommandIndex = -1;
            _lastQuantity = -1;
        }

        /// <summary>
        /// Fires on every cursor move in the shop's buy/sell item list — including greyed
        /// unaffordable items, which ShopListItemContentController.SetFocus skips. Reads the
        /// focused item from ShopListMainContentController (selectCursor.Index into
        /// productContentList) and announces it. The description parameter is ignored.
        /// </summary>
        public static void SetDescription_Postfix(ShopInfoController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Gate on the ShopController state — only the buy/sell item lists.
                // Other states (command bar, confirm, equipment) also fire SetDescription
                // as a panel refresh and must not announce an item.
                int state = ShopMenuTracker.GetState();
                if (state != IL2CppOffsets.Shop.STATE_SELECT_PRODUCT && state != IL2CppOffsets.Shop.STATE_SELECT_SELL_ITEM)
                {
                    _lastAnnouncedListIndex = -1;
                    return;
                }

                var mainList = FindActiveMainContentController();
                if (mainList == null)
                    return;

                ShopMenuTracker.SetActive();
                // In the item list — re-arm the command-bar and quantity announcements for
                // the trip back / into the trade window.
                _lastCommandIndex = -1;
                _lastQuantity = -1;

                IntPtr instancePtr = mainList.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return;

                IntPtr cursorPtr = StateReaderHelper.ReadPointerField(instancePtr, IL2CppOffsets.Shop.LIST_MAIN_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero)
                    return;

                int index = new GameCursor(cursorPtr).Index;
                if (index < 0)
                    return;

                // SetDescription also fires on a stats/description panel toggle for the same
                // focused item; dedup by list index. Resets at state-exit boundaries keep
                // re-entries on the same cursor audible.
                if (index == _lastAnnouncedListIndex)
                    return;
                _lastAnnouncedListIndex = index;

                IntPtr listPtr = StateReaderHelper.ReadPointerField(instancePtr, IL2CppOffsets.Shop.LIST_MAIN_PRODUCT_LIST);
                if (listPtr == IntPtr.Zero)
                    return;

                var list = new Il2CppSystem.Collections.Generic.List<ShopListItemContentController>(listPtr);
                if (list == null || index >= list.Count)
                    return;

                AnnounceShopItem(list[index]);
            }
            catch { }
        }

        private static ShopListMainContentController FindActiveMainContentController()
        {
            try
            {
                if (_cachedMainList != null)
                {
                    try
                    {
                        if (_cachedMainList.Pointer != IntPtr.Zero &&
                            _cachedMainList.gameObject != null &&
                            _cachedMainList.gameObject.activeInHierarchy)
                        {
                            return _cachedMainList;
                        }
                    }
                    catch { _cachedMainList = null; }
                }

                var all = UnityEngine.Object.FindObjectsOfType<ShopListMainContentController>();
                if (all != null)
                {
                    foreach (var candidate in all)
                    {
                        if (candidate == null) continue;
                        try
                        {
                            if (candidate.gameObject != null && candidate.gameObject.activeInHierarchy)
                            {
                                _cachedMainList = candidate;
                                return candidate;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Announces a focused shop item (name, price, and inline description for
        /// consumables) and caches it for the I key. Empty sell slots announce "Empty"
        /// without overwriting the cached I-key target.
        /// </summary>
        private static void AnnounceShopItem(ShopListItemContentController content)
        {
            if (content == null)
                return;

            string itemName = null;
            try { itemName = content.iconTextView?.nameText?.text; } catch { }

            int contentId = 0;
            try { contentId = content.ContentId; } catch { }

            // Fallback: master-data name when the UI text is empty (unaffordable items).
            if (string.IsNullOrEmpty(itemName) && contentId > 0)
            {
                try
                {
                    var master = MasterManager.Instance.GetData<Content>(contentId);
                    if (master != null)
                        itemName = MessageManager.Instance.GetMessage(master.MesIdName, false);
                }
                catch { }
            }

            if (string.IsNullOrEmpty(itemName))
            {
                // Empty sell slot — announce but keep the last real item for the I key.
                FFII_ScreenReaderMod.SpeakText("Empty");
                return;
            }

            itemName = TextUtils.StripIconMarkup(itemName);

            string price = null;
            try { price = content.shopListItemContentView?.priceText?.text; } catch { }

            string description = null;
            try { description = content.Message; } catch { }

            string stats = contentId > 0 ? GetItemStats(contentId) : null;

            ShopMenuTracker.LastItemName = itemName;
            ShopMenuTracker.LastItemPrice = price;
            ShopMenuTracker.LastItemDescription = description;
            ShopMenuTracker.LastItemStats = stats;

            // Detail = stats + description; the I key reads it via ShopDetailsAnnouncer
            // (ShopMenuTracker.LastItemStats/Description, set above).
            string detail = stats;
            if (!string.IsNullOrEmpty(description))
                detail = string.IsNullOrEmpty(detail) ? description : $"{detail}. {description}";

            // Terse by default ("Name, Price"); AutoDetail appends the detail inline.
            string baseAnnouncement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";
            string announcement = baseAnnouncement;
            if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrWhiteSpace(detail))
                announcement = $"{baseAnnouncement}: {detail}";

            // Caller (SetDescription) already gated on the list-index guard, so announce.
            FFII_ScreenReaderMod.SpeakText(announcement);
        }

        /// <summary>
        /// Fires when the Buy/Sell/Equipment/Back command bar is focused — on shop open
        /// AND on navigation — so the initial command is announced without an arrow press.
        /// </summary>
        public static void CommandSetCursor_Postfix(ShopCommandMenuController __instance, int index)
        {
            try
            {
                if (__instance == null)
                    return;

                // Gate on the ShopController state machine (FF1 1b6b1cf pattern, applied to the
                // command reader). The command bar's SetCursor still fires during init on shops
                // that open straight into the buy list (state SELECT_PRODUCT); speaking "Buy"
                // there only gets cut off by the focused item (SetDescription_Postfix). Only
                // announce when the command bar genuinely has focus.
                if (ShopMenuTracker.GetState() != IL2CppOffsets.Shop.STATE_SELECT_COMMAND)
                    return;

                ShopMenuTracker.SetActive();
                // In the command bar — re-arm the item-list announcement for the trip back.
                _lastAnnouncedListIndex = -1;

                var contentList = __instance.contentList;
                if (contentList == null || index < 0 || index >= contentList.Count)
                    return;

                var commandContent = contentList[index];
                if (commandContent == null)
                    return;

                string commandName = GetShopCommandName(commandContent.CommandId);
                if (string.IsNullOrEmpty(commandName))
                    return;

                // SetCursor can fire more than once for the same focus on open — local
                // index guard announces once per command.
                if (index == _lastCommandIndex)
                    return;
                _lastCommandIndex = index;

                FFII_ScreenReaderMod.SpeakText(commandName, interrupt: true);
            }
            catch { }
        }

        private static string GetShopCommandName(ShopCommandId commandId)
        {
            return commandId switch
            {
                ShopCommandId.Buy => "Buy",
                ShopCommandId.Sell => "Sell",
                ShopCommandId.Equipment => "Equipment",
                ShopCommandId.Back => "Back",
                _ => null
            };
        }

        private static string GetItemStats(int contentId)
        {
            try
            {
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                var content = masterManager.GetData<Content>(contentId);
                if (content == null)
                    return null;

                int typeId = content.TypeId;
                int actualItemId = content.TypeValue;

                switch ((ContentType)typeId)
                {
                    case ContentType.Weapon:
                        return GetWeaponStats(masterManager, actualItemId);
                    case ContentType.Armor:
                        return GetArmorStats(masterManager, actualItemId);
                    default:
                        // Non-equipment items (Item, MagicItem, Ability) use description only
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetWeaponStats(MasterManager masterManager, int weaponId)
        {
            try
            {
                var weapon = masterManager.GetData<Weapon>(weaponId);
                if (weapon == null)
                    return null;

                var stats = new List<string>();

                int attack = weapon.Attack;
                if (attack > 0)
                    stats.Add($"Attack {attack}");

                int accuracy = weapon.AccuracyRate;
                if (accuracy > 0)
                    stats.Add($"Accuracy {accuracy}");

                int evasion = weapon.EvasionRate;
                if (evasion > 0)
                    stats.Add($"Evasion {evasion}");

                if (weapon.Strength > 0) stats.Add($"Strength +{weapon.Strength}");
                if (weapon.Vitality > 0) stats.Add($"Vitality +{weapon.Vitality}");
                if (weapon.Agility > 0) stats.Add($"Agility +{weapon.Agility}");
                if (weapon.Intelligence > 0) stats.Add($"Intelligence +{weapon.Intelligence}");
                if (weapon.Spirit > 0) stats.Add($"Spirit +{weapon.Spirit}");
                if (weapon.Magic > 0) stats.Add($"Magic +{weapon.Magic}");

                return stats.Count > 0 ? string.Join(", ", stats) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetArmorStats(MasterManager masterManager, int armorId)
        {
            try
            {
                var armor = masterManager.GetData<Armor>(armorId);
                if (armor == null)
                    return null;

                var stats = new List<string>();

                int defense = armor.Defense;
                if (defense > 0)
                    stats.Add($"Defense {defense}");

                int magicDefense = armor.AbilityDefense;
                if (magicDefense > 0)
                    stats.Add($"Magic Defense {magicDefense}");

                int evasion = armor.EvasionRate;
                if (evasion > 0)
                    stats.Add($"Evasion {evasion}");

                int magicEvasion = armor.AbilityEvasionRate;
                if (magicEvasion > 0)
                    stats.Add($"Magic Evasion {magicEvasion}");

                if (armor.Strength > 0) stats.Add($"Strength +{armor.Strength}");
                if (armor.Vitality > 0) stats.Add($"Vitality +{armor.Vitality}");
                if (armor.Agility > 0) stats.Add($"Agility +{armor.Agility}");
                if (armor.Intelligence > 0) stats.Add($"Intelligence +{armor.Intelligence}");
                if (armor.Spirit > 0) stats.Add($"Spirit +{armor.Spirit}");
                if (armor.Magic > 0) stats.Add($"Magic +{armor.Magic}");

                return stats.Count > 0 ? string.Join(", ", stats) : null;
            }
            catch
            {
                return null;
            }
        }

        public static void UpdateCotroller_Postfix(ShopTradeWindowController __instance, bool isCount)
        {
            try
            {
                if (__instance == null)
                    return;

                int selectedCount = GetSelectedCount(__instance);

                // Local quantity guard — the trade window calls this every frame for visual sync.
                if (selectedCount == _lastQuantity)
                    return;
                _lastQuantity = selectedCount;

                string totalPrice = GetTotalPriceText(__instance);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? selectedCount.ToString()
                    : $"{selectedCount}, {totalPrice}";

                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch
            {
            }
        }

        private static int GetSelectedCount(ShopTradeWindowController controller)
        {
            try
            {
                unsafe
                {
                    IntPtr ptr = controller.Pointer;
                    if (ptr != IntPtr.Zero)
                    {
                        return *(int*)((byte*)ptr.ToPointer() + IL2CppOffsets.Shop.OFFSET_SELECTED_COUNT);
                    }
                }
            }
            catch { }
            return 0;
        }

        private static string GetTotalPriceText(ShopTradeWindowController controller)
        {
            try
            {
                var view = controller.view;
                if (view != null)
                {
                    var priceText = view.totarlPriceText;
                    if (priceText != null)
                    {
                        return priceText.text;
                    }
                }
            }
            catch { }
            return null;
        }

    }
}
