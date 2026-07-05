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
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewType = Il2CppLast.UI.CustomScrollView;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using ConfigKeysSettingController = Il2CppLast.UI.KeyInput.ConfigKeysSettingController;
using ConfigControllCommandController = Il2CppLast.UI.KeyInput.ConfigControllCommandController;
using ConfigKeyIconController = Il2CppLast.UI.KeyInput.ConfigKeyIconController;
using OptionController = Il2CppLast.UI.KeyInput.OptionController;

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
            _helper.RegisterResetHandler(() => { lastAnnouncedText = ""; lastAnnouncedSettingName = ""; });
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        public static bool ShouldSuppress() => IsActive;

        public static void ResetState() => _helper.IsActive = false;

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

                // Controls (keyboard/gamepad remap) navigation + assign flow, and the
                // title-screen language dropdown.
                TryPatchControlsAndLanguage(harmony);

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
        /// Builds the controls-screen announcement for one command: action name + keyboard binding
        /// (readable key names) + gamepad binding. The gamepad icon is an unreadable controller glyph,
        /// so we translate the LIVE bound button (from the screen's KeyConfigData) to family-aware text
        /// via ControllerLabels, falling back to nothing if that can't be resolved.
        /// Shared by the navigation read (SelectContent) and the rebind read (ChangeKeySetting).
        /// </summary>
        private static string BuildCommandAnnouncement(
            ConfigKeysSettingController owner,
            ConfigControllCommandController command)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            // Action name from the view's nameTexts
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

            // Keyboard binding — already readable key names.
            AppendIconTexts(textParts, command.keyboardIconController);

            // Gamepad binding — the icon is a sprite glyph carrying NO readable text (iconTextList is
            // empty), so reading it never worked. The keyboard and gamepad remap sections are mutually
            // exclusive per row: keyboard rows carry a key name, gamepad rows don't. So when the keyboard
            // icon is empty we're on the gamepad section — translate the LIVE bound button via
            // ControllerLabels (the keyboard binding above already handled keyboard-section rows).
            if (ResolveGamepadButtonText(owner, command) is string btn && !string.IsNullOrEmpty(btn)
                && !IconHasContent(command.keyboardIconController))
            {
                textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>True if an icon controller is currently showing readable binding text.</summary>
        private static bool IconHasContent(ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return false;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var t = iconView.iconTextList[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.text)) return true;
            }
            return false;
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
                    if (!textParts.Contains(text))
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
                FFII_ScreenReaderMod.SpeakText(gamepad ? "Press a button." : "Press a key.", interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in assign-prompt patch: {ex.Message}");
            }
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
