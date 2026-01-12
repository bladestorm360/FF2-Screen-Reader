using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;
using Il2CppLast.Battle;

// Type aliases for IL2CPP types
// FF2 uses BattleQuantityAbilityInfomationController (MP-based) instead of FF3's BattleFrequencyAbilityInfomationController (charges)
using BattleQuantityAbilityInfomationController_KeyInput = Il2CppSerial.FF2.UI.KeyInput.BattleQuantityAbilityInfomationController;
using BattleQuantityAbilityInfomationController_Touch = Il2CppSerial.FF2.UI.Touch.BattleQuantityAbilityInfomationController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using CommonGauge = Il2CppLast.UI.CommonGauge;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle magic menu.
    /// FF2 uses MP cost system instead of FF3's spell charges.
    /// Controller: BattleQuantityAbilityInfomationController (KeyInput and Touch variants)
    /// </summary>
    public static class BattleMagicPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Magic] Applying battle magic menu patches...");

                // Patch KeyInput variant
                PatchControllerType(harmony, typeof(BattleQuantityAbilityInfomationController_KeyInput), "KeyInput");

                // Patch Touch variant
                PatchControllerType(harmony, typeof(BattleQuantityAbilityInfomationController_Touch), "Touch");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error applying patches: {ex.Message}");
            }
        }

        private static void PatchControllerType(HarmonyLib.Harmony harmony, Type controllerType, string variant)
        {
            try
            {
                // Find SelectContent method - signature: SelectContent(Cursor targetCursor, CustomScrollView.WithinRangeType type = 0)
                MethodInfo selectContentMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        // Look for the method with Cursor as first parameter
                        if (parameters.Length >= 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            selectContentMethod = m;
                            MelonLogger.Msg($"[Battle Magic] Found SelectContent method ({variant})");
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleMagicSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg($"[Battle Magic] Patched SelectContent successfully ({variant})");
                }
                else
                {
                    MelonLogger.Warning($"[Battle Magic] SelectContent method not found ({variant})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error patching {variant}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// State tracking for battle magic menu.
    /// FF2 uses MP cost system instead of spell charges.
    /// </summary>
    public static class BattleMagicMenuState
    {
        public static bool IsActive { get; set; } = false;

        // State machine offsets for BattleCommandSelectController
        private const int OFFSET_STATE_MACHINE = 0x48;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        private const int STATE_NORMAL = 1;
        private const int STATE_EXTRA = 2;

        /// <summary>
        /// Check if MenuTextDiscovery should be suppressed.
        /// Returns true if we're handling magic announcements.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                // Check if either KeyInput or Touch magic controller is still active
                var keyInputController = UnityEngine.Object.FindObjectOfType<BattleQuantityAbilityInfomationController_KeyInput>();
                var touchController = UnityEngine.Object.FindObjectOfType<BattleQuantityAbilityInfomationController_Touch>();

                bool controllerActive = (keyInputController != null && keyInputController.gameObject.activeInHierarchy) ||
                                         (touchController != null && touchController.gameObject.activeInHierarchy);

                if (!controllerActive)
                {
                    Reset();
                    return false;
                }

                // Also check if command select controller is back to normal state
                // If so, we've returned to command menu - clear magic state
                var cmdController = UnityEngine.Object.FindObjectOfType<BattleCommandSelectController>();
                if (cmdController != null && cmdController.gameObject.activeInHierarchy)
                {
                    int state = GetCommandState(cmdController);
                    if (state == STATE_NORMAL || state == STATE_EXTRA)
                    {
                        // Command menu is active, we're no longer in magic selection
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

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.15f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        public static void Reset()
        {
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }
    }

    /// <summary>
    /// Patch for battle magic selection.
    /// FF2 format: "Spell Name, Level X (Y%), MP Z: Description"
    /// Gets spell proficiency from character's actual OwnedAbilityList for accuracy.
    /// </summary>
    public static class BattleMagicSelectContent_Patch
    {
        // Offsets for BattleAbilityInfomationControllerBase (parent class)
        // From dump.cs line 284575-284593:
        // protected BattlePlayerData selectedBattlePlayerData; // 0x28
        // protected List<OwnedAbility> dataList; // 0x70
        // protected List<BattleAbilityInfomationContentController> contentList; // 0x78
        private const int OFFSET_SELECTED_PLAYER = 0x28;
        private const int OFFSET_DATA_LIST = 0x70;
        private const int OFFSET_CONTENT_LIST = 0x78;

        // Offset for BattleAbilityInfomationContentController.commonGauge (KeyInput variant)
        // From dump.cs line 430372: private CommonGauge commonGauge; // 0x38
        private const int OFFSET_CONTENT_GAUGE = 0x38;

        // Offset for CommonGauge.gaugeImage
        // From dump.cs line 385715: private Image gaugeImage; // 0x18
        private const int OFFSET_GAUGE_IMAGE = 0x18;

        public static void Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                int cursorIndex = targetCursor.Index;

                // Get the announcement with proper character data lookup
                string announcement = TryGetAbilityAnnouncement(__instance, cursorIndex);

                if (string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg("[Battle Magic] Could not get spell data");
                    return;
                }

                if (!BattleMagicMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state and clear other menus
                FFII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                BattleMagicMenuState.IsActive = true;

                MelonLogger.Msg($"[Battle Magic] Announcing: {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error in SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to get the ability at the cursor index, using character's OwnedAbilityList for accurate proficiency.
        /// </summary>
        private static string TryGetAbilityAnnouncement(object controller, int cursorIndex)
        {
            try
            {
                IntPtr ptr = IntPtr.Zero;

                // Get pointer based on controller type
                if (controller is BattleQuantityAbilityInfomationController_KeyInput keyInput)
                    ptr = keyInput.Pointer;
                else if (controller is BattleQuantityAbilityInfomationController_Touch touch)
                    ptr = touch.Pointer;

                if (ptr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // Read dataList at offset 0x70
                    IntPtr dataListPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET_DATA_LIST);
                    if (dataListPtr == IntPtr.Zero)
                    {
                        MelonLogger.Msg("[Battle Magic] dataList pointer is null");
                        return null;
                    }

                    // Create managed list wrapper
                    var dataList = new Il2CppSystem.Collections.Generic.List<OwnedAbility>(dataListPtr);
                    if (dataList == null || cursorIndex < 0 || cursorIndex >= dataList.Count)
                    {
                        MelonLogger.Msg($"[Battle Magic] Index {cursorIndex} out of range (count: {dataList?.Count ?? 0})");
                        return null;
                    }

                    var ability = dataList[cursorIndex];
                    if (ability == null)
                        return "Empty";

                    // Get the selected player's character data for accurate spell proficiency
                    OwnedCharacterData characterData = null;
                    try
                    {
                        IntPtr playerPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET_SELECTED_PLAYER);
                        if (playerPtr != IntPtr.Zero)
                        {
                            var battlePlayerData = new BattlePlayerData(playerPtr);
                            characterData = battlePlayerData.ownedCharacterData;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"[Battle Magic] Could not get character data: {ex.Message}");
                    }

                    // Try to get gauge progress from content controller
                    float gaugeProgress = -1f;
                    try
                    {
                        IntPtr contentListPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET_CONTENT_LIST);
                        if (contentListPtr != IntPtr.Zero)
                        {
                            var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);
                            if (contentList != null && cursorIndex >= 0 && cursorIndex < contentList.Count)
                            {
                                var contentController = contentList[cursorIndex];
                                if (contentController != null)
                                {
                                    gaugeProgress = GetGaugeFillAmount(contentController);
                                }
                            }
                        }
                    }
                    catch { }

                    return FormatAbilityAnnouncement(ability, characterData, gaugeProgress);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[Battle Magic] Error getting ability from data list: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the gauge fill amount (0-1) from a content controller.
        /// </summary>
        private static float GetGaugeFillAmount(BattleAbilityInfomationContentController contentController)
        {
            try
            {
                IntPtr contentPtr = contentController.Pointer;
                if (contentPtr == IntPtr.Zero)
                    return -1f;

                unsafe
                {
                    // Get commonGauge at offset 0x38
                    IntPtr gaugePtr = *(IntPtr*)((byte*)contentPtr.ToPointer() + OFFSET_CONTENT_GAUGE);
                    if (gaugePtr == IntPtr.Zero)
                        return -1f;

                    // Get gaugeImage at offset 0x18
                    IntPtr imagePtr = *(IntPtr*)((byte*)gaugePtr.ToPointer() + OFFSET_GAUGE_IMAGE);
                    if (imagePtr == IntPtr.Zero)
                        return -1f;

                    var image = new Image(imagePtr);
                    if (image != null)
                    {
                        return image.fillAmount;
                    }
                }
            }
            catch { }

            return -1f;
        }

        /// <summary>
        /// Format ability data into announcement string.
        /// FF2 Format: "Spell Name, Level X (Y%), MP Z: Description"
        /// Uses character's OwnedAbilityList for accurate proficiency when available.
        /// </summary>
        private static string FormatAbilityAnnouncement(OwnedAbility ability, OwnedCharacterData characterData, float gaugeProgress)
        {
            try
            {
                // Get spell name
                string name = null;
                string mesIdName = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesIdName))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        name = messageManager.GetMessage(mesIdName, false);
                    }
                }

                if (string.IsNullOrEmpty(name))
                    return null;

                name = TextUtils.StripIconMarkup(name);
                if (string.IsNullOrEmpty(name))
                    return null;

                string announcement = name;

                // Get ability ID for matching
                int abilityId = -1;
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        abilityId = abilityData.Id;
                    }
                }
                catch { }

                // FF2 specific: Get spell level from character's actual OwnedAbilityList
                // OwnedAbility.SkillLevel stores raw exp, not actual level
                // Formula: level = (rawExp / 100) + 1, clamped to 1-16
                int spellLevel = 1;
                try
                {
                    int rawExp = 0;

                    // First try to find the ability in character's OwnedAbilityList (authoritative source)
                    if (characterData != null && abilityId >= 0)
                    {
                        var ownedAbilityList = characterData.OwnedAbilityList;
                        if (ownedAbilityList != null)
                        {
                            foreach (var ownedAbility in ownedAbilityList)
                            {
                                if (ownedAbility != null)
                                {
                                    try
                                    {
                                        var ownedAbilityData = ownedAbility.Ability;
                                        if (ownedAbilityData != null && ownedAbilityData.Id == abilityId)
                                        {
                                            rawExp = ownedAbility.SkillLevel;
                                            MelonLogger.Msg($"[Battle Magic] Found ability {abilityId} in character data, rawExp={rawExp}");
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }

                    // Fallback to the passed ability's SkillLevel if not found
                    if (rawExp <= 0)
                    {
                        rawExp = ability.SkillLevel;
                        MelonLogger.Msg($"[Battle Magic] Using dataList ability rawExp={rawExp}");
                    }

                    // Convert raw exp to level: level = (rawExp / 100) + 1
                    spellLevel = (rawExp / 100) + 1;
                }
                catch { }

                // Clamp level to valid range (1-16)
                if (spellLevel < 1) spellLevel = 1;
                if (spellLevel > 16) spellLevel = 16;

                // Add level
                if (spellLevel > 0)
                {
                    announcement += $" lv{spellLevel}";
                }

                // FF2 specific: Get MP cost from ability data
                int mpCost = 0;
                try
                {
                    var abilityDataForMp = ability.Ability;
                    if (abilityDataForMp != null)
                    {
                        mpCost = abilityDataForMp.UseValue;
                    }
                }
                catch { }

                if (mpCost > 0)
                {
                    announcement += $", MP {mpCost}";
                }

                // Add description
                try
                {
                    string mesIdDesc = ability.MesIdDescription;
                    if (!string.IsNullOrEmpty(mesIdDesc))
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string description = messageManager.GetMessage(mesIdDesc, false);
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                description = TextUtils.StripIconMarkup(description);
                                if (!string.IsNullOrWhiteSpace(description))
                                {
                                    announcement += ": " + description;
                                }
                            }
                        }
                    }
                }
                catch { }

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error formatting announcement: {ex.Message}");
                return null;
            }
        }
    }
}
