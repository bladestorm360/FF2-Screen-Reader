using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

// Type aliases for IL2CPP types - Base
using BasePopup = Il2CppLast.UI.Popup;
using GameCursor = Il2CppLast.UI.Cursor;

// Type aliases for IL2CPP types - KeyInput Popups
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using KeyInputChangeMagicStonePopup = Il2CppLast.UI.KeyInput.ChangeMagicStonePopup;
using KeyInputGameOverSelectPopup = Il2CppLast.UI.KeyInput.GameOverSelectPopup;
using KeyInputGameOverLoadPopup = Il2CppLast.UI.KeyInput.GameOverLoadPopup;
using KeyInputGameOverPopupController = Il2CppLast.UI.KeyInput.GameOverPopupController;
using KeyInputInfomationPopup = Il2CppLast.UI.KeyInput.InfomationPopup;
using KeyInputInputPopup = Il2CppLast.UI.KeyInput.InputPopup;
using KeyInputChangeNamePopup = Il2CppLast.UI.KeyInput.ChangeNamePopup;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;
using KeyInputTitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;

// Type aliases for IL2CPP types - Touch Popups
using TouchCommonPopup = Il2CppLast.UI.Touch.CommonPopup;
using TouchTitleMenuCommandController = Il2CppLast.UI.Touch.TitleMenuCommandController;

