using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using CommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using CommonCommand = Il2CppLast.UI.KeyInput.CommonCommand;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for popup dialogs (Yes/No confirmations).
    /// </summary>
    public static class PopupState
    {
        private static bool _isActive = false;
        private static string lastAnnouncement = "";

        // Memory offsets for KeyInput.CommonPopup (from dump.cs line 457726)
        public const int OFFSET_SELECT_CURSOR = 0x68;
        public const int OFFSET_COMMAND_LIST = 0x70;

        public static bool IsActive => _isActive;

        public static void SetActive()
        {
            _isActive = true;
            lastAnnouncement = "";
        }

        public static void ClearState()
        {
            _isActive = false;
            lastAnnouncement = "";
        }

        public static bool ShouldAnnounce(string announcement)
        {
            if (announcement == lastAnnouncement)
                return false;
            lastAnnouncement = announcement;
            return true;
        }

        public static bool ShouldSuppress()
        {
            if (!_isActive)
                return false;

            try
            {
                var popup = UnityEngine.Object.FindObjectOfType<CommonPopup>();
                if (popup == null || !popup.gameObject.activeInHierarchy)
                {
                    ClearState();
                    return false;
                }
                return true;
            }
            catch
            {
                ClearState();
                return false;
            }
        }
    }

    /// <summary>
    /// Patches for CommonPopup Yes/No dialogs.
    /// Handles confirmations like "Learn this spell?" when using tomes.
    /// </summary>
    public static class PopupPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                MelonLogger.Msg("[Popup] Applying popup patches...");

                TryPatchCommonPopup(harmony);

                isPatched = true;
                MelonLogger.Msg("[Popup] Popup patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchCommonPopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(CommonPopup);

                // Patch UpdateFocus to announce selected option
                MethodInfo updateFocusMethod = null;
                foreach (var method in popupType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "UpdateFocus")
                    {
                        updateFocusMethod = method;
                        break;
                    }
                }

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched CommonPopup.UpdateFocus");
                }
                else
                {
                    MelonLogger.Warning("[Popup] CommonPopup.UpdateFocus not found");
                }

                // Also patch Open to announce when popup appears
                MethodInfo openMethod = null;
                foreach (var method in popupType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "Open" && method.GetParameters().Length == 0)
                    {
                        openMethod = method;
                        break;
                    }
                }

                if (openMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(Open_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched CommonPopup.Open");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching CommonPopup: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for CommonPopup.Open - marks popup as active and announces initial state.
        /// </summary>
        public static void Open_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var popup = __instance as CommonPopup;
                if (popup == null)
                    return;

                PopupState.SetActive();

                // Announce the first focused option after a brief delay
                // The cursor may not be set yet during Open
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Open_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for CommonPopup.UpdateFocus - announces the selected option (Yes/No).
        /// </summary>
        public static void UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var popup = __instance as CommonPopup;
                if (popup == null || !popup.gameObject.activeInHierarchy)
                    return;

                // Mark as active
                if (!PopupState.IsActive)
                {
                    PopupState.SetActive();
                }

                // Read cursor and command list using pointer offsets
                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero)
                    return;

                unsafe
                {
                    // Get selectCursor at offset 0x68
                    IntPtr cursorPtr = *(IntPtr*)((byte*)popupPtr.ToPointer() + PopupState.OFFSET_SELECT_CURSOR);
                    if (cursorPtr == IntPtr.Zero)
                        return;

                    var cursor = new GameCursor(cursorPtr);
                    int index = cursor.Index;

                    // Get commandList at offset 0x70
                    IntPtr commandListPtr = *(IntPtr*)((byte*)popupPtr.ToPointer() + PopupState.OFFSET_COMMAND_LIST);
                    if (commandListPtr == IntPtr.Zero)
                        return;

                    var commandList = new Il2CppSystem.Collections.Generic.List<CommonCommand>(commandListPtr);
                    if (index < 0 || index >= commandList.Count)
                        return;

                    var command = commandList[index];
                    if (command == null)
                        return;

                    // Get the Text component and read its text
                    var textComponent = command.Text;
                    if (textComponent == null)
                        return;

                    string optionText = textComponent.text;
                    if (string.IsNullOrEmpty(optionText))
                        return;

                    optionText = TextUtils.StripIconMarkup(optionText);

                    // Check for duplicate announcement
                    if (!PopupState.ShouldAnnounce(optionText))
                        return;

                    MelonLogger.Msg($"[Popup] {optionText}");
                    FFII_ScreenReaderMod.SpeakText(optionText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in UpdateFocus_Postfix: {ex.Message}");
            }
        }
    }
}
