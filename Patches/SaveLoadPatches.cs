using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

// FF2 Save/Load UI types
// All controllers use SavePopup with messageText at 0x40, commandList at 0x60
using TitleLoadController = Il2CppLast.UI.KeyInput.LoadGameWindowController;  // Title screen load (savePopup at 0x58)
using MainMenuLoadController = Il2CppLast.UI.KeyInput.LoadWindowController;   // Main menu load (savePopup at 0x28)
using MainMenuSaveController = Il2CppLast.UI.KeyInput.SaveWindowController;   // Main menu save (savePopup at 0x28)
using InterruptionController = Il2CppLast.UI.KeyInput.InterruptionWindowController;  // QuickSave (savePopup at 0x38)
using KeyInputSavePopup = Il2CppLast.UI.KeyInput.SavePopup;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks save/load menu state for suppression.
    /// </summary>
    public static class SaveLoadMenuState
    {
        /// <summary>
        /// True when save/load menu is active.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.SAVE_LOAD_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.SAVE_LOAD_MENU, value);
        }
        public static bool IsInConfirmation { get; set; } = false;

        public static bool ShouldSuppress()
        {
            return IsActive && IsInConfirmation;
        }

        public static void ResetState()
        {
            IsActive = false;
            IsInConfirmation = false;
        }
    }

    /// <summary>
    /// Patches for Save/Load confirmation popups.
    ///
    /// Hooks SetPopupActive(bool isEnable) on three controllers:
    /// - LoadGameWindowController (title screen load)
    /// - LoadWindowController (main menu load)
    /// - SaveWindowController (main menu save)
    ///
    /// Hooks SetEnablePopup(bool isEnable) on:
    /// - InterruptionWindowController (QuickSave)
    ///
    /// Hooks UpdateFocus() on SavePopup to read Yes/No buttons.
    ///
    /// All use SavePopup with messageText at 0x40, commandList at 0x60.
    /// </summary>
    public static class SaveLoadPatches
    {
        // SavePopup field offsets (from dump.cs line 453803)
        private const int SAVE_POPUP_MESSAGE_TEXT_OFFSET = 0x40;  // Text messageText
        private const int SAVE_POPUP_SELECT_CURSOR_OFFSET = 0x58; // Cursor selectCursor
        private const int SAVE_POPUP_COMMAND_LIST_OFFSET = 0x60;  // List<CommonCommand> commandList

        // CommonCommand field offset (from dump.cs line 429974)
        private const int COMMON_COMMAND_TEXT_OFFSET = 0x18;      // Text text

        // Controller-specific savePopup field offsets (verified from FF2 dump.cs)
        private const int TITLE_LOAD_SAVE_POPUP_OFFSET = 0x58;   // LoadGameWindowController.savePopup
        private const int MAIN_MENU_SAVE_POPUP_OFFSET = 0x28;    // Both LoadWindowController and SaveWindowController
        private const int INTERRUPTION_SAVE_POPUP_OFFSET = 0x38; // InterruptionWindowController.savePopup (QuickSave)

        // Track last announced button to avoid duplicates
        private static int lastAnnouncedButtonIndex = -1;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SetPopupActive(bool) on save/load controllers
                TryPatchTitleLoad(harmony);
                TryPatchMainMenuLoad(harmony);
                TryPatchMainMenuSave(harmony);

                // Patch SetEnablePopup(bool) on QuickSave controller
                TryPatchInterruption(harmony);

                // Patch SavePopup.UpdateFocus for button reading
                TryPatchSavePopupUpdateFocus(harmony);

                MelonLogger.Msg("[SaveLoad] All save/load patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SaveLoad] Failed to apply patches: {ex.Message}");
            }
        }

        /// <summary>
        /// NOTE: SavePopup does NOT have an UpdateFocus method (verified in dump.cs).
        /// Button reading for save popups must use cursor navigation patches instead.
        /// This method is disabled to prevent patching the wrong method.
        /// </summary>
        private static void TryPatchSavePopupUpdateFocus(HarmonyLib.Harmony harmony)
        {
            // DISABLED: SavePopup.UpdateFocus doesn't exist in FF2
            // The previous implementation used AccessTools.Method which may have found
            // a method from a parent class (MonoBehaviour) causing crashes.
            //
            // SavePopup button navigation should be handled through:
            // 1. Cursor.NextIndex/PrevIndex patches (already in place)
            // 2. Checking PopupState.IsConfirmationPopupActive in cursor patches
            MelonLogger.Msg("[SaveLoad] SavePopup.UpdateFocus patch disabled (method doesn't exist in FF2)");
        }

        /// <summary>
        /// Patches LoadGameWindowController.SetPopupActive (title screen load).
        /// </summary>
        private static void TryPatchTitleLoad(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(TitleLoadController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(TitleLoadSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[SaveLoad] Patched TitleLoadController.SetPopupActive");
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] TitleLoadController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch TitleLoadController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadWindowController.SetPopupActive (main menu load).
        /// </summary>
        private static void TryPatchMainMenuLoad(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuLoadController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuLoadSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[SaveLoad] Patched MainMenuLoadController.SetPopupActive");
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuLoadController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuLoadController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SaveWindowController.SetPopupActive (main menu save).
        /// </summary>
        private static void TryPatchMainMenuSave(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuSaveController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuSaveSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[SaveLoad] Patched MainMenuSaveController.SetPopupActive");
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuSaveController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuSaveController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches InterruptionWindowController.SetEnablePopup (QuickSave).
        /// </summary>
        private static void TryPatchInterruption(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(InterruptionController);
                var method = AccessTools.Method(controllerType, "SetEnablePopup");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(InterruptionSetEnablePopup_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[SaveLoad] Patched InterruptionController.SetEnablePopup (QuickSave)");
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] InterruptionController.SetEnablePopup not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch InterruptionController: {ex.Message}");
            }
        }

        // ============ Postfix Methods ============

        /// <summary>
        /// Postfix for SavePopup.UpdateFocus - reads and announces current button.
        /// </summary>
        public static void SavePopup_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputSavePopup;
                if (popup == null) return;

                // Safety check: verify popup GameObject is still valid and active
                // This prevents crashes during scene transitions when objects are being destroyed
                try
                {
                    var gameObj = popup.gameObject;
                    if (gameObj == null || !gameObj.activeInHierarchy)
                        return;
                }
                catch
                {
                    // GameObject access failed - popup is being destroyed
                    return;
                }

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                // Read selectCursor at offset 0x58
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + SAVE_POPUP_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero) return;

                GameCursor cursor;
                try
                {
                    cursor = new GameCursor(cursorPtr);
                }
                catch
                {
                    // Cursor creation failed - pointer is invalid
                    return;
                }

                int cursorIndex = cursor.Index;

                // Skip if same button as last announced
                if (cursorIndex == lastAnnouncedButtonIndex)
                    return;

                lastAnnouncedButtonIndex = cursorIndex;

                // Read commandList at offset 0x60
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + SAVE_POPUP_COMMAND_LIST_OFFSET);
                if (listPtr == IntPtr.Zero) return;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size) return;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return;

                // Read text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    MelonLogger.Msg($"[SaveLoad] Popup button: {buttonText}");
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in UpdateFocus postfix: {ex.Message}");
            }
        }

        public static void TitleLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                MelonLogger.Msg($"[SaveLoad] TitleLoad.SetPopupActive called with isEnable={isEnable}");

                if (isEnable)
                {
                    var controller = __instance as TitleLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, TITLE_LOAD_SAVE_POPUP_OFFSET, "TitleLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in TitleLoadSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void MainMenuLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                MelonLogger.Msg($"[SaveLoad] MainMenuLoad.SetPopupActive called with isEnable={isEnable}");

                if (isEnable)
                {
                    var controller = __instance as MainMenuLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, MAIN_MENU_SAVE_POPUP_OFFSET, "MainMenuLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuLoadSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void MainMenuSaveSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                MelonLogger.Msg($"[SaveLoad] MainMenuSave.SetPopupActive called with isEnable={isEnable}");

                if (isEnable)
                {
                    var controller = __instance as MainMenuSaveController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, MAIN_MENU_SAVE_POPUP_OFFSET, "MainMenuSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuSaveSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void InterruptionSetEnablePopup_Postfix(object __instance, bool isEnable)
        {
            try
            {
                MelonLogger.Msg($"[SaveLoad] Interruption.SetEnablePopup called with isEnable={isEnable}");

                if (isEnable)
                {
                    var controller = __instance as InterruptionController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, INTERRUPTION_SAVE_POPUP_OFFSET, "QuickSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in InterruptionSetEnablePopup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a coroutine to read SavePopup message after a short delay.
        /// The delay allows the UI to populate the text before we read it.
        /// </summary>
        private static void ReadSavePopup(IntPtr controllerPtr, int savePopupOffset, string context)
        {
            if (controllerPtr == IntPtr.Zero)
            {
                MelonLogger.Warning($"[SaveLoad] {context}: Controller pointer is null");
                return;
            }

            try
            {
                unsafe
                {
                    // Read savePopup pointer from controller
                    IntPtr popupPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + savePopupOffset);
                    if (popupPtr == IntPtr.Zero)
                    {
                        MelonLogger.Warning($"[SaveLoad] {context}: SavePopup pointer is null");
                        return;
                    }

                    MelonLogger.Msg($"[SaveLoad] {context}: SavePopup at 0x{popupPtr.ToInt64():X}");

                    // Set state for button navigation immediately
                    SaveLoadMenuState.IsActive = true;
                    SaveLoadMenuState.IsInConfirmation = true;
                    PopupState.SetActive($"{context}Popup", popupPtr, SAVE_POPUP_COMMAND_LIST_OFFSET);
                    lastAnnouncedButtonIndex = -1;  // Reset button tracking for new popup

                    // Start coroutine to read text after delay (allows UI to populate)
                    CoroutineManager.StartManaged(ReadPopupTextDelayed(popupPtr, context));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading {context} popup: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that waits a frame then reads the popup text.
        /// </summary>
        private static IEnumerator ReadPopupTextDelayed(IntPtr popupPtr, string context)
        {
            // Wait 2 frames to let UI populate
            yield return null;
            yield return null;

            try
            {
                unsafe
                {
                    // Read messageText at offset 0x40
                    IntPtr messageTextPtr = *(IntPtr*)((byte*)popupPtr.ToPointer() + SAVE_POPUP_MESSAGE_TEXT_OFFSET);
                    if (messageTextPtr == IntPtr.Zero)
                    {
                        MelonLogger.Warning($"[SaveLoad] {context}: messageText pointer is null");
                        yield break;
                    }

                    var textComponent = new UnityEngine.UI.Text(messageTextPtr);
                    string message = textComponent.text;

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        // Strip Unity rich text tags (like <color=#ff4040>...</color>)
                        message = StripRichTextTags(message);
                        MelonLogger.Msg($"[SaveLoad] {context}: {message}");
                        FFII_ScreenReaderMod.SpeakText(message);
                    }
                    else
                    {
                        MelonLogger.Warning($"[SaveLoad] {context}: Message is empty");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading {context} popup text: {ex.Message}");
            }
        }

        /// <summary>
        /// Strips Unity rich text tags from a string.
        /// Removes tags like <color=#xxxxxx>, </color>, <b>, </b>, etc.
        /// </summary>
        private static string StripRichTextTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Remove all XML-style tags: <tagname>, </tagname>, <tagname=value>, etc.
            return Regex.Replace(text, @"<[^>]+>", string.Empty);
        }

        private static void ClearPopupState()
        {
            SaveLoadMenuState.ResetState();
            PopupState.Clear();
            lastAnnouncedButtonIndex = -1;
            MelonLogger.Msg("[SaveLoad] Popup closed, state cleared");
        }

        /// <summary>
        /// Reset button tracking state (called when popup opens).
        /// </summary>
        public static void ResetButtonTracking()
        {
            lastAnnouncedButtonIndex = -1;
        }

        /// <summary>
        /// Reads and announces the current SavePopup button based on cursor index.
        /// Called from CursorNavigation_Postfix when SavePopup is active.
        /// </summary>
        public static void ReadCurrentButton(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return;

                // Get popup pointer from PopupState
                IntPtr popupPtr = PopupState.ActivePopupPtr;
                if (popupPtr == IntPtr.Zero) return;

                int cursorIndex = cursor.Index;

                // Skip if same button as last announced
                if (cursorIndex == lastAnnouncedButtonIndex)
                    return;

                lastAnnouncedButtonIndex = cursorIndex;

                // Read commandList at offset 0x60
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + SAVE_POPUP_COMMAND_LIST_OFFSET);
                if (listPtr == IntPtr.Zero) return;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size) return;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return;

                // Read text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    MelonLogger.Msg($"[SaveLoad] Popup button: {buttonText}");
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading current button: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if current popup is a SavePopup (for cursor navigation handling).
        /// </summary>
        public static bool IsSavePopupActive()
        {
            return SaveLoadMenuState.IsInConfirmation &&
                   PopupState.IsConfirmationPopupActive &&
                   PopupState.CurrentPopupType != null &&
                   PopupState.CurrentPopupType.Contains("Popup");
        }
    }
}
