using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.AnnouncementDeduplicator;

// FF2 Shop UI types
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
using ShopTradeWindowController = Il2CppLast.UI.KeyInput.ShopTradeWindowController;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;

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
        /// <summary>
        /// True when shop menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.SHOP_MENU);

        /// <summary>
        /// Alias for IsActive for backward compatibility.
        /// </summary>
        public static bool IsShopMenuActive => IsActive;

        /// <summary>
        /// Sets the shop menu as active, clearing other menu states.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.SHOP_MENU);
        }

        /// <summary>
        /// Alias for ClearState for consistency with other menu states.
        /// </summary>
        public static void ResetState() => ClearState();

        // State constants from dump.cs (ShopController.State)
        private const int STATE_NONE = 0;           // Menu closed
        private const int STATE_SELECT_COMMAND = 1; // Command bar (Buy/Sell)


        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Validates state machine to auto-clear when backing to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            // Validate we're actually in a submenu, not command bar
            var windowController = GameObjectCache.GetOrRefresh<KeyInputShopController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(windowController.Pointer, StateReaderHelper.OFFSET_SHOP_CONTROLLER);
            if (state == STATE_SELECT_COMMAND || state == STATE_NONE)
            {
                ClearState();
                return false;  // Don't suppress - let generic cursor handle command bar
            }
            return true;  // In submenu - suppress generic cursor
        }

        /// <summary>
        /// Alias for ShouldSuppress for backward compatibility.
        /// </summary>
        public static bool ValidateState() => ShouldSuppress();

        public static string LastItemName { get; set; }
        public static string LastItemDescription { get; set; }
        public static string LastItemPrice { get; set; }
        public static string LastItemStats { get; set; }

        public static void ClearState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.SHOP_MENU);
            LastItemName = null;
            LastItemDescription = null;
            LastItemPrice = null;
            LastItemStats = null;
            AnnouncementDeduplicator.Reset(CONTEXT_SHOP_ITEM, CONTEXT_SHOP_QUANTITY);
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed.
    /// </summary>
    public static class ShopDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                if (!ShopMenuTracker.ValidateState())
                    return;

                string stats = ShopMenuTracker.LastItemStats;
                string description = ShopMenuTracker.LastItemDescription;

                string announcement = "";

                if (!string.IsNullOrEmpty(stats))
                {
                    announcement = stats;
                }

                if (!string.IsNullOrEmpty(description))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ". " + description;
                    }
                    else
                    {
                        announcement = description;
                    }
                }

                if (string.IsNullOrEmpty(announcement))
                {
                    announcement = "No item details available";
                }

                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch
            {
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
                PatchSetFocus(harmony);
                PatchTradeWindow(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to apply shop patches: {ex.Message}");
            }
        }

        private static void PatchSetFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopListItemContentController);
                var setFocusMethod = controllerType.GetMethod("SetFocus", new Type[] { typeof(bool) });

                if (setFocusMethod != null)
                {
                    harmony.Patch(setFocusMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(SetFocus_Postfix)));
                }
            }
            catch
            {
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

        public static void SetFocus_Postfix(ShopListItemContentController __instance, bool isFocus)
        {
            try
            {
                if (__instance == null)
                    return;

                // Only announce when item gains focus
                if (!isFocus)
                    return;

                ShopMenuTracker.SetActive();

                string itemName = null;
                try
                {
                    itemName = __instance.iconTextView?.nameText?.text;
                }
                catch { }

                // Get content ID early for fallback name lookup and stats
                int contentId = 0;
                try { contentId = __instance.ContentId; } catch { }

                // Fallback: get name from master data if UI text is empty (unaffordable items)
                if (string.IsNullOrEmpty(itemName) && contentId > 0)
                {
                    try
                    {
                        var masterManager = MasterManager.Instance;
                        var content = masterManager.GetData<Content>(contentId);
                        if (content != null)
                        {
                            var messageManager = MessageManager.Instance;
                            itemName = messageManager.GetMessage(content.MesIdName, false);
                        }
                    }
                    catch
                    {
                    }
                }

                if (string.IsNullOrEmpty(itemName))
                {
                    return;
                }

                itemName = TextUtils.StripIconMarkup(itemName);

                string price = null;
                try
                {
                    price = __instance.shopListItemContentView?.priceText?.text;
                }
                catch { }

                string description = null;
                try
                {
                    description = __instance.Message;
                }
                catch { }

                string stats = null;
                if (contentId > 0)
                {
                    stats = GetItemStats(contentId);
                }

                ShopMenuTracker.LastItemName = itemName;
                ShopMenuTracker.LastItemPrice = price;
                ShopMenuTracker.LastItemDescription = description;
                ShopMenuTracker.LastItemStats = stats;

                // For equipment: "Name, Price" (use I key for stats + description)
                // For items/magic: "Name, Price. Description" (description shown immediately)
                bool isEquipment = contentId > 0 && IsEquipmentItem(contentId);
                string announcement;

                if (isEquipment || string.IsNullOrEmpty(description))
                {
                    announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";
                }
                else
                {
                    announcement = string.IsNullOrEmpty(price)
                        ? $"{itemName}. {description}"
                        : $"{itemName}, {price}. {description}";
                }

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_SHOP_ITEM, announcement))
                    return;

                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch
            {
            }
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

        /// <summary>
        /// Checks if the content is equipment (weapon or armor) vs item/magic.
        /// Used to determine announcement format.
        /// </summary>
        private static bool IsEquipmentItem(int contentId)
        {
            try
            {
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return false;

                var content = masterManager.GetData<Content>(contentId);
                if (content == null)
                    return false;

                var typeId = (ContentType)content.TypeId;
                return typeId == ContentType.Weapon || typeId == ContentType.Armor;
            }
            catch
            {
                return false;
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

        private const int OFFSET_SELECTED_COUNT = 0x3C;

        public static void UpdateCotroller_Postfix(ShopTradeWindowController __instance, bool isCount)
        {
            try
            {
                if (__instance == null)
                    return;

                int selectedCount = GetSelectedCount(__instance);

                // Skip duplicates using centralized deduplication
                if (!ShouldAnnounce(CONTEXT_SHOP_QUANTITY, selectedCount))
                    return;

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
                        return *(int*)((byte*)ptr.ToPointer() + OFFSET_SELECTED_COUNT);
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