// Splash/Title screen
using SplashController = Il2CppLast.UI.SplashController;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks popup state for handling in CursorNavigation.
    /// </summary>
    public static class PopupState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.POPUP);

        static PopupState()
        {
            _helper.RegisterResetHandler(() =>
            {
                CurrentPopupType = null;
                ActivePopupPtr = IntPtr.Zero;
                CommandListOffset = -1;
            });
        }

        public static bool IsConfirmationPopupActive => _helper.IsActive;

        public static string CurrentPopupType { get; private set; }

        public static IntPtr ActivePopupPtr { get; private set; }

        public static int CommandListOffset { get; private set; }

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset)
        {
            _helper.SetActiveExclusive();
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
        }

        public static void Clear() => _helper.IsActive = false;

        public static bool ShouldSuppress() => IsConfirmationPopupActive && CommandListOffset >= 0;
    }

    /// <summary>
    /// Patches for popup dialogs - handles ALL popup reading (message + buttons).
    /// Uses TryCast for IL2CPP-safe type detection.
    ///
    /// Supported popup types:
    /// - CommonPopup: General confirmations (save/load, return to title, exit game)
    /// - ChangeMagicStonePopup: Spell learn/remove (tome usage in FF2)
    /// - GameOverSelectPopup: Game over options
    /// - InfomationPopup: Info-only (no buttons)
    /// - InputPopup: Text input
    /// - ChangeNamePopup: Character renaming
    ///
    /// EXCLUDES: Shop popups (handled by ShopPatches).
    /// </summary>
    public static class PopupPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for popups.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                TryPatchGameOverSelectPopupUpdateCommand(harmony);
                TryPatchGameOverLoadPopup(harmony);
                TryPatchTitleScreen(harmony);
                isPatched = true;
            }
            catch { }
        }

        /// <summary>
        /// Patch base Popup.Open() and Popup.Close() methods.
        /// </summary>
        private static void TryPatchBasePopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(BasePopup);

                // Use AccessTools.Method for IL2CPP compatibility
                var openMethod = AccessTools.Method(popupType, "Open");
                if (openMethod != null)
                {
                    var openPostfix = typeof(PopupPatches).GetMethod(nameof(PopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                }

                var closeMethod = AccessTools.Method(popupType, "Close");
                if (closeMethod != null)
                {
                    var closePostfix = typeof(PopupPatches).GetMethod(nameof(PopupClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                }
            }
            catch { }
        }

        /// <summary>
        /// Patch GameOverSelectPopup.UpdateCommand to read buttons on navigation.
        /// FF2 uses UpdateCommand (not UpdateFocus like FF1 for button input handling).
        /// </summary>
        private static void TryPatchGameOverSelectPopupUpdateCommand(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(KeyInputGameOverSelectPopup);
                var updateCommandMethod = AccessTools.Method(popupType, "UpdateCommand");

                if (updateCommandMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverSelectPopup_UpdateCommand_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateCommandMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] GameOverSelectPopup.UpdateCommand method not found");
                }
            }
            catch { }
        }

        /// <summary>
        /// Patch GameOverLoadPopup for the "Start from recent save data?" popup.
        /// This popup is NOT a Popup subclass (it extends MonoBehaviour), so we need separate patches.
        /// </summary>
        private static void TryPatchGameOverLoadPopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch GameOverLoadPopup.UpdateCommand for button navigation
                Type loadPopupType = typeof(KeyInputGameOverLoadPopup);
                var updateCommandMethod = AccessTools.Method(loadPopupType, "UpdateCommand");

                if (updateCommandMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverLoadPopup_UpdateCommand_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateCommandMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] GameOverLoadPopup.UpdateCommand method not found");
                }

                // Also patch UpdateFocus - cursor navigation may use this method instead of UpdateCommand
                var updateFocusMethod = AccessTools.Method(loadPopupType, "UpdateFocus");
                if (updateFocusMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverLoadPopup_UpdateCommand_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch GameOverPopupController.InitSaveLoadPopup to announce the popup message
                Type controllerType = typeof(KeyInputGameOverPopupController);
                var initMethod = AccessTools.Method(controllerType, "InitSaveLoadPopup");

                if (initMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverPopupController_InitSaveLoadPopup_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] GameOverPopupController.InitSaveLoadPopup method not found");
                }
            }
            catch { }
        }

        /// <summary>
        /// Patch title screen "Press any button" using combination approach:
        /// 1. SplashController.InitializeTitle - stores text silently (fires early during loading)
        /// 2. SystemIndicator.Show - tracks when title loading starts
        /// 3. SystemIndicator.Hide - speaks stored text when loading completes (indicator hidden)
        /// </summary>
        private static void TryPatchTitleScreen(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Step 1: Patch SplashController.InitializeTitle to capture and store the text
                Type splashControllerType = typeof(SplashController);
                var initTitleMethod = AccessTools.Method(splashControllerType, "InitializeTitle");

                if (initTitleMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SplashController_InitializeTitle_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initTitleMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] SplashController.InitializeTitle method not found");
                }

                // Step 2 & 3: Patch SystemIndicator.Show and Hide
                Type systemIndicatorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        systemIndicatorType = asm.GetType("Il2CppLast.Systems.Indicator.SystemIndicator");
                        if (systemIndicatorType != null)
                        {
                            break;
                        }
                    }
                    catch { }
                }

                if (systemIndicatorType == null)
                {
                    MelonLogger.Error("[Popup] SystemIndicator type not found");
                    return;
                }

                // Patch Show(Mode) to track when title loading starts
                var showMethod = AccessTools.Method(systemIndicatorType, "Show");
                if (showMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SystemIndicator_Show_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] SystemIndicator.Show method not found");
                }

                // Patch Hide() to speak when loading completes
                var hideMethod = AccessTools.Method(systemIndicatorType, "Hide");
                if (hideMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SystemIndicator_Hide_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(hideMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] SystemIndicator.Hide method not found");
                }

                // Step 4: Patch TitleMenuCommandController.SetEnableMainMenu to clear state when title menu becomes active
                TryPatchTitleMenuCommand(harmony);
            }
            catch { }
        }

        /// <summary>
        /// Patch TitleMenuCommandController.SetEnableMainMenu(bool) in both KeyInput and Touch namespaces.
        /// Clears all menu states when title menu becomes enabled (after "Press any button").
        /// </summary>
        private static void TryPatchTitleMenuCommand(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch KeyInput version
                Type keyInputType = typeof(KeyInputTitleMenuCommandController);
                var keyInputMethod = AccessTools.Method(keyInputType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (keyInputMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(keyInputMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] KeyInput.TitleMenuCommandController.SetEnableMainMenu not found");
                }

                // Patch Touch version
                Type touchType = typeof(TouchTitleMenuCommandController);
                var touchMethod = AccessTools.Method(touchType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (touchMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(touchMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] Touch.TitleMenuCommandController.SetEnableMainMenu not found");
                }
            }
            catch { }
        }

        /// <summary>
        /// Check if shop is active.
        /// </summary>
        private static bool IsShopActive()
        {
            try
            {
                var shopController = UnityEngine.Object.FindObjectOfType<KeyInputShopController>();
                return shopController != null && shopController.IsOpne;
            }
            catch
            {
                return false;
            }
        }

        #region Text Reading Helpers

        private static string ReadTextFromPointer(IntPtr textPtr)
        {
            if (textPtr == IntPtr.Zero) return null;
            try
            {
                var text = new Text(textPtr);
                return text?.text;
            }
            catch { return null; }
        }

        private static string ReadIconTextViewText(IntPtr iconTextViewPtr)
        {
            if (iconTextViewPtr == IntPtr.Zero) return null;
            try
            {
                IntPtr nameTextPtr = Marshal.ReadIntPtr(iconTextViewPtr + IL2CppOffsets.Popup.ICON_TEXT_VIEW_NAME_TEXT_OFFSET);
                return ReadTextFromPointer(nameTextPtr);
            }
            catch { return null; }
        }

        private static string BuildAnnouncement(string title, string message)
        {
            title = string.IsNullOrWhiteSpace(title) ? null : TextUtils.StripIconMarkup(title.Trim());
            message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
                return $"{title}. {message}";
            else if (!string.IsNullOrEmpty(title))
                return title;
            else if (!string.IsNullOrEmpty(message))
                return message;
            return null;
        }

        #endregion

        #region Type-Specific Readers

        private static string ReadCommonPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.COMMON_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.COMMON_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadChangeMagicStonePopup(IntPtr ptr)
        {
            IntPtr namePtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.MAGICSTONE_NAME_OFFSET);
            string name = ReadTextFromPointer(namePtr);
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.MAGICSTONE_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return BuildAnnouncement(name, desc);
        }

        private static string ReadGameOverSelectPopup(IntPtr ptr)
        {
            // GameOverSelectPopup has no title/message, just buttons
            // Announce "Game Over" as context
            return "Game Over";
        }

        private static string ReadInfomationPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.INFO_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.INFO_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadInputPopup(IntPtr ptr)
        {
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.INPUT_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        private static string ReadChangeNamePopup(IntPtr ptr)
        {
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + IL2CppOffsets.Popup.CHANGENAME_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        #endregion

        #region Button Reading

        /// <summary>
        /// Read current button label from active popup.
        /// Called by CursorNavigation_Postfix when popup is active.
        /// </summary>
        public static void ReadCurrentButton(GameCursor cursor)
        {
            try
            {
                if (PopupState.ActivePopupPtr == IntPtr.Zero)
                {
                    return;
                }

                if (PopupState.CommandListOffset < 0)
                {
                    return;
                }

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    cursor.Index);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch { }
        }

        private static string ReadButtonFromCommandList(IntPtr popupPtr, int cmdListOffset, int index)
        {
            try
            {
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + cmdListOffset);
                if (listPtr == IntPtr.Zero) return null;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (index < 0 || index >= size) return null;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return null;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (index * 8));
                if (commandPtr == IntPtr.Zero) return null;

                // CommonCommand.text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + IL2CppOffsets.Popup.COMMON_COMMAND_TEXT_OFFSET);
                return ReadTextFromPointer(textPtr);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Popup Open/Close Postfixes

        /// <summary>
        /// Postfix for base Popup.Open - uses TryCast for type detection.
        /// </summary>
        public static void PopupOpen_Postfix(BasePopup __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                if (IsShopActive())
                {
                    return;
                }

                // Use TryCast for IL2CPP-safe type detection
                // KeyInput types first (more common for keyboard/gamepad)

                // CommonPopup - general confirmations
                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                if (commonPopup != null)
                {
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, IL2CppOffsets.Popup.COMMON_CMDLIST_OFFSET,
                        () => ReadCommonPopup(commonPopup.Pointer));
                    return;
                }

                // ChangeMagicStonePopup - tome/spell learning
                var magicStone = __instance.TryCast<KeyInputChangeMagicStonePopup>();
                if (magicStone != null)
                {
                    HandlePopupDetected("ChangeMagicStonePopup", magicStone.Pointer, IL2CppOffsets.Popup.MAGICSTONE_CMDLIST_OFFSET,
                        () => ReadChangeMagicStonePopup(magicStone.Pointer));
                    return;
                }

                // GameOverSelectPopup
                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, IL2CppOffsets.Popup.GAMEOVER_CMDLIST_OFFSET,
                        () => ReadGameOverSelectPopup(gameOver.Pointer));
                    return;
                }

                // InfomationPopup (no buttons)
                var info = __instance.TryCast<KeyInputInfomationPopup>();
                if (info != null)
                {
                    HandlePopupDetected("InfomationPopup", info.Pointer, -1,
                        () => ReadInfomationPopup(info.Pointer));
                    return;
                }

                // InputPopup (no buttons - input field)
                var input = __instance.TryCast<KeyInputInputPopup>();
                if (input != null)
                {
                    HandlePopupDetected("InputPopup", input.Pointer, -1,
                        () => ReadInputPopup(input.Pointer));
                    return;
                }

                // ChangeNamePopup (no buttons - input field)
                var changeName = __instance.TryCast<KeyInputChangeNamePopup>();
                if (changeName != null)
                {
                    HandlePopupDetected("ChangeNamePopup", changeName.Pointer, -1,
                        () => ReadChangeNamePopup(changeName.Pointer));
                    return;
                }

                // Touch types (fallback)
                var touchCommon = __instance.TryCast<TouchCommonPopup>();
                if (touchCommon != null)
                {
                    // Touch CommonPopup has different offsets: title=0x28, message=0x38
                    HandlePopupDetected("TouchCommonPopup", touchCommon.Pointer, -1, // Touch uses SimpleButton, not commandList
                        () => {
                            IntPtr titlePtr = Marshal.ReadIntPtr(touchCommon.Pointer + 0x28);
                            string title = ReadTextFromPointer(titlePtr);
                            IntPtr msgPtr = Marshal.ReadIntPtr(touchCommon.Pointer + 0x38);
                            string msg = ReadTextFromPointer(msgPtr);
                            return BuildAnnouncement(title, msg);
                        });
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Handle a detected popup - set state and start delayed read.
        /// </summary>
        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc)
        {
            PopupState.SetActive(typeName, ptr, cmdListOffset);

            // Reset button tracking to prevent stale state from previous popups
            BattlePausePatches.Reset();
            SaveLoadPatches.ResetButtonTracking();

            CoroutineManager.StartManaged(DelayedPopupRead(ptr, typeName, readFunc));
        }

        /// <summary>
        /// Coroutine to read popup text after 1 frame delay.
        /// </summary>
        private static IEnumerator DelayedPopupRead(IntPtr popupPtr, string typeName, Func<string> readFunc)
        {
            yield return null; // Wait 1 frame

            try
            {
                if (popupPtr == IntPtr.Zero) yield break;

                string announcement = readFunc();
                if (!string.IsNullOrEmpty(announcement))
                {
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for base Popup.Close - clears state.
        /// </summary>
        public static void PopupClose_Postfix()
        {
            try
            {
                if (PopupState.IsConfirmationPopupActive)
                {
                    PopupState.Clear();
                }
                // Always reset button tracking on popup close to ensure fresh state for next popup
                AnnouncementDeduplicator.Reset("Popup.Button", "Popup.GameOverButton", "Popup.GameOverLoadButton");
            }
            catch { }
        }

        /// <summary>
        /// Postfix for GameOverSelectPopup.UpdateCommand - reads and announces current button.
        /// GameOverSelectPopup has its own UpdateCommand method that needs separate patching.
        /// </summary>
        public static void GameOverSelectPopup_UpdateCommand_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputGameOverSelectPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                // Read selectCursor at offset 0x38
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.Popup.GAMEOVER_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero)
                {
                    return;
                }

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                // Use central deduplicator - skip if same button as last announced
                if (!AnnouncementDeduplicator.ShouldAnnounce("Popup.GameOverButton", cursorIndex))
                    return;

                // Read commandList at offset 0x40
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.Popup.GAMEOVER_CMDLIST_OFFSET);
                if (listPtr == IntPtr.Zero)
                {
                    return;
                }

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size)
                {
                    return;
                }

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return;

                // CommonCommand.text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + IL2CppOffsets.Popup.COMMON_COMMAND_TEXT_OFFSET);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for GameOverLoadPopup.UpdateCommand - reads and announces current button.
        /// This handles button navigation (Yes/No) for the "Start from recent save data?" popup.
        /// </summary>
        public static void GameOverLoadPopup_UpdateCommand_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputGameOverLoadPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                // Read selectCursor at offset 0x58
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.Popup.GAMEOVERLOAD_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero)
                {
                    return;
                }

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                // Use central deduplicator - skip if same button as last announced
                if (!AnnouncementDeduplicator.ShouldAnnounce("Popup.GameOverLoadButton", cursorIndex))
                    return;

                // Read commandList at offset 0x60
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.Popup.GAMEOVERLOAD_CMDLIST_OFFSET);
                if (listPtr == IntPtr.Zero)
                {
                    return;
                }

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size)
                {
                    return;
                }

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return;

                // CommonCommand.text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + IL2CppOffsets.Popup.COMMON_COMMAND_TEXT_OFFSET);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for GameOverPopupController.InitSaveLoadPopup - announces the load popup message.
        /// Uses a coroutine delay to let the text be set first.
        /// </summary>
        public static void GameOverPopupController_InitSaveLoadPopup_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                var controller = __instance as KeyInputGameOverPopupController;
                if (controller == null)
                {
                    return;
                }

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                {
                    return;
                }

                // Start coroutine to read message after 1 frame delay
                CoroutineManager.StartManaged(DelayedGameOverLoadPopupRead(controllerPtr));
            }
            catch { }
        }

        /// <summary>
        /// Coroutine to read GameOverLoadPopup message after 1 frame delay.
        /// Navigates: controller -> view (0x30) -> loadPopup (0x18) -> messageText (0x40)
        /// </summary>
        private static IEnumerator DelayedGameOverLoadPopupRead(IntPtr controllerPtr)
        {
            yield return null; // Wait 1 frame

            try
            {
                if (controllerPtr == IntPtr.Zero) yield break;

                // Read view at offset 0x30
                IntPtr viewPtr = Marshal.ReadIntPtr(controllerPtr + IL2CppOffsets.Popup.GAMEOVERPOPUPCTRL_VIEW_OFFSET);
                if (viewPtr == IntPtr.Zero)
                {
                    yield break;
                }

                // Read loadPopup at offset 0x18 from view
                IntPtr loadPopupPtr = Marshal.ReadIntPtr(viewPtr + IL2CppOffsets.Popup.GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET);
                if (loadPopupPtr == IntPtr.Zero)
                {
                    yield break;
                }

                // Read messageText at offset 0x40
                IntPtr messagePtr = Marshal.ReadIntPtr(loadPopupPtr + IL2CppOffsets.Popup.GAMEOVERLOAD_MESSAGE_OFFSET);
                string message = ReadTextFromPointer(messagePtr);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    message = TextUtils.StripIconMarkup(message.Trim());
                    FFII_ScreenReaderMod.SpeakText(message, interrupt: false);
                }
            }
            catch { }
        }

        #endregion

        #region Title Screen (Press Any Button) - SystemIndicator Approach

        /// <summary>
        /// Stores the "Press any button" text captured during InitializeTitle.
        /// Spoken when SystemIndicator.Hide() is called (loading indicator hidden).
        ///
        /// KNOWN ISSUE: Speech occurs ~1 second before user input is actually available.
        /// No hookable method exists that fires exactly when input becomes available.
        /// </summary>
        private static string pendingTitleText = null;

        /// <summary>
        /// Guard flag: only true when we've captured title screen text and are waiting to speak it.
        /// This ensures speech only triggers for title screen, not other loading sequences.
        /// Set true ONLY by InitializeTitle, cleared when speech occurs.
        /// </summary>
        private static bool isTitleScreenTextPending = false;

        /// <summary>
        /// Postfix for SplashController.InitializeTitle.
        /// Called when entering the Title state - captures and stores the text but does NOT speak.
        /// The text will be spoken later by SystemIndicator.Hide when loading completes.
        /// </summary>
        public static void SplashController_InitializeTitle_Postfix(SplashController __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                // Try to read the localized "Press any button" text from UiMessageConstants
                string pressText = null;

                try
                {
                    // Access UiMessageConstants via reflection since it's in root namespace
                    var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                                 ?? Type.GetType("UiMessageConstants, Assembly-CSharp");

                    if (uiMsgType != null)
                    {
                        var field = uiMsgType.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                        if (field != null)
                        {
                            pressText = field.GetValue(null) as string;
                        }
                    }
                }
                catch
                {
                    // Silently ignore - will use fallback
                }

                if (!string.IsNullOrWhiteSpace(pressText))
                {
                    pendingTitleText = TextUtils.StripIconMarkup(pressText.Trim());
                }
                else
                {
                    // Fallback to hardcoded text
                    pendingTitleText = "Press any button";
                }

                // Set the guard flag - this ensures only title screen triggers speech
                isTitleScreenTextPending = true;
            }
            catch
            {
                pendingTitleText = "Press any button";
                isTitleScreenTextPending = true;
            }
        }

        /// <summary>
        /// Postfix for SystemIndicator.Show(Mode).
        /// Just logs for debugging.
        /// </summary>
        public static void SystemIndicator_Show_Postfix(int mode)
        {
            // No-op - kept for potential future debugging needs
        }

        /// <summary>
        /// Postfix for SystemIndicator.Hide().
        /// Called when loading indicator is hidden (loading complete).
        /// If we have pending title text AND the guard flag is set, speaks immediately.
        ///
        /// KNOWN ISSUE: This fires ~1 second before user input is actually available.
        /// </summary>
        public static void SystemIndicator_Hide_Postfix()
        {
            try
            {
                // Only proceed if BOTH the guard flag is set AND we have valid text
                // This ensures we only speak for the title screen, not other loading sequences
                if (isTitleScreenTextPending && !string.IsNullOrWhiteSpace(pendingTitleText))
                {
                    FFII_ScreenReaderMod.SpeakText(pendingTitleText, interrupt: false);

                    // Clear BOTH to prevent any re-triggering
                    pendingTitleText = null;
                    isTitleScreenTextPending = false;
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for TitleMenuCommandController.SetEnableMainMenu(bool).
        /// Called when title menu becomes enabled/disabled.
        /// Clears all menu states when isEnable=true (title menu becoming active after button press).
        /// </summary>
        public static void TitleMenuCommand_SetEnableMainMenu_Postfix(bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    // Title menu is becoming active - clear all battle/menu states
                    // This happens after user presses button on "Press any button" screen
                    MenuStateRegistry.ResetAll();
                }
            }
            catch { }
        }

        #endregion
    }
}
