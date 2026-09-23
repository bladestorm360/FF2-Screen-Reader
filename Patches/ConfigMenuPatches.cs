using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP types
using ConfigCommandController = Il2CppLast.UI.KeyInput.ConfigCommandController;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewType = Il2CppLast.UI.CustomScrollView;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using ConfigKeysSettingController = Il2CppLast.UI.KeyInput.ConfigKeysSettingController;
using ConfigControllCommandController = Il2CppLast.UI.KeyInput.ConfigControllCommandController;
using ConfigKeyIconController = Il2CppLast.UI.KeyInput.ConfigKeyIconController;
using OptionController = Il2CppLast.UI.KeyInput.OptionController;
using ConfigController = Il2CppLast.UI.KeyInput.ConfigController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracking for config menu.
    /// </summary>
    public static class ConfigMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.CONFIG_MENU);

        private static string lastAnnouncedText = "";
        private static string lastAnnouncedSettingName = "";

        static ConfigMenuState()
        {
            _helper.RegisterResetHandler(ClearDedup);
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        public static bool ShouldSuppress() => IsActive;

        public static void ResetState() => _helper.IsActive = false;

        /// <summary>Forgets the last spoken row so the focused row speaks again on its next read.</summary>
        public static void ClearDedup()
        {
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

            string[] parts = announcement.Split(new[] { ": " }, 2, StringSplitOptions.None);
            if (parts.Length >= 1)
            {
                string settingName = parts[0];
                if (settingName == lastAnnouncedSettingName && parts.Length > 1)
                {
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
        // Self-contained Language(enum int) → display name. The game's own GetLanguageMessage returns
        // EMPTY for the CURRENT language (it blanks the current item's label), so we map here instead.
        // Names match the game's English-name style observed in the language dropdown.
        private static readonly System.Collections.Generic.Dictionary<int, string> LanguageNames = new()
        {
            { 1, "Japanese" }, { 2, "English" }, { 3, "French" }, { 4, "Italian" },
            { 5, "German" }, { 6, "Spanish" }, { 7, "Korean" }, { 8, "Chinese (Traditional)" },
            { 9, "Chinese (Simplified)" }, { 10, "Russian" }, { 11, "Thai" }, { 12, "Brazilian Portuguese" },
        };

        /// <summary>
        /// Current game language display name, mapped from MessageManager.currentLanguage (the same
        /// source EntityTranslator uses). Used for the Language config row and the open dropdown's
        /// current item (whose LabelText is blank). Deliberately does NOT use
        /// LangugeUtility.GetLanguageMessage, which returns empty for the current language.
        /// </summary>
        public static string GetCurrentLanguageDisplayName()
        {
            try
            {
                var mgr = Il2CppLast.Management.MessageManager.Instance;
                if (mgr == null) return null;
                int langId = (int)mgr.currentLanguage;
                return LanguageNames.TryGetValue(langId, out string name) ? name : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] GetCurrentLanguageDisplayName failed: {ex.Message}");
                return null;
            }
        }

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

                // Language row: its dropdown OptionData text is empty while the popup is closed, so read
                // the current language straight from the game → "Language: <current>". Identified by the
                // command's config type (not a localized string match).
                var cmdData = controller.ConfigCommandsData;
                if (cmdData != null && cmdData.ConfigCommandType == Il2CppLast.UI.ConfigCommandType.Language)
                {
                    string lang = GetCurrentLanguageDisplayName();
                    if (!string.IsNullOrEmpty(lang))
                        return lang;
                }

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
            catch { }

            return null;
        }

        /// <summary>
        /// Formats a slider value for announcement. Small-range integer sliders
        /// (BGM/SFX, max-min <= 10) return the raw value since the on-screen UI
        /// already shows it as an integer. Larger sliders (Master Volume, Brightness)
        /// keep percentage conversion since their on-screen UI shows a percentage.
        /// </summary>
        public static string GetSliderPercentage(UnityEngine.UI.Slider slider)
        {
            if (slider == null) return null;

            float min = slider.minValue;
            float max = slider.maxValue;
            float current = slider.value;

            float range = max - min;
            if (range <= 0) return "0%";

            // Raw integer for small-range sliders (e.g., BGM/SFX volume shown as 1-10)
            if (range <= 10)
                return ((int)Math.Round(current)).ToString();

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
        // Last arrow value spoken, per row: a different row reaching the same value text ("On") must
        // still speak, so the value guard is keyed on the row's native pointer.
        private static IntPtr lastArrowRow = IntPtr.Zero;
        private static string lastArrowValue = "";
        private static string lastSliderPercentage = "";
        private static ConfigCommandController lastController = null;

        // One-shot re-announce of the focused row, armed when the config menu regains focus from a
        // context that does not re-focus a row: returning from the config bestiary (GameStatePatches),
        // closing a popup opened over the config menu (PopupPatches) or opening the title
        // Configuration (ShowConfig). The arming event starts a deferred read (one frame later,
        // bounded retry) of the open config menu's focused row; cleared when the config menu closes
        // so it can't leak into the next open. No per-frame hook (CLAUDE.md rule 3).
        private static bool _pendingConfigReannounce = false;
        // Bumped on every arm; an older deferred read exits when a newer arm superseded it.
        private static int _reannounceGen = 0;
        // Retry cap for the deferred read (~2 s at 60 fps) while the focused row isn't readable yet.
        private const int MAX_REANNOUNCE_FRAMES = 120;

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

                // Controls (keyboard/gamepad remap) navigation + assign flow, and the
                // title-screen language dropdown.
                TryPatchControlsAndLanguage(harmony);

                // Focused-row re-announce (bestiary return, popup cancel, title Configuration open).
                TryPatchReannounce(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches the keyboard/gamepad remap sub-screen (ConfigKeysSettingController) navigation and
        /// assign flow, plus the title-screen language dropdown (OptionController.SetDropDownItemFocus).
        /// </summary>
        private static void TryPatchControlsAndLanguage(HarmonyLib.Harmony harmony)
        {
            // Controls navigation: ConfigKeysSettingController.SelectContent has TWO overloads (the
            // 5-arg navigation method and a 2-arg variant), so AccessTools.Method without an explicit
            // Type[] throws AmbiguousMatchException — disambiguate to the navigation overload.
            try
            {
                var selectContentMethod = AccessTools.Method(
                    typeof(ConfigKeysSettingController), "SelectContent",
                    new Type[]
                    {
                        typeof(int),
                        typeof(CustomScrollViewType),
                        typeof(GameCursor),
                        typeof(Il2CppSystem.Collections.Generic.IEnumerable<ConfigControllCommandController>),
                        typeof(CustomScrollViewWithinRangeType)
                    });
                if (selectContentMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ConfigMenuPatches), nameof(SelectContent_Postfix));
                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Config Menu] ConfigKeysSettingController.SelectContent not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error applying controls patch: {ex.Message}");
            }

            // Title-screen Language dropdown (keyboard/gamepad uses a Unity Dropdown driven by the
            // KeyInput OptionController). SetDropDownItemFocus is the discrete, event-driven hook —
            // it announces the focused language directly (no dedup), gated on the config menu being open.
            // DO NOT hook OptionController.UpdateSelectLanguage — it is an EMPTY method whose body is
            // the shared 0x2698F0 stub; detouring it corrupts the others → launch crash.
            void PatchOption(string method, string postfixName)
            {
                try
                {
                    var m = AccessTools.Method(typeof(OptionController), method);
                    if (m != null)
                        harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), postfixName)));
                    else
                        MelonLogger.Warning($"[Config Menu] OptionController.{method} not found");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Config Menu] Error patching OptionController.{method}: {ex.Message}");
                }
            }

            PatchOption("SetDropDownItemFocus", nameof(SetDropDownItemFocus_Postfix));

            // Remap assign-flow speaking (ConfigKeysSettingController, all real-bodied methods).
            // KeyboardSettingInit / GamePadSettingInit fire on entering assign mode → "press a
            // key/button" prompt. ChangeKeySetting (overloaded keyboard + gamepad) fires when the
            // binding is applied → announce the new mapping.
            void PatchKeysSetting(string method, string postfixName)
            {
                try
                {
                    var m = AccessTools.Method(typeof(ConfigKeysSettingController), method);
                    if (m != null)
                        harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), postfixName)));
                    else
                        MelonLogger.Warning($"[Config Menu] ConfigKeysSettingController.{method} not found");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Config Menu] Error patching ConfigKeysSettingController.{method}: {ex.Message}");
                }
            }

            PatchKeysSetting("KeyboardSettingInit", nameof(KeyboardSettingInit_Postfix));
            PatchKeysSetting("GamePadSettingInit", nameof(GamePadSettingInit_Postfix));

            // Gamepad/Keyboard "Controls" pop-up (read-only list of every control) → navigation list.
            // Entering the GamePad/Keyboard Help state shows helpContentList/keyboardHelpContentList
            // (dump: 0x58/0x60); render it once and hand it to KeyHelpReader so arrows/W/S step it.
            PatchKeysSetting("GamePadHelpInit", nameof(GamePadHelpInit_Postfix));
            PatchKeysSetting("KeyboardHelpInit", nameof(KeyboardHelpInit_Postfix));
            // Leaving the help state (back to the select list, or closing the controls screen) clears it.
            PatchKeysSetting("GamePadSelectInit", nameof(ControlsHelpClose_Postfix));
            PatchKeysSetting("KeyboardSelectInit", nameof(ControlsHelpClose_Postfix));
            PatchKeysSetting("Close", nameof(ControlsHelpClose_Postfix));

            // ChangeKeySetting is overloaded — patch every overload with the same __instance-only
            // postfix (avoids AmbiguousMatchException without needing an exact Type[]).
            try
            {
                var changePostfix = new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), nameof(ChangeKeySetting_Postfix)));
                foreach (var m in typeof(ConfigKeysSettingController).GetMethods(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (m.Name == "ChangeKeySetting")
                        harmony.Patch(m, postfix: changePostfix);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching ChangeKeySetting: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches OptionController.ShowConfig (unique RVA 0x2FD2D0), which opens the title
        /// Configuration screen and arms the focused-row re-announce. The other arming events
        /// (bestiary return, popup close) live in GameStatePatches / PopupPatches.
        /// </summary>
        private static void TryPatchReannounce(HarmonyLib.Harmony harmony)
        {
            try
            {
                var showConfig = AccessTools.Method(typeof(OptionController), "ShowConfig", Type.EmptyTypes);
                if (showConfig != null)
                    harmony.Patch(showConfig,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), nameof(ShowConfig_Prefix))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), nameof(ShowConfig_Postfix))));
                else
                    MelonLogger.Warning("[Config Menu] OptionController.ShowConfig not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching re-announce hooks: {ex.Message}");
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
                MelonLogger.Error($"[Config Menu] Error patching SetFocus: {ex.Message}");
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
                MelonLogger.Error($"[Config Menu] Error patching SwitchArrowSelectType: {ex.Message}");
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
                MelonLogger.Error($"[Config Menu] Error patching SwitchSliderType: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigCommandController.SetFocus - announces config option when navigating.
        /// </summary>
        public static void SetFocus_Postfix(ConfigCommandController __instance, bool isFocus)
        {
            try
            {
                // Only announce when gaining focus
                if (!isFocus)
                    return;

                // Set active state when config menu is in use
                ConfigMenuState.SetActive();

                if (__instance == null || !__instance.gameObject.activeInHierarchy)
                    return;

                // Note: Removed SelectedCommand verification check that failed with multiple
                // ConfigActualDetailsControllerBase instances (regular config + boost menu).
                // The isFocus parameter and activeInHierarchy check are sufficient; the owning
                // details controller (for the row position) is the row's nearest parent.
                AnnounceCommand(__instance, __instance.GetComponentInParent<ConfigActualDetailsControllerBase_KeyInput>());
            }
            catch { }
        }

        /// <summary>
        /// Speaks a focused config row as "Name: Value, (X of Y)" unless it is the row last spoken
        /// (SetFocus can re-fire for the focused row) or only its value changed (the arrow/slider
        /// postfixes speak value changes). Returns false only when the row isn't readable yet, so the
        /// one-shot re-announce consumer retries next frame; true once spoken or already spoken.
        /// </summary>
        private static bool AnnounceCommand(ConfigCommandController command, ConfigActualDetailsControllerBase_KeyInput details)
        {
            var view = command.view;
            if (view == null)
                return false;

            var nameText = view.NameText;
            if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                return false;

            string menuText = nameText.text.Trim();

            // Filter out template values
            if (!ConfigMenuReader.IsValidConfigValue(menuText))
                return false;

            // Get the current value
            string configValue = ConfigMenuReader.FindConfigValueFromController(command);

            string announcement = menuText;
            if (!string.IsNullOrWhiteSpace(configValue))
            {
                announcement = $"{menuText}: {configValue}";
            }

            // Check for duplicates; a value-only change is spoken by the arrow/slider postfixes.
            if (!ConfigMenuState.ShouldAnnounce(announcement, out bool isValueChangeOnly) || isValueChangeOnly)
                return true;

            // Row position within the details controller's command list (best-effort: -1 → no suffix).
            int index = -1, count = -1;
            try
            {
                var list = details?.CommandList;
                if (list != null)
                {
                    count = list.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var c = list[i];
                        if (c != null && c.Pointer == command.Pointer) { index = i; break; }
                    }
                }
            }
            catch { }

            FFII_ScreenReaderMod.SpeakText(MenuPosition.Format(announcement, index, count), interrupt: true);
            return true;
        }

        /// <summary>
        /// Arms the one-shot re-announce of the focused config row (see _pendingConfigReannounce) and
        /// starts its deferred read. Callers make sure the dedup no longer holds that row, so whichever
        /// of SetFocus or the deferred read reads it first speaks and the other is deduplicated.
        /// <paramref name="option"/> is the title OptionController when the caller has it (ShowConfig);
        /// otherwise the open config menu is looked up when the read runs.
        /// </summary>
        public static void ReannounceFocusedConfigOption(OptionController option = null)
        {
            _pendingConfigReannounce = true;
            int gen = ++_reannounceGen;
            try { CoroutineManager.StartManaged(DeferredReannounce(option, gen)); }
            catch (Exception ex) { MelonLogger.Warning($"[Config Menu] Error scheduling focused-row read: {ex.Message}"); }
        }

        /// <summary>Drops a pending re-announce (config menu closed).</summary>
        public static void CancelReannounce() => _pendingConfigReannounce = false;

        /// <summary>
        /// Reads the focused row one frame after the arming event, retrying each frame (capped) while
        /// no config menu is open or its focused row isn't readable yet. Stops as soon as the row is
        /// spoken, the re-announce is cancelled (config menu closed) or a newer arm superseded it.
        /// </summary>
        private static IEnumerator DeferredReannounce(OptionController option, int gen)
        {
            for (int frame = 0; frame < MAX_REANNOUNCE_FRAMES; frame++)
            {
                yield return null; // yield stays outside the try (yield-in-try-with-catch is illegal)

                if (!_pendingConfigReannounce || gen != _reannounceGen)
                    yield break;

                if (TryConsumeReannounce(FindOpenDetailsController(option)))
                {
                    _pendingConfigReannounce = false;
                    yield break;
                }
            }

            if (gen == _reannounceGen)
                _pendingConfigReannounce = false;
        }

        /// <summary>
        /// Details controller of the open config menu: the given title OptionController, else the
        /// active in-game ConfigController (detailsController, dump.cs:446198 @0x48), else the active
        /// title OptionController (configActualDetailsController, dump.cs:456168 @0xA0). Null while no
        /// config menu is open, so an exit that really left the menu stays silent.
        /// </summary>
        private static ConfigActualDetailsControllerBase_KeyInput FindOpenDetailsController(OptionController option)
        {
            try
            {
                if (option == null)
                {
                    var config = UnityEngine.Object.FindObjectOfType<ConfigController>();
                    if (config != null && config.gameObject.activeInHierarchy)
                        return config.detailsController;
                    option = UnityEngine.Object.FindObjectOfType<OptionController>();
                }
                if (option != null && option.gameObject.activeInHierarchy)
                    return option.configActualDetailsController;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Speaks the given details controller's focused row. Returns true once spoken (or already
        /// spoken); false while it isn't readable yet, so the deferred read retries next frame.
        /// </summary>
        private static bool TryConsumeReannounce(ConfigActualDetailsControllerBase_KeyInput details)
        {
            try
            {
                var selected = details?.SelectedCommand;
                if (selected == null || !selected.gameObject.activeInHierarchy)
                    return false; // not ready yet — retry next frame

                ConfigMenuState.SetActive();
                return AnnounceCommand(selected, details);
            }
            catch
            {
                return false; // best-effort; retry next frame
            }
        }

        /// <summary>
        /// Title Configuration opened: forget any row spoken before (prefix) and arm the focused-row
        /// read (postfix), so the initial row speaks exactly once whether or not ShowConfig's own
        /// SetFocus fires.
        /// </summary>
        public static void ShowConfig_Prefix() => ConfigMenuState.ClearDedup();

        public static void ShowConfig_Postfix(OptionController __instance)
        {
            try
            {
                ReannounceFocusedConfigOption(__instance);
            }
            catch { }
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
                                if (controller.Pointer == lastArrowRow && textValue == lastArrowValue)
                                    return;

                                lastArrowRow = controller.Pointer;
                                lastArrowValue = textValue;
                                FFII_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
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
            catch { }
        }

        // ── Controls (keyboard/gamepad remap) navigation ────────────────────────────────

        /// <summary>
        /// Postfix for ConfigKeysSettingController.SelectContent.
        /// Announces action name and key bindings when navigating controls settings.
        /// </summary>
        public static void SelectContent_Postfix(
            ConfigKeysSettingController __instance,
            int index,
            Il2CppSystem.Collections.Generic.IEnumerable<ConfigControllCommandController> contentList)
        {
            try
            {
                if (__instance == null || contentList == null) return;

                var list = contentList.TryCast<Il2CppSystem.Collections.Generic.List<ConfigControllCommandController>>();
                if (list == null || index < 0 || index >= list.Count) return;
                var command = list[index];

                string announcement = BuildCommandAnnouncement(__instance, command);
                if (string.IsNullOrWhiteSpace(announcement)) return;

                announcement = MenuPosition.Format(announcement, index, list.Count);
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigKeysSettingController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the controls-screen announcement for one command: action name + binding. Keyboard rows
        /// carry readable key names; mouse rows and gamepad rows render glyphs, so those are translated
        /// (mouse sprite → text; gamepad → the LIVE bound button via ControllerLabels, falling back to
        /// the rendered glyph for the fixed, non-remappable buttons). Shared by the navigation read
        /// (SelectContent), the rebind read (ChangeKeySetting) and the Controls help list.
        /// </summary>
        private static string BuildCommandAnnouncement(
            ConfigKeysSettingController owner,
            ConfigControllCommandController command,
            bool isHelpList = false)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            AppendCommandName(textParts, command, isHelpList);

            if (command.IsMouseKey)
            {
                string mouse = ResolveMouseButtonText(command);
                if (!string.IsNullOrEmpty(mouse))
                    textParts.Add($"({mouse})");
            }
            else
            {
                AppendIconTexts(textParts, command.keyboardIconController);
            }

            // Gamepad binding — only when this row actually shows a gamepad binding icon
            // (gamePadIconsRoot active), which excludes keyboard-section rows and non-binding rows.
            var gpRoot = command.view != null ? command.view.gamePadIconsRoot : null;
            if (gpRoot != null && gpRoot.activeSelf)
            {
                // Face buttons are remappable → the LIVE binding; the fixed buttons (shoulders,
                // triggers, sticks, Start, movement) aren't in the remap dictionary → the rendered glyph.
                string btn = ResolveGamepadButtonText(owner, command);
                if (string.IsNullOrEmpty(btn))
                    btn = GetGamepadGlyphLabel(command);
                if (!string.IsNullOrEmpty(btn))
                    textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>
        /// Appends a command's action name. Remap rows carry it in the view's nameTexts; Controls help
        /// rows leave nameTexts as a "New Text" placeholder and render the name into the controller's
        /// messageTexts instead (falling back to resolving MessageId).
        /// </summary>
        private static void AppendCommandName(
            System.Collections.Generic.List<string> textParts,
            ConfigControllCommandController command,
            bool isHelpList)
        {
            if (isHelpList)
            {
                var msgTexts = command.messageTexts;
                if (msgTexts != null)
                {
                    for (int i = 0; i < msgTexts.Count; i++)
                    {
                        var t = msgTexts[i];
                        if (t != null && IsRealName(t.text))
                        {
                            string s = t.text.Trim();
                            if (!textParts.Contains(s)) textParts.Add(s);
                        }
                    }
                }
                if (textParts.Count == 0)
                {
                    string loc = TryGetMessage(command.MessageId);
                    if (IsRealName(loc)) textParts.Add(loc.Trim());
                }
                return;
            }

            if (command.view != null && command.view.nameTexts != null && command.view.nameTexts.Count > 0)
            {
                foreach (var textComp in command.view.nameTexts)
                {
                    if (textComp != null && !string.IsNullOrWhiteSpace(textComp.text))
                    {
                        string text = textComp.text.Trim();
                        if (!text.StartsWith("MENU_") && !textParts.Contains(text))
                            textParts.Add(text);
                    }
                }
            }
        }

        /// <summary>True if the text is a usable name (not blank or an editor placeholder).</summary>
        private static bool IsRealName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            return t != "New Text" && t != "NewText" && t != "Text" && t != "Name" && t != "Label";
        }

        /// <summary>Resolves a message id to its localized text via the game's MessageManager.</summary>
        private static string TryGetMessage(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return null;
            try
            {
                var mm = Il2CppLast.Management.MessageManager.Instance;
                string text = mm?.GetMessage(messageId, false);
                return string.IsNullOrWhiteSpace(text) ? null : TextUtils.StripIconMarkup(text);
            }
            catch { return null; }
        }

        /// <summary>
        /// Reads a row's rendered gamepad glyph (under view.gamePadIconsRoot) and maps it to a
        /// controller-aware label. Used only for the fixed buttons the live remap read can't resolve.
        /// </summary>
        private static string GetGamepadGlyphLabel(ConfigControllCommandController command)
        {
            try
            {
                var gpRoot = command?.view != null ? command.view.gamePadIconsRoot : null;
                if (gpRoot == null || !gpRoot.activeSelf) return null;
                var images = gpRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                if (images == null) return null;
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
                    if (img == null || !img.gameObject.activeInHierarchy || img.sprite == null) continue;
                    string label = GamepadGlyphSpriteToLabel(img.sprite.name);
                    if (!string.IsNullOrEmpty(label)) return label;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Maps a controls-screen glyph sprite name ("UI_Common_&lt;Button&gt;button01") to a label for the
        /// FIXED buttons only; the remappable face buttons and unknowns return null so they are never
        /// locked to a static glyph.
        /// </summary>
        private static string GamepadGlyphSpriteToLabel(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            if (Has(spriteName, "LBbutton")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER);
            if (Has(spriteName, "RBbutton")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER);
            if (Has(spriteName, "LTbutton")) return ControllerLabels.GetLeftTriggerLabel();
            if (Has(spriteName, "RTbutton")) return ControllerLabels.GetRightTriggerLabel();
            // Stick clicks are phrased as a click ("L3"), not "LS", which sounds like moving the stick.
            if (Has(spriteName, "L3button")) return ControllerLabels.GetLeftStickClickLabel();
            if (Has(spriteName, "R3button")) return ControllerLabels.GetRightStickClickLabel();
            // The Menu (Start) button opens the mod menu and can't be remapped.
            if (Has(spriteName, "Menubutton")) return T("used for mod menu");
            if (Has(spriteName, "Backbutton") || Has(spriteName, "Selectbutton") || Has(spriteName, "Viewbutton"))
                return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_BACK);
            // The movement glyph: the mod repurposes the D-pad, so only the left stick moves the character.
            if (Has(spriteName, "Tenkeybutton") || Has(spriteName, "Dpadbutton")
                || Has(spriteName, "Crossbutton") || Has(spriteName, "Directionbutton"))
                return T("Left Stick");

            return null;
        }

        private static bool Has(string s, string token)
            => s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Resolves a mouse row's bound button/scroll from its rendered glyph sprite (under
        /// keyboardIconsRoot), which tracks the live binding. Null on failure.
        /// </summary>
        private static string ResolveMouseButtonText(ConfigControllCommandController command)
        {
            try
            {
                var root = command.view != null ? command.view.keyboardIconsRoot : null;
                UnityEngine.UI.Image[] imgs = root != null
                    ? root.GetComponentsInChildren<UnityEngine.UI.Image>(true)
                    : (command.gameObject != null ? command.gameObject.GetComponentsInChildren<UnityEngine.UI.Image>(true) : null);
                if (imgs == null) return null;
                for (int i = 0; i < imgs.Length; i++)
                {
                    var img = imgs[i];
                    if (img == null || !img.gameObject.activeInHierarchy || img.sprite == null) continue;
                    string label = MouseSpriteToLabel(img.sprite.name);
                    if (!string.IsNullOrEmpty(label)) return label;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Maps a mouse glyph sprite name to text. "mouse_rad" (wheel) contains "mouse_r", so the wheel
        /// is checked before the right button.
        /// </summary>
        private static string MouseSpriteToLabel(string s)
        {
            if (string.IsNullOrEmpty(s) || !Has(s, "mouse")) return null;
            if (Has(s, "mouse_rad") || Has(s, "wheel") || Has(s, "scroll")) return T("Mouse Wheel");
            if (Has(s, "mouse_l")) return T("Left Mouse Button");
            if (Has(s, "mouse_r")) return T("Right Mouse Button");
            if (Has(s, "mouse_c") || Has(s, "mouse_m")) return T("Middle Mouse Button");
            return T("Mouse Button");
        }

        /// <summary>
        /// Resolves a remap row's CURRENT (remappable) gamepad button to family-aware text. Reads the
        /// live binding from the screen's KeyConfigData (GameKey → Unity KeyCode), maps the KeyCode to
        /// an SDL button index, and lets ControllerLabels pick the text for the connected controller.
        /// Returns null if it can't be resolved.
        /// </summary>
        private static string ResolveGamepadButtonText(
            ConfigKeysSettingController owner,
            ConfigControllCommandController command)
        {
            try
            {
                if (owner == null || command == null) return null;
                var kd = owner.keydata;
                if (kd == null) return null;
                var dict = kd.GetGamePadKeyConfigtDictionary();
                if (dict == null || !dict.ContainsKey(command.key)) return null;
                int sdl = JoystickKeyCodeToSdlButton((int)dict[command.key]);
                if (sdl < 0) return null;
                return ControllerLabels.GetButtonLabel(sdl);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Maps a Unity legacy KeyCode.JoystickButtonN (330+) to an SDL gamepad button index using the
        /// XInput-standard layout (correct for Xbox + most PC controllers). ControllerLabels then yields
        /// the right family text for whichever controller is connected. Returns -1 if not a mapped button.
        /// </summary>
        private static int JoystickKeyCodeToSdlButton(int keyCode)
        {
            switch (keyCode)
            {
                // FFPR keeps the bottom/right face buttons in the Japanese arrangement: Confirm is
                // stored on JoystickButton1 and Cancel on JoystickButton0. The American build confirms
                // with the BOTTOM button (A/Cross), so JB1→SOUTH and JB0→EAST — i.e. swapped from
                // Unity's XInput default. (X/Y below are unaffected.)
                case 330: return SDL3.SDL_GAMEPAD_BUTTON_EAST;           // JoystickButton0 — Cancel (B/Circle)
                case 331: return SDL3.SDL_GAMEPAD_BUTTON_SOUTH;          // JoystickButton1 — Confirm (A/Cross)
                case 332: return SDL3.SDL_GAMEPAD_BUTTON_WEST;           // JoystickButton2 — X/Square
                case 333: return SDL3.SDL_GAMEPAD_BUTTON_NORTH;          // JoystickButton3 — Y/Triangle
                case 334: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER;  // JoystickButton4 — LB
                case 335: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER; // JoystickButton5 — RB
                case 336: return SDL3.SDL_GAMEPAD_BUTTON_BACK;           // JoystickButton6 — Back/View
                case 337: return SDL3.SDL_GAMEPAD_BUTTON_START;          // JoystickButton7 — Start/Menu
                case 338: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK;     // JoystickButton8 — LS
                case 339: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK;    // JoystickButton9 — RS
                default: return -1;
            }
        }

        /// <summary>Appends an icon controller's binding labels (iconTextList) to textParts, deduped.</summary>
        private static void AppendIconTexts(System.Collections.Generic.List<string> textParts, ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var iconText = iconView.iconTextList[i];
                if (iconText != null && !string.IsNullOrWhiteSpace(iconText.text))
                {
                    string text = iconText.text.Trim();
                    // Gamepad-help rows leave the keyboard icon text as a "New Text" placeholder; skip it.
                    if (IsRealName(text) && !textParts.Contains(text))
                        textParts.Add(text);
                }
            }
        }

        // ── Title-screen Language dropdown ──────────────────────────────────────────────
        // SetDropDownItemFocus is the EVENT-DRIVEN announce (no dedup), gated to when the config menu
        // is actually open so it cannot speak the current language at the title "Press any button."

        /// <summary>Focused language label: prefer the tracked dropdown item, else the dropdown value.</summary>
        private static string GetFocusedLanguageLabel(OptionController inst)
        {
            var item = inst.selectedItem;
            if (item == null) return null;

            if (item.view != null && item.view.LabelText != null)
            {
                string t = item.view.LabelText.text;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            var dd = inst.selectedDoropDown;
            if (dd != null && dd.options != null && dd.value >= 0 && dd.value < dd.options.Count)
            {
                var opt = dd.options[dd.value];
                if (opt != null && !string.IsNullOrWhiteSpace(opt.text)) return opt.text;
            }
            // The CURRENT language's item has an empty LabelText (its native name is a sprite; the
            // parenthetical English name is omitted for the current language). A focused item with no
            // readable label is therefore the current language → name it via MessageManager.
            return ConfigMenuReader.GetCurrentLanguageDisplayName();
        }

        /// <summary>
        /// EVENT-DRIVEN announce. OptionController.SetDropDownItemFocus (KeyInput) is the discrete
        /// "focused dropdown item changed" hook — speaks the focused language directly, NO dedup.
        /// </summary>
        public static void SetDropDownItemFocus_Postfix(OptionController __instance)
        {
            try
            {
                if (__instance == null) return;
                string label = GetFocusedLanguageLabel(__instance);
                // Gate on the config menu being genuinely OPEN — the title screen instantiates the
                // language OptionController during load and can fire SetDropDownItemFocus before the
                // menu is opened, which would speak the current language over "Press any button."
                if (!ConfigMenuState.IsActive) return;
                if (string.IsNullOrWhiteSpace(label)) return;
                FFII_ScreenReaderMod.SpeakText(label.Trim(), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetDropDownItemFocus patch: {ex.Message}");
            }
        }

        // ── Remap assign-flow (ConfigKeysSettingController) ──────────────────────────────
        // Entering assign mode → announce the "press a key/button" prompt. Init methods fire once on
        // state entry → event-driven, no dedup.
        public static void KeyboardSettingInit_Postfix(ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(__instance, gamepad: false);

        public static void GamePadSettingInit_Postfix(ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(__instance, gamepad: true);

        private static void AnnounceAssignPrompt(ConfigKeysSettingController inst, bool gamepad)
        {
            try
            {
                if (inst == null) return;
                FFII_ScreenReaderMod.SpeakText(gamepad ? T("Press a button.") : T("Press a key."), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in assign-prompt patch: {ex.Message}");
            }
        }

        // ── Gamepad/Keyboard Controls pop-up (read-only controls list) → navigation list ──

        public static void GamePadHelpInit_Postfix(ConfigKeysSettingController __instance)
            => OpenControlsHelp(__instance, gamepad: true);

        public static void KeyboardHelpInit_Postfix(ConfigKeysSettingController __instance)
            => OpenControlsHelp(__instance, gamepad: false);

        /// <summary>Returning to the controls list (Select state) or closing the screen tears the list down.</summary>
        public static void ControlsHelpClose_Postfix() => KeyHelpReader.CloseControlsHelp();

        private static void OpenControlsHelp(ConfigKeysSettingController inst, bool gamepad)
        {
            if (inst == null) return;
            // One-frame delay so each row's binding text is populated before it is rendered.
            CoroutineManager.StartManaged(DelayedOpenControlsHelp(inst, gamepad));
        }

        private static IEnumerator DelayedOpenControlsHelp(ConfigKeysSettingController inst, bool gamepad)
        {
            yield return null;
            System.Collections.Generic.List<string> entries = null;
            try
            {
                // The state machine can cycle its Help-state Init during scene construction while the
                // controls screen isn't shown — only build/announce when it's genuinely on-screen.
                if (inst == null || inst.gameObject == null || !inst.gameObject.activeInHierarchy)
                {
                    KeyHelpReader.CloseControlsHelp();
                }
                else
                {
                    var list = gamepad ? inst.HelpContentList : inst.KeyboardHelpContentList;
                    entries = new System.Collections.Generic.List<string>();
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            string ann = BuildCommandAnnouncement(inst, list[i], isHelpList: true);
                            if (!string.IsNullOrWhiteSpace(ann)) entries.Add(ann);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading controls help list: {ex.Message}");
            }
            if (entries != null && entries.Count > 0)
                KeyHelpReader.OpenControlsHelp(inst, entries);
        }

        /// <summary>
        /// Postfix for ConfigKeysSettingController.ChangeKeySetting (all overloads). Fires when a
        /// binding is applied — re-reads the just-edited command and announces the new mapping.
        /// Event-driven, no dedup.
        /// </summary>
        public static void ChangeKeySetting_Postfix(ConfigKeysSettingController __instance)
        {
            try
            {
                if (__instance == null) return;
                string announcement = BuildCommandAnnouncement(__instance, __instance.selectedCommand);
                if (string.IsNullOrWhiteSpace(announcement)) return;
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ChangeKeySetting patch: {ex.Message}");
            }
        }
    }
}
