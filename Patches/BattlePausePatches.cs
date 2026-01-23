using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for battle pause menu (spacebar during battle).
    /// Pause menu commands are read via cursor path detection in CursorNavigation_Postfix.
    /// This class handles popup button reading (e.g., Return to Title confirmation).
    /// </summary>
    public static class BattlePausePatches
    {
        // Memory offsets for CommonPopup (KeyInput) - from dump.cs
        private const int OFFSET_SELECT_CURSOR = 0x68;    // Cursor selectCursor
        private const int OFFSET_COMMAND_LIST = 0x70;     // List<CommonCommand> commandList

        // Memory offset for CommonCommand - from dump.cs
        private const int OFFSET_COMMAND_TEXT = 0x18;     // Text text

        // Track last announced button to avoid duplicates
        private static int lastAnnouncedButtonIndex = -1;

        /// <summary>
        /// Apply battle pause menu patches.
        /// Note: State clearing for Return to Title is handled by TitleMenuCommandController.SetEnableMainMenu
        /// in PopupPatches.cs, which fires when title menu becomes active.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch CommonPopup.UpdateFocus for popup button reading during battle
                TryPatchCommonPopupUpdateFocus(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch CommonPopup.UpdateFocus to read popup buttons during battle.
        /// </summary>
        private static void TryPatchCommonPopupUpdateFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(KeyInputCommonPopup);
                var updateFocusMethod = AccessTools.Method(popupType, "UpdateFocus");

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(BattlePausePatches).GetMethod(nameof(CommonPopup_UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Pause] CommonPopup.UpdateFocus method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error patching CommonPopup.UpdateFocus: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for CommonPopup.UpdateFocus - reads and announces current button.
        /// Only active during battle - outside battle, CursorNavigation_Postfix handles popup buttons.
        /// Uses lastAnnouncedButtonIndex for duplicate prevention.
        /// </summary>
        public static void CommonPopup_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                // Only handle popups during battle - outside battle, CursorNavigation_Postfix handles it
                if (!FFII_ScreenReaderMod.IsInBattleUIContext())
                    return;

                if (__instance == null) return;

                var popup = __instance as KeyInputCommonPopup;
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

                // Read selectCursor at offset 0x68
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + OFFSET_SELECT_CURSOR);
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

                // Read commandList at offset 0x70
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + OFFSET_COMMAND_LIST);
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
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + OFFSET_COMMAND_TEXT);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error in UpdateFocus postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state (called when battle ends or popup closes).
        /// </summary>
        public static void Reset()
        {
            lastAnnouncedButtonIndex = -1;
        }
    }
}
