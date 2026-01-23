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
        /// <summary>
        /// True when a confirmation popup is active.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsConfirmationPopupActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.POPUP);
            private set => MenuStateRegistry.SetActive(MenuStateRegistry.POPUP, value);
        }

        /// <summary>
        /// The type name of the current popup.
        /// </summary>
        public static string CurrentPopupType { get; private set; }

        /// <summary>
        /// Pointer to the active popup instance.
        /// </summary>
        public static IntPtr ActivePopupPtr { get; private set; }

        /// <summary>
        /// Offset to commandList field (-1 if popup has no buttons).
        /// </summary>
        public static int CommandListOffset { get; private set; }

        /// <summary>
        /// True when popup just opened - suppresses initial button reading.
        /// Cleared after popup message is read.
        /// </summary>
        public static bool PopupJustOpened { get; set; }

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset)
        {
            IsConfirmationPopupActive = true;
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
            PopupJustOpened = true;
            MelonLogger.Msg($"[Popup] State set: {typeName}, hasButtons={cmdListOffset >= 0}");
        }

        public static void Clear()
        {
            IsConfirmationPopupActive = false;
            CurrentPopupType = null;
            ActivePopupPtr = IntPtr.Zero;
            CommandListOffset = -1;
            PopupJustOpened = false;
        }

        /// <summary>
        /// Returns true if popup with buttons is active (suppress MenuTextDiscovery).
        /// </summary>
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

        // --- Memory offsets (from FF2 dump.cs) ---

        // IconTextView.nameText offset
        private const int ICON_TEXT_VIEW_NAME_TEXT_OFFSET = 0x20;

        // CommonCommand.text offset
        private const int COMMON_COMMAND_TEXT_OFFSET = 0x18;

        // CommonPopup (KeyInput) - line 457709
        private const int COMMON_TITLE_OFFSET = 0x38;      // IconTextView
        private const int COMMON_MESSAGE_OFFSET = 0x40;    // Text
        private const int COMMON_CMDLIST_OFFSET = 0x70;    // List<CommonCommand>

        // ChangeMagicStonePopup (KeyInput) - line 457506
        private const int MAGICSTONE_NAME_OFFSET = 0x28;   // Text
        private const int MAGICSTONE_DESC_OFFSET = 0x30;   // Text
        private const int MAGICSTONE_CMDLIST_OFFSET = 0x58;

        // GameOverSelectPopup (KeyInput) - line 457900
        private const int GAMEOVER_CMDLIST_OFFSET = 0x40;

        // InfomationPopup (KeyInput) - line 458020
        private const int INFO_TITLE_OFFSET = 0x28;        // IconTextView
        private const int INFO_MESSAGE_OFFSET = 0x30;      // Text

        // InputPopup (KeyInput) - line 458048
        private const int INPUT_DESC_OFFSET = 0x30;        // Text

        // ChangeNamePopup (KeyInput) - line 457646
        private const int CHANGENAME_DESC_OFFSET = 0x30;   // Text

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
                TryPatchTitleScreen(harmony);
                isPatched = true;
                MelonLogger.Msg("[Popup] All popup patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
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
                    MelonLogger.Msg("[Popup] Patched base Popup.Open");
                }

                var closeMethod = AccessTools.Method(popupType, "Close");
                if (closeMethod != null)
                {
                    var closePostfix = typeof(PopupPatches).GetMethod(nameof(PopupClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                    MelonLogger.Msg("[Popup] Patched base Popup.Close");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching base Popup: {ex.Message}");
            }
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
                    MelonLogger.Msg("[Popup] Patched SplashController.InitializeTitle (stores text)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] SplashController.InitializeTitle method not found");
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
                            MelonLogger.Msg($"[Popup] Found SystemIndicator in {asm.GetName().Name}");
                            break;
                        }
                    }
                    catch { }
                }

                if (systemIndicatorType == null)
                {
                    MelonLogger.Warning("[Popup] SystemIndicator type not found");
                    return;
                }

                // Patch Show(Mode) to track when title loading starts
                var showMethod = AccessTools.Method(systemIndicatorType, "Show");
                if (showMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SystemIndicator_Show_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched SystemIndicator.Show (tracks title loading)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] SystemIndicator.Show method not found");
                }

                // Patch Hide() to speak when loading completes
                var hideMethod = AccessTools.Method(systemIndicatorType, "Hide");
                if (hideMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(SystemIndicator_Hide_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(hideMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched SystemIndicator.Hide (speaks when loading done)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] SystemIndicator.Hide method not found");
                }

                // Step 4: Patch TitleMenuCommandController.SetEnableMainMenu to clear state when title menu becomes active
                TryPatchTitleMenuCommand(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching title screen: {ex.Message}");
            }
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
                    MelonLogger.Msg("[Popup] Patched KeyInput.TitleMenuCommandController.SetEnableMainMenu (clears state on title menu)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] KeyInput.TitleMenuCommandController.SetEnableMainMenu not found");
                }

                // Patch Touch version
                Type touchType = typeof(TouchTitleMenuCommandController);
                var touchMethod = AccessTools.Method(touchType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (touchMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(touchMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched Touch.TitleMenuCommandController.SetEnableMainMenu (clears state on title menu)");
                }
                else
                {
                    MelonLogger.Warning("[Popup] Touch.TitleMenuCommandController.SetEnableMainMenu not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching TitleMenuCommandController: {ex.Message}");
            }
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
                IntPtr nameTextPtr = Marshal.ReadIntPtr(iconTextViewPtr + ICON_TEXT_VIEW_NAME_TEXT_OFFSET);
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
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + COMMON_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + COMMON_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadChangeMagicStonePopup(IntPtr ptr)
        {
            IntPtr namePtr = Marshal.ReadIntPtr(ptr + MAGICSTONE_NAME_OFFSET);
            string name = ReadTextFromPointer(namePtr);
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + MAGICSTONE_DESC_OFFSET);
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
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + INFO_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + INFO_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadInputPopup(IntPtr ptr)
        {
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + INPUT_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        private static string ReadChangeNamePopup(IntPtr ptr)
        {
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + CHANGENAME_DESC_OFFSET);
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
                    MelonLogger.Msg("[Popup] ReadCurrentButton - no active popup");
                    return;
                }

                if (PopupState.CommandListOffset < 0)
                {
                    MelonLogger.Msg("[Popup] ReadCurrentButton - popup has no buttons");
                    return;
                }

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    cursor.Index);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    MelonLogger.Msg($"[Popup] Button: {buttonText}");
                    FFII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
                else
                {
                    MelonLogger.Msg($"[Popup] No button text at index {cursor.Index}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading button: {ex.Message}");
            }
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
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
                return ReadTextFromPointer(textPtr);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading command list: {ex.Message}");
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
                MelonLogger.Msg($"[Popup] ========== PopupOpen_Postfix CALLED ==========");

                if (__instance == null)
                {
                    MelonLogger.Msg("[Popup] Instance is null, returning");
                    return;
                }
                if (IsShopActive())
                {
                    MelonLogger.Msg("[Popup] Skipping - shop is active");
                    return;
                }

                MelonLogger.Msg("[Popup] Starting TryCast type detection...");

                // Use TryCast for IL2CPP-safe type detection
                // KeyInput types first (more common for keyboard/gamepad)

                // CommonPopup - general confirmations
                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                if (commonPopup != null)
                {
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, COMMON_CMDLIST_OFFSET,
                        () => ReadCommonPopup(commonPopup.Pointer));
                    return;
                }

                // ChangeMagicStonePopup - tome/spell learning
                var magicStone = __instance.TryCast<KeyInputChangeMagicStonePopup>();
                if (magicStone != null)
                {
                    HandlePopupDetected("ChangeMagicStonePopup", magicStone.Pointer, MAGICSTONE_CMDLIST_OFFSET,
                        () => ReadChangeMagicStonePopup(magicStone.Pointer));
                    return;
                }

                // GameOverSelectPopup
                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, GAMEOVER_CMDLIST_OFFSET,
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

                // Unknown popup type
                MelonLogger.Msg($"[Popup] Unknown popup type opened (base Popup)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Open postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle a detected popup - set state and start delayed read.
        /// </summary>
        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc)
        {
            MelonLogger.Msg($"[Popup] Detected: {typeName}");
            PopupState.SetActive(typeName, ptr, cmdListOffset);
            CoroutineManager.StartManaged(DelayedPopupRead(ptr, typeName, readFunc));
        }

        /// <summary>
        /// Coroutine to read popup text after 1 frame delay.
        /// Clears PopupJustOpened flag after reading to allow button navigation.
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
                    MelonLogger.Msg($"[Popup] {typeName}: {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
                else
                {
                    MelonLogger.Msg($"[Popup] {typeName} - no text found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in delayed read: {ex.Message}");
            }
            finally
            {
                // Clear flag to allow button reading on subsequent navigation
                PopupState.PopupJustOpened = false;
                MelonLogger.Msg($"[Popup] PopupJustOpened cleared - button navigation enabled");
            }
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
                    MelonLogger.Msg($"[Popup] Close - clearing state for {PopupState.CurrentPopupType}");
                    PopupState.Clear();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Close postfix: {ex.Message}");
            }
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
                MelonLogger.Msg("[Popup] SplashController_InitializeTitle_Postfix called - storing text silently");

                if (__instance == null)
                {
                    MelonLogger.Msg("[Popup] SplashController is null");
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
                            MelonLogger.Msg($"[Popup] MENU_TITLE_PRESS_TEXT = \"{pressText}\"");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Popup] Could not read MENU_TITLE_PRESS_TEXT: {ex.Message}");
                }

                if (!string.IsNullOrWhiteSpace(pressText))
                {
                    pendingTitleText = TextUtils.StripIconMarkup(pressText.Trim());
                    MelonLogger.Msg($"[Popup] Stored pending title text: {pendingTitleText}");
                }
                else
                {
                    // Fallback to hardcoded text
                    pendingTitleText = "Press any button";
                    MelonLogger.Msg("[Popup] Using fallback, stored: Press any button");
                }

                // Set the guard flag - this ensures only title screen triggers speech
                isTitleScreenTextPending = true;
                MelonLogger.Msg("[Popup] Title screen text pending flag set to true");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SplashController.InitializeTitle postfix: {ex.Message}");
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
            try
            {
                MelonLogger.Msg($"[Popup] SystemIndicator_Show_Postfix called with mode={mode}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SystemIndicator.Show postfix: {ex.Message}");
            }
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
                MelonLogger.Msg($"[Popup] SystemIndicator_Hide_Postfix called, flag={isTitleScreenTextPending}, text={pendingTitleText ?? "null"}");

                // Only proceed if BOTH the guard flag is set AND we have valid text
                // This ensures we only speak for the title screen, not other loading sequences
                if (isTitleScreenTextPending && !string.IsNullOrWhiteSpace(pendingTitleText))
                {
                    MelonLogger.Msg($"[Popup] Speaking title text: {pendingTitleText}");
                    FFII_ScreenReaderMod.SpeakText(pendingTitleText, interrupt: false);

                    // Clear BOTH to prevent any re-triggering
                    pendingTitleText = null;
                    isTitleScreenTextPending = false;
                }
                else
                {
                    MelonLogger.Msg("[Popup] Guard flag not set or no text - not title screen loading, skipping");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SystemIndicator.Hide postfix: {ex.Message}");
            }
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
                MelonLogger.Msg($"[Popup] TitleMenuCommand_SetEnableMainMenu_Postfix called with isEnable={isEnable}");

                if (isEnable)
                {
                    // Title menu is becoming active - clear all battle/menu states
                    // This happens after user presses button on "Press any button" screen
                    MelonLogger.Msg("[Popup] Title menu enabled - clearing all menu states");
                    MenuStateRegistry.ResetAll();
                    MelonLogger.Msg("[Popup] All menu states cleared for title screen");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in SetEnableMainMenu postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
