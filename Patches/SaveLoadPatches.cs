using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Utils;

// FF2 Save/Load UI types
// All controllers use SavePopup with messageText at 0x40, commandList at 0x60
using TitleLoadController = Il2CppLast.UI.KeyInput.LoadGameWindowController;  // Title screen load (savePopup at 0x58)
using MainMenuLoadController = Il2CppLast.UI.KeyInput.LoadWindowController;   // Main menu load (savePopup at 0x28)
using MainMenuSaveController = Il2CppLast.UI.KeyInput.SaveWindowController;   // Main menu save (savePopup at 0x28)
using InterruptionController = Il2CppLast.UI.KeyInput.InterruptionWindowController;  // QuickSave (savePopup at 0x38)
using SaveListController = Il2CppLast.UI.KeyInput.SaveListController;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks save/load menu state for suppression.
    /// </summary>
    public static class SaveLoadMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.SAVE_LOAD_MENU);

        static SaveLoadMenuState()
        {
            _helper.RegisterResetHandler(() => { IsInConfirmation = false; });
        }

        public static bool IsActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
        }
        public static bool IsInConfirmation { get; set; } = false;

        public static bool ShouldSuppress()
        {
            return IsActive && IsInConfirmation;
        }

        public static void ResetState() => _helper.IsActive = false;
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
    /// All use SavePopup with messageText at 0x40, selectCursor at 0x58, commandList at 0x60. The
    /// popup's message is read with its focused choice appended; choice navigation flows through the
    /// cursor patches (PopupState) — SavePopup has no UpdateFocus in FF2.
    ///
    /// Also hooks SaveListController.SetActive (shared by title load, field save/load and quicksave)
    /// to read the initially-highlighted slot when the list opens (FF1 SaveListPatches port).
    /// </summary>
    public static class SaveLoadPatches
    {
        // SaveListController.selectCursor (KeyInput, dump.cs SaveListController)
        private const int OFFSET_SAVE_LIST_SELECT_CURSOR = 0x58;

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

                // Patch SaveListController.SetActive for the slot open-read
                TryPatchSaveList(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SaveLoad] Failed to apply patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SaveListController.SetActive(bool isActive, bool isReset, bool isLoadGame).
        /// </summary>
        private static void TryPatchSaveList(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = AccessTools.Method(typeof(SaveListController), "SetActive",
                    new Type[] { typeof(bool), typeof(bool), typeof(bool) });
                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(SaveListSetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[SaveLoad] SaveListController.SetActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SaveLoad] Error patching SaveListController.SetActive: {ex.Message}");
            }
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
                }
                else
                {
                    MelonLogger.Error("[SaveLoad] TitleLoadController.SetPopupActive not found");
                }
            }
            catch { }
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
                }
                else
                {
                    MelonLogger.Error("[SaveLoad] MainMenuLoadController.SetPopupActive not found");
                }
            }
            catch { }
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
                }
                else
                {
                    MelonLogger.Error("[SaveLoad] MainMenuSaveController.SetPopupActive not found");
                }
            }
            catch { }
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
                }
                else
                {
                    MelonLogger.Error("[SaveLoad] InterruptionController.SetEnablePopup not found");
                }
            }
            catch { }
        }

        // ============ Postfix Methods ============

        /// <summary>
        /// The save list opened (title load, field save/load, quicksave): read the initially-highlighted
        /// slot one frame later — navigation is read by the generic cursor reader, but the initial
        /// placement never fires it.
        /// </summary>
        public static void SaveListSetActive_Postfix(SaveListController __instance, bool isActive)
        {
            if (!isActive || __instance == null) return;
            CoroutineManager.StartManaged(DelayedReadSlot(__instance));
        }

        private static IEnumerator DelayedReadSlot(SaveListController controller)
        {
            yield return null;   // let the list populate + cursor settle
            string announcement = null;
            try
            {
                // The background autosave during a map load also activates a SaveListController while no
                // menu is open — ShouldReadSaveSlot excludes it.
                if (controller != null && controller.gameObject != null && controller.gameObject.activeInHierarchy
                    && ShouldReadSaveSlot())
                {
                    IntPtr cursorPtr = Marshal.ReadIntPtr(controller.Pointer, OFFSET_SAVE_LIST_SELECT_CURSOR);
                    if (cursorPtr != IntPtr.Zero)
                    {
                        var cursor = new GameCursor(cursorPtr);
                        announcement = SaveSlotReader.TryReadSaveSlot(cursor.transform, cursor.Index, out int count);
                        if (announcement != null)
                            announcement = MenuPosition.Format(announcement, cursor.Index, count);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading save slot: {ex.Message}");
            }
            if (!string.IsNullOrWhiteSpace(announcement))
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }

        /// <summary>
        /// True when save-slot content should be announced: a real in-game save/load menu is open
        /// (MenuManager.IsOpen — false during a map load's scene construction), or the title Load screen
        /// (not a MenuManager menu) is on-screen. Excludes the background autosave list.
        /// </summary>
        public static bool ShouldReadSaveSlot()
        {
            try
            {
                var mm = Il2CppLast.UI.MenuManager.Instance;
                if (mm != null && mm.IsOpen) return true;
            }
            catch { } // MenuManager not available yet
            try
            {
                var title = UnityEngine.Object.FindObjectOfType<TitleLoadController>();
                return title != null && title.gameObject != null && title.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        public static void TitleLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as TitleLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.TITLE_LOAD_SAVE_POPUP_OFFSET, "TitleLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch { }
        }

        public static void MainMenuLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as MainMenuLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.MAIN_MENU_SAVE_POPUP_OFFSET, "MainMenuLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch { }
        }

        public static void MainMenuSaveSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as MainMenuSaveController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.MAIN_MENU_SAVE_POPUP_OFFSET, "MainMenuSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch { }
        }

        public static void InterruptionSetEnablePopup_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as InterruptionController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.INTERRUPTION_SAVE_POPUP_OFFSET, "QuickSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch { }
        }

        /// <summary>
        /// Starts a coroutine to read SavePopup message after a short delay.
        /// The delay allows the UI to populate the text before we read it.
        /// </summary>
        private static void ReadSavePopup(IntPtr controllerPtr, int savePopupOffset, string context)
        {
            if (controllerPtr == IntPtr.Zero)
                return;

            try
            {
                unsafe
                {
                    // Read savePopup pointer from controller
                    IntPtr popupPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + savePopupOffset);
                    if (popupPtr == IntPtr.Zero)
                        return;

                    // Set state for button navigation immediately
                    SaveLoadMenuState.IsActive = true;
                    SaveLoadMenuState.IsInConfirmation = true;
                    PopupState.SetActive($"{context}Popup", popupPtr, IL2CppOffsets.SaveLoad.SAVE_POPUP_COMMAND_LIST_OFFSET);

                    // Start coroutine to read text after delay (allows UI to populate)
                    CoroutineManager.StartManaged(ReadPopupTextDelayed(popupPtr, context));
                }
            }
            catch { }
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
                    IntPtr messageTextPtr = *(IntPtr*)((byte*)popupPtr.ToPointer() + IL2CppOffsets.SaveLoad.SAVE_POPUP_MESSAGE_TEXT_OFFSET);
                    if (messageTextPtr == IntPtr.Zero)
                        yield break;

                    var textComponent = new UnityEngine.UI.Text(messageTextPtr);
                    string message = textComponent.text;

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        // Strip Unity rich text tags (like <color=#ff4040>...</color>), then append the
                        // initially-focused choice (the cursor's initial placement is never announced
                        // by navigation).
                        message = StripRichTextTags(message);
                        string focused = ReadFocusedButton(popupPtr);
                        if (!string.IsNullOrEmpty(focused))
                            message = $"{message} {focused}";
                        FFII_ScreenReaderMod.SpeakText(message);
                    }
                }
            }
            catch { }
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

        /// <summary>
        /// The SavePopup's focused choice ("Yes, (1 of 2)"), read from its selectCursor (0x58) and
        /// commandList (0x60); null when unavailable.
        /// </summary>
        private static string ReadFocusedButton(IntPtr popupPtr)
        {
            try
            {
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.SaveLoad.SAVE_POPUP_SELECT_CURSOR_OFFSET);
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.SaveLoad.SAVE_POPUP_COMMAND_LIST_OFFSET);
                if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero) return null;
                int cursorIndex = new GameCursor(cursorPtr).Index;

                // IL2CPP List: _size at 0x18, _items at 0x10; array elements start at 0x20
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size) return null;
                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return null;
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return null;
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + IL2CppOffsets.SaveLoad.COMMON_COMMAND_TEXT_OFFSET);
                if (textPtr == IntPtr.Zero) return null;

                string buttonText = new UnityEngine.UI.Text(textPtr).text;
                if (string.IsNullOrWhiteSpace(buttonText)) return null;
                return MenuPosition.Format(TextUtils.StripIconMarkup(buttonText.Trim()), cursorIndex, size);
            }
            catch { return null; }
        }

        private static void ClearPopupState()
        {
            SaveLoadMenuState.ResetState();
            PopupState.Clear();
        }
    }
}
