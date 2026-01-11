using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

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
    /// Uses state machine validation to only suppress during item list navigation.
    /// </summary>
    public static class ShopMenuTracker
    {
        public static bool IsShopMenuActive { get; set; }

        /// <summary>
        /// Alias for IsShopMenuActive for consistency with other menu states.
        /// </summary>
        public static bool IsActive => IsShopMenuActive;

        /// <summary>
        /// Alias for ClearState for consistency with other menu states.
        /// </summary>
        public static void ResetState() => ClearState();

        /// <summary>
        /// Alias for ValidateState for consistency with other menu states.
        /// </summary>
        public static bool ShouldSuppress() => ValidateState();
        public static string LastItemName { get; set; }
        public static string LastItemDescription { get; set; }
        public static string LastItemPrice { get; set; }
        public static string LastItemStats { get; set; }

        // State machine offsets
        private const int OFFSET_STATE_MACHINE = 0x98;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // ShopController.State values
        private const int STATE_NONE = 0;
        private const int STATE_SELECT_COMMAND = 1;
        private const int STATE_SELECT_PRODUCT = 2;
        private const int STATE_SELECT_SELL_ITEM = 3;
        private const int STATE_SELECT_ABILITY_TARGET = 4;
        private const int STATE_SELECT_EQUIPMENT = 5;
        private const int STATE_CONFIRMATION_BUY_ITEM = 6;
        private const int STATE_CONFIRMATION_SELL_ITEM = 7;
        private const int STATE_CONFIRMATION_FORGET_MAGIC = 8;
        private const int STATE_CONFIRMATION_BUY_MAGIC = 9;

        /// <summary>
        /// Validates that shop menu is active and should suppress generic cursor.
        /// </summary>
        public static bool ValidateState()
        {
            if (!IsShopMenuActive)
                return false;

            try
            {
                var shopController = UnityEngine.Object.FindObjectOfType<KeyInputShopController>();
                if (shopController != null)
                {
                    int currentState = GetCurrentState(shopController);

                    if (currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }

                    // Command menu: Don't suppress
                    if (currentState == STATE_SELECT_COMMAND)
                    {
                        return false;
                    }

                    // Item list states: Suppress
                    if (currentState >= STATE_SELECT_PRODUCT)
                    {
                        return true;
                    }
                }

                var shopItemController = UnityEngine.Object.FindObjectOfType<ShopListItemContentController>();
                if (shopItemController != null && shopItemController.gameObject.activeInHierarchy)
                {
                    return true;
                }

                ClearState();
                return false;
            }
            catch
            {
                ClearState();
                return false;
            }
        }

        private static int GetCurrentState(KeyInputShopController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }

        public static void ClearState()
        {
            IsShopMenuActive = false;
            LastItemName = null;
            LastItemDescription = null;
            LastItemPrice = null;
            LastItemStats = null;
            ShopPatches.ResetQuantityTracking();
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

                MelonLogger.Msg($"[Shop Details] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing shop details: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Shop menu patches using manual Harmony patching.
    /// </summary>
    public static class ShopPatches
    {
        private static bool isPatched = false;
        private static string lastAnnouncedText = "";
        private static float lastAnnouncedTime = 0f;
        private const float DIFFERENT_ITEM_DEBOUNCE = 0.1f;
        private const float SAME_ITEM_DEBOUNCE = 0.15f;
        private static int lastAnnouncedQuantity = -1;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                MelonLogger.Msg("[Shop] Applying shop patches...");

                PatchSetFocus(harmony);
                PatchTradeWindow(harmony);

                isPatched = true;
                MelonLogger.Msg("[Shop] Shop patches applied successfully");
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
                    MelonLogger.Msg("[Shop] Patched ShopListItemContentController.SetFocus");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch SetFocus: {ex.Message}");
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
                    MelonLogger.Msg("[Shop] Patched ShopTradeWindowController.UpdateCotroller");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch trade window: {ex.Message}");
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

                FFII_ScreenReaderMod.ClearOtherMenuStates("Shop");
                ShopMenuTracker.IsShopMenuActive = true;

                string itemName = null;
                try
                {
                    itemName = __instance.iconTextView?.nameText?.text;
                }
                catch { }

                // Get content ID early for fallback name lookup and stats
                int contentId = 0;
                try { contentId = __instance.ContentId; } catch { }

                // Debug: log what we found
                MelonLogger.Msg($"[Shop DEBUG] UI itemName='{itemName}', contentId={contentId}");

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
                            MelonLogger.Msg($"[Shop DEBUG] Fallback itemName='{itemName}'");
                        }
                        else
                        {
                            MelonLogger.Msg($"[Shop DEBUG] content is null for contentId={contentId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"[Shop DEBUG] Fallback exception: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(itemName))
                {
                    MelonLogger.Msg($"[Shop DEBUG] itemName still empty, returning");
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

                float currentTime = UnityEngine.Time.time;
                float timeSinceLastAnnouncement = currentTime - lastAnnouncedTime;

                if (announcement != lastAnnouncedText && timeSinceLastAnnouncement < DIFFERENT_ITEM_DEBOUNCE)
                    return;

                if (announcement == lastAnnouncedText && timeSinceLastAnnouncement < SAME_ITEM_DEBOUNCE)
                    return;

                lastAnnouncedText = announcement;
                lastAnnouncedTime = currentTime;

                MelonLogger.Msg($"[Shop Item] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in SetFocus_Postfix: {ex.Message}");
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

                if (selectedCount == lastAnnouncedQuantity)
                    return;

                lastAnnouncedQuantity = selectedCount;

                string totalPrice = GetTotalPriceText(__instance);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? selectedCount.ToString()
                    : $"{selectedCount}, {totalPrice}";

                MelonLogger.Msg($"[Shop Quantity] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in UpdateCotroller_Postfix: {ex.Message}");
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

        public static void ResetQuantityTracking()
        {
            lastAnnouncedQuantity = -1;
        }
    }
}
