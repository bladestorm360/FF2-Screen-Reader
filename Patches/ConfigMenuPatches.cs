using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using ConfigCommandController = Il2CppLast.UI.KeyInput.ConfigCommandController;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracking for config menu.
    /// </summary>
    public static class ConfigMenuState
    {
        /// <summary>
        /// True when config menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.CONFIG_MENU);

        private static string lastAnnouncedText = "";
        private static string lastAnnouncedSettingName = "";

        /// <summary>
        /// Sets the config menu as active, clearing other menu states.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.CONFIG_MENU);
        }

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// State is cleared by transition patch when menu closes.
        /// </summary>
        public static bool ShouldSuppress() => IsActive;

        public static void ResetState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.CONFIG_MENU);
            lastAnnouncedText = "";
            lastAnnouncedSettingName = "";
        }

        /// <summary>
        /// Checks if announcement should proceed (deduplication).
        /// </summary>
        public static bool ShouldAnnounce(string announcement, out bool isValueChangeOnly)
        {
            isValueChangeOnly = false;

            if (announcement == lastAnnouncedText)
                return false;

            // Check if this is the same setting but different value
            string[] parts = announcement.Split(new[] { ": " }, 2, StringSplitOptions.None);
            if (parts.Length >= 1)
            {
                string settingName = parts[0];
                if (settingName == lastAnnouncedSettingName && parts.Length > 1)
                {
                    // Same setting, value changed - announce just the value
                    isValueChangeOnly = true;
                }
                lastAnnouncedSettingName = settingName;
            }

            lastAnnouncedText = announcement;
            return true;
        }
    }

    /// <summary>
    /// Helper class for reading config values.
    /// </summary>
    public static class ConfigMenuReader
    {
        /// <summary>
        /// Find config value directly from a ConfigCommandController instance.
        /// </summary>
        public static string FindConfigValueFromController(ConfigCommandController controller)
        {
            try
            {
                if (controller == null || controller.view == null)
                    return null;

                var view = controller.view;

                // Check arrow change text (for toggle/selection options)
                if (view.ArrowSelectTypeRoot != null && view.ArrowSelectTypeRoot.activeSelf)
                {
                    var arrowRoot = view.ArrowSelectTypeRoot;
                    var texts = arrowRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string value = text.text.Trim();
                            if (IsValidConfigValue(value))
                                return value;
                        }
                    }
                }

                // Check slider value (for volume sliders)
                if (view.SliderTypeRoot != null && view.SliderTypeRoot.activeSelf)
                {
                    if (view.Slider != null)
                    {
                        string percentage = GetSliderPercentage(view.Slider);
                        if (!string.IsNullOrEmpty(percentage))
                            return percentage;
                    }
                }

                // Check dropdown
                if (view.DropDownTypeRoot != null && view.DropDownTypeRoot.activeSelf)
                {
                    if (view.DropDown != null)
                    {
                        var dropdown = view.DropDown;
                        if (dropdown.options != null && dropdown.value >= 0 && dropdown.value < dropdown.options.Count)
                        {
                            string dropdownText = dropdown.options[dropdown.value].text;
                            if (!string.IsNullOrEmpty(dropdownText))
                                return dropdownText;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error reading config value: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Converts a slider value to percentage.
        /// </summary>
        public static string GetSliderPercentage(UnityEngine.UI.Slider slider)
        {
            if (slider == null) return null;

            float min = slider.minValue;
            float max = slider.maxValue;
            float current = slider.value;

            float range = max - min;
            if (range <= 0) return "0%";

            float percentage = ((current - min) / range) * 100f;
            int roundedPercentage = (int)Math.Round(percentage);

            return $"{roundedPercentage}%";
        }

        /// <summary>
        /// Check if a config value is valid.
        /// </summary>
        public static bool IsValidConfigValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Filter out arrow characters
            if (value == "<" || value == ">" || value == "◀" || value == "▶" ||
                value == "←" || value == "→")
                return false;

            // Filter out template/placeholder values
            if (value == "NewText" || value == "ReEquip" || value == "Text" ||
                value == "Label" || value == "Value" || value == "Name")
                return false;

            return true;
        }
    }

    /// <summary>
    /// Patches for config menu navigation.
    /// Uses manual Harmony patching due to FFPR's IL2CPP constraints.
    /// </summary>
    public static class ConfigMenuPatches
    {
        private static bool isPatched = false;
        private static string lastArrowValue = "";
        private static string lastSliderPercentage = "";
        private static ConfigCommandController lastController = null;

        /// <summary>
        /// Applies config menu patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch ConfigCommandController.SetFocus for navigation
                TryPatchSetFocus(harmony);

                // Patch slider and arrow value changes
                TryPatchSwitchArrowSelectType(harmony);
                TryPatchSwitchSliderType(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchSetFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                var setFocusMethod = AccessTools.Method(typeof(ConfigCommandController), "SetFocus");
                if (setFocusMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ConfigMenuPatches), nameof(SetFocus_Postfix));
                    harmony.Patch(setFocusMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error patching SetFocus: {ex.Message}");
            }
        }

        private static void TryPatchSwitchArrowSelectType(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = AccessTools.Method(typeof(ConfigActualDetailsControllerBase_KeyInput), "SwitchArrowSelectTypeProcess");
                if (method != null)
                {
                    var postfix = AccessTools.Method(typeof(ConfigMenuPatches), nameof(SwitchArrowSelectType_Postfix));
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error patching SwitchArrowSelectType: {ex.Message}");
            }
        }

        private static void TryPatchSwitchSliderType(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = AccessTools.Method(typeof(ConfigActualDetailsControllerBase_KeyInput), "SwitchSliderTypeProcess");
                if (method != null)
                {
                    var postfix = AccessTools.Method(typeof(ConfigMenuPatches), nameof(SwitchSliderType_Postfix));
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error patching SwitchSliderType: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigCommandController.SetFocus - announces config option when navigating.
        /// </summary>
        public static void SetFocus_Postfix(ConfigCommandController __instance, bool isFocus)
        {
            try
            {
                // Set active state when config menu is in use
                if (isFocus)
                {
                    ConfigMenuState.SetActive();
                }

                // Only announce when gaining focus
                if (!isFocus)
                    return;

                if (__instance == null || !__instance.gameObject.activeInHierarchy)
                    return;

                // Note: Removed SelectedCommand verification check that failed with multiple
                // ConfigActualDetailsControllerBase instances (regular config + boost menu).
                // The isFocus parameter and activeInHierarchy check are sufficient.

                var view = __instance.view;
                if (view == null)
                    return;

                var nameText = view.NameText;
                if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                    return;

                string menuText = nameText.text.Trim();

                // Filter out template values
                if (!ConfigMenuReader.IsValidConfigValue(menuText))
                    return;

                // Get the current value
                string configValue = ConfigMenuReader.FindConfigValueFromController(__instance);

                string announcement = menuText;
                if (!string.IsNullOrWhiteSpace(configValue))
                {
                    announcement = $"{menuText}: {configValue}";
                }

                // Check for duplicates
                bool isValueChangeOnly;
                if (!ConfigMenuState.ShouldAnnounce(announcement, out isValueChangeOnly))
                    return;

                // If only value changed (same setting), don't announce here
                // The SwitchArrowSelectType_Postfix and SwitchSliderType_Postfix patches handle value changes
                if (isValueChangeOnly)
                    return;

                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error in SetFocus patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SwitchArrowSelectTypeProcess - announces when toggle values change.
        /// </summary>
        public static void SwitchArrowSelectType_Postfix(
            ConfigActualDetailsControllerBase_KeyInput __instance,
            ConfigCommandController controller)
        {
            try
            {
                if (controller == null || controller.view == null)
                    return;

                var view = controller.view;

                if (view.ArrowSelectTypeRoot != null && view.ArrowSelectTypeRoot.activeSelf)
                {
                    var texts = view.ArrowSelectTypeRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string textValue = text.text.Trim();
                            if (ConfigMenuReader.IsValidConfigValue(textValue))
                            {
                                if (textValue == lastArrowValue)
                                    return;

                                lastArrowValue = textValue;
                                FFII_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error in SwitchArrowSelectType patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SwitchSliderTypeProcess - announces when slider values change.
        /// </summary>
        public static void SwitchSliderType_Postfix(
            ConfigActualDetailsControllerBase_KeyInput __instance,
            ConfigCommandController controller)
        {
            try
            {
                if (controller == null || controller.view == null)
                    return;

                var view = controller.view;
                if (view.Slider == null)
                    return;

                string percentage = ConfigMenuReader.GetSliderPercentage(view.Slider);
                if (string.IsNullOrEmpty(percentage))
                    return;

                // Only announce if value changed for the SAME controller
                if (controller == lastController && percentage == lastSliderPercentage)
                    return;

                // If different controller, don't announce - let SetFocus handle it
                if (controller != lastController)
                {
                    lastController = controller;
                    lastSliderPercentage = percentage;
                    return;
                }

                lastSliderPercentage = percentage;

                FFII_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error in SwitchSliderType patch: {ex.Message}");
            }
        }
    }
}
