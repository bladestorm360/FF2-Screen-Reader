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
using static FFII_ScreenReader.Utils.ModTextTranslator;

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

// Title screen (the "Press any button" prompt is the None state of the KeyInput title window)
using KeyInputTitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;

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
                SelectCursorOffset = -1;
                LastButtonIndex = -1;
            });
        }

        public static bool IsConfirmationPopupActive => _helper.IsActive;

        public static string CurrentPopupType { get; private set; }

        public static IntPtr ActivePopupPtr { get; private set; }

        public static int CommandListOffset { get; private set; }

        // Offset of the popup's selectCursor (per type; -1 for button-less popups). Lets the
        // on-open read (DelayedPopupRead) announce the initially-focused choice.
        public static int SelectCursorOffset { get; private set; } = -1;

        // Last button index announced for this popup. Reset on open; set by the on-open read and
        // by ReadCurrentButton so a stray cursor event for the same focus can't double-announce.
        public static int LastButtonIndex { get; set; } = -1;

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset, int selectCursorOffset = -1)
        {
            _helper.SetActiveExclusive();
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
            SelectCursorOffset = selectCursorOffset;
            LastButtonIndex = -1;
        }

        public static void Clear() => _helper.IsActive = false;

        public static bool ShouldSuppress() => IsConfirmationPopupActive && CommandListOffset >= 0;

        /// <summary>
        /// True while one of the game-over popups (Load / Return to Title, then "Start from recent
        /// save data?") owns the cursor. Their navigation is routed to ReadCurrentButton even when the
        /// battle flag is still set, so it never depends on the battle-context early return.
        /// </summary>
        public static bool IsGameOverPopupActive =>
            ShouldSuppress() && CurrentPopupType != null && CurrentPopupType.StartsWith("GameOver", StringComparison.Ordinal);
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
        /// Patch GameOverLoadPopup for the "Start from recent save data?" popup.
        /// This popup is NOT a Popup subclass (it extends MonoBehaviour), so Popup.Open never sees it:
        /// InitSaveLoadPopup starts its open read, which also registers it in PopupState so its Yes/No
        /// navigation (Cursor.NextIndex/PrevIndex in the popup's own input lambda) is read by
        /// ReadCurrentButton. The per-frame UpdateCommand/UpdateFocus hooks are gone (CLAUDE.md rule 3):
        /// both run every frame from the popup's UpdateSelect.
        /// </summary>
        private static void TryPatchGameOverLoadPopup(HarmonyLib.Harmony harmony)
        {
            try
            {
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

                // GameOverPopupController.InitCommandSelect (unique RVA 0x7776A0): the Load / Return to
                // Title choice regains the cursor — on first open, and when the load popup or the
                // back-to-title confirmation is cancelled. The select popup is not re-opened then, so
                // Popup.Open never re-registers it.
                var initCommandSelect = AccessTools.Method(controllerType, "InitCommandSelect", Type.EmptyTypes);
                if (initCommandSelect != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverPopupController_InitCommandSelect_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initCommandSelect, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] GameOverPopupController.InitCommandSelect method not found");
                }
            }
            catch { }
        }

        /// <summary>
        /// Patch the title screen "Press any button" prompt (boot AND return to title):
        /// 1. KeyInput TitleWindowController.InitNone (unique RVA 0x804A50) arms the prompt. The None
        ///    state is entered by TitleWindowController.Initialize whenever the title scene is built
        ///    without scene arguments (SceneTitleScreen.CreateInstance → CreateTitleWindow → Initialize →
        ///    StateMachine.Change(None)), which is both boot and return to title.
        /// 2. SystemIndicator.Hide speaks it. UpdateNone calls Hide exactly once, in the frame it shows
        ///    view.startParent (the prompt), once the fade and preload are done. CreateInstance also calls
        ///    Hide, in the same frame as InitNone and before the fade-in, so an arm-frame gate skips it.
        /// (SplashController.InitializeTitle was dropped: its body is folded with six Action-invoke
        /// lambdas — shop trade window, Words menu, result items, … — so it armed the prompt in game.)
        /// </summary>
        private static void TryPatchTitleScreen(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Step 1: arm on the title window's None-state entry
                var initNoneMethod = AccessTools.Method(typeof(KeyInputTitleWindowController), "InitNone", Type.EmptyTypes);
                if (initNoneMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TitleWindowController_InitNone_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initNoneMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Popup] TitleWindowController.InitNone method not found");
                }

                // Step 2: Patch SystemIndicator.Hide (speaks the armed prompt)
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

                // Step 3: Patch TitleMenuCommandController.SetEnableMainMenu to clear state when title menu becomes active
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
            return T("Game Over");
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
        /// True when <paramref name="cursor"/> is the selectCursor of the popup PopupState owns (a popup
        /// with buttons). Lets the cursor reader route the popup's own navigation to ReadCurrentButton in
        /// battle, where the generic reader is otherwise silent, without misreading any other cursor.
        /// </summary>
        public static bool IsActivePopupCursor(GameCursor cursor)
        {
            try
            {
                if (cursor == null || !PopupState.ShouldSuppress() || PopupState.SelectCursorOffset < 0)
                    return false;
                IntPtr popupPtr = PopupState.ActivePopupPtr;
                if (popupPtr == IntPtr.Zero)
                    return false;
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + PopupState.SelectCursorOffset);
                return cursorPtr != IntPtr.Zero && cursorPtr == cursor.Pointer;
            }
            catch
            {
                return false;
            }
        }

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

                int index = cursor.Index;

                // Skip if this index was already announced (e.g. the initial-focus read done by
                // DelayedPopupRead on open, or a repeated cursor event for the same focus).
                if (index == PopupState.LastButtonIndex)
                {
                    return;
                }

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    index,
                    out int count);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    PopupState.LastButtonIndex = index;
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFII_ScreenReaderMod.SpeakText(MenuPosition.Format(buttonText, index, count), interrupt: true);
                }
            }
            catch { }
        }

        private static string ReadButtonFromCommandList(IntPtr popupPtr, int cmdListOffset, int index, out int count)
        {
            count = -1;
            try
            {
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + cmdListOffset);
                if (listPtr == IntPtr.Zero) return null;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                count = size;
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

                // Remember a popup opened over the config menu (Quit / Return to Title): its close
                // re-announces the focused config row. Captured before HandlePopupDetected, whose
                // exclusive popup state clears the config state (and with it the config dedup).
                _openedOverConfig = ConfigMenuState.IsActive;

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
                        () => ReadCommonPopup(commonPopup.Pointer), IL2CppOffsets.Popup.COMMON_SELECT_CURSOR_OFFSET);
                    return;
                }

                // ChangeMagicStonePopup - tome/spell learning
                var magicStone = __instance.TryCast<KeyInputChangeMagicStonePopup>();
                if (magicStone != null)
                {
                    HandlePopupDetected("ChangeMagicStonePopup", magicStone.Pointer, IL2CppOffsets.Popup.MAGICSTONE_CMDLIST_OFFSET,
                        () => ReadChangeMagicStonePopup(magicStone.Pointer), IL2CppOffsets.Popup.MAGICSTONE_SELECT_CURSOR_OFFSET);
                    return;
                }

                // GameOverSelectPopup — the open read appends the focused command (Load / Return to
                // Title); navigation is read by ReadCurrentButton (see IsGameOverPopupActive).
                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, IL2CppOffsets.Popup.GAMEOVER_CMDLIST_OFFSET,
                        () => ReadGameOverSelectPopup(gameOver.Pointer), IL2CppOffsets.Popup.GAMEOVER_SELECT_CURSOR_OFFSET);
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
        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc, int selectCursorOffset = -1)
        {
            PopupState.SetActive(typeName, ptr, cmdListOffset, selectCursorOffset);

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
                    // Append the initially-focused choice so the default button (Yes/No) is
                    // announced on open, not just on the first arrow. Read the LIVE cursor index
                    // (not a hard 0) so a "No"-default popup correctly announces "No".
                    if (PopupState.CommandListOffset >= 0 && PopupState.SelectCursorOffset >= 0)
                    {
                        try
                        {
                            IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + PopupState.SelectCursorOffset);
                            if (cursorPtr != IntPtr.Zero)
                            {
                                int idx = new GameCursor(cursorPtr).Index;
                                string choice = ReadButtonFromCommandList(popupPtr, PopupState.CommandListOffset, idx, out _);
                                if (!string.IsNullOrWhiteSpace(choice))
                                {
                                    announcement += ". " + TextUtils.StripIconMarkup(choice);
                                    PopupState.LastButtonIndex = idx;
                                }
                            }
                        }
                        catch { }
                    }

                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch { }
        }

        // True while a popup opened over the config menu is up (see PopupOpen_Postfix).
        private static bool _openedOverConfig = false;

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
                // A cancelled popup over the config menu returns to the row without re-focusing it;
                // re-announce it (read one frame later, only while the config menu is still open).
                if (_openedOverConfig)
                {
                    _openedOverConfig = false;
                    ConfigMenuPatches.ReannounceFocusedConfigOption();
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

        // GameOverPopupController.selectPopup (KeyInput, dump.cs: GameOverPopupController @0x50)
        private const int GAMEOVERPOPUPCTRL_SELECT_POPUP_OFFSET = 0x50;

        /// <summary>
        /// Postfix for GameOverPopupController.InitCommandSelect. Two frames later (after the open
        /// read that Popup.Open schedules for the first appearance), makes the select popup the popup
        /// that owns the cursor again and, unless that open read already spoke a choice, reads the
        /// focused Load / Return to Title — the back-out read after cancelling the load popup or the
        /// back-to-title confirmation (the old per-frame UpdateCommand hook re-read it then).
        /// </summary>
        public static void GameOverPopupController_InitCommandSelect_Postfix(KeyInputGameOverPopupController __instance)
        {
            try
            {
                if (__instance != null && __instance.Pointer != IntPtr.Zero)
                    CoroutineManager.StartManaged(DelayedGameOverSelectReturn(__instance.Pointer));
            }
            catch { }
        }

        private static IEnumerator DelayedGameOverSelectReturn(IntPtr controllerPtr)
        {
            yield return null;
            yield return null;

            try
            {
                IntPtr popupPtr = Marshal.ReadIntPtr(controllerPtr + GAMEOVERPOPUPCTRL_SELECT_POPUP_OFFSET);
                if (popupPtr == IntPtr.Zero) yield break;
                var popup = new KeyInputGameOverSelectPopup(popupPtr);
                if (popup.gameObject == null || !popup.gameObject.activeInHierarchy) yield break;

                bool owned = PopupState.IsConfirmationPopupActive && PopupState.ActivePopupPtr == popupPtr;
                if (owned && PopupState.LastButtonIndex >= 0)
                    yield break;   // the open read (or a navigation) already spoke the choice

                if (!owned)
                    PopupState.SetActive("GameOverSelectPopup", popupPtr,
                        IL2CppOffsets.Popup.GAMEOVER_CMDLIST_OFFSET, IL2CppOffsets.Popup.GAMEOVER_SELECT_CURSOR_OFFSET);

                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.Popup.GAMEOVER_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero) yield break;
                int idx = new GameCursor(cursorPtr).Index;
                string choice = ReadButtonFromCommandList(popupPtr, IL2CppOffsets.Popup.GAMEOVER_CMDLIST_OFFSET, idx, out int count);
                if (string.IsNullOrWhiteSpace(choice)) yield break;

                PopupState.LastButtonIndex = idx;
                FFII_ScreenReaderMod.SpeakText(MenuPosition.Format(TextUtils.StripIconMarkup(choice.Trim()), idx, count), interrupt: false);
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
                message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

                // Own the cursor from here: Yes/No navigation (Cursor.NextIndex/PrevIndex) is routed to
                // ReadCurrentButton. Registered a frame after InitSaveLoadPopup, so the game-over select
                // popup's own Close (which clears PopupState) has already run.
                PopupState.SetActive("GameOverLoadPopup", loadPopupPtr,
                    IL2CppOffsets.Popup.GAMEOVERLOAD_CMDLIST_OFFSET, IL2CppOffsets.Popup.GAMEOVERLOAD_SELECT_CURSOR_OFFSET);

                // Append the initially-focused choice (the old per-frame UpdateCommand hook spoke it).
                string choice = null;
                IntPtr cursorPtr = Marshal.ReadIntPtr(loadPopupPtr + IL2CppOffsets.Popup.GAMEOVERLOAD_SELECT_CURSOR_OFFSET);
                if (cursorPtr != IntPtr.Zero)
                {
                    int idx = new GameCursor(cursorPtr).Index;
                    choice = ReadButtonFromCommandList(loadPopupPtr, IL2CppOffsets.Popup.GAMEOVERLOAD_CMDLIST_OFFSET, idx, out int count);
                    if (!string.IsNullOrWhiteSpace(choice))
                    {
                        choice = MenuPosition.Format(TextUtils.StripIconMarkup(choice.Trim()), idx, count);
                        PopupState.LastButtonIndex = idx;
                    }
                    else
                    {
                        choice = null;
                    }
                }

                string announcement = message != null && choice != null ? $"{message}. {choice}" : (message ?? choice);
                if (!string.IsNullOrWhiteSpace(announcement))
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch { }
        }

        #endregion

        #region Title Screen (Press Any Button) - InitNone arms, SystemIndicator.Hide speaks

        /// <summary>
        /// The "Press any button" text armed by TitleWindowController.InitNone; spoken by the next
        /// SystemIndicator.Hide in a LATER frame (UpdateNone's, the moment the prompt appears).
        /// </summary>
        private static string pendingTitleText = null;

        /// <summary>True between InitNone (the title's None state) and the prompt being spoken.</summary>
        private static bool isTitleScreenTextPending = false;

        /// <summary>
        /// Frame InitNone armed the prompt. SceneTitleScreen.CreateInstance calls SystemIndicator.Hide
        /// synchronously right after building the title window (same frame as InitNone, before the
        /// fade-in); UpdateNone's Hide comes frames later, once the fade and preload are finished.
        /// </summary>
        private static int titlePromptArmFrame = -1;

        /// <summary>
        /// Postfix for KeyInput TitleWindowController.InitNone — the title's "Press any button" state
        /// entry, on boot and on every return to the title. Arms the prompt; does not speak.
        /// </summary>
        public static void TitleWindowController_InitNone_Postfix()
        {
            try
            {
                pendingTitleText = GetPressAnyButtonText();
                isTitleScreenTextPending = true;
                titlePromptArmFrame = UnityEngine.Time.frameCount;
            }
            catch { }
        }

        /// <summary>
        /// Localized "Press any button": the UiMessageConstants.MENU_TITLE_PRESS_TEXT constant when it
        /// resolves, else the mod text (unchanged from the boot path that already worked).
        /// </summary>
        private static string GetPressAnyButtonText()
        {
            try
            {
                var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                             ?? Type.GetType("UiMessageConstants, Assembly-CSharp");
                var field = uiMsgType?.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                string pressText = field?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(pressText))
                    return TextUtils.StripIconMarkup(pressText.Trim());
            }
            catch { } // best-effort; mod text below
            return T("Press any button");
        }

        /// <summary>
        /// Postfix for SystemIndicator.Hide(). Speaks the armed prompt when Hide comes from a later
        /// frame than InitNone — TitleWindowController.UpdateNone hides the indicator in the same call
        /// that shows the prompt. The same-frame Hide from SceneTitleScreen.CreateInstance is skipped.
        /// </summary>
        public static void SystemIndicator_Hide_Postfix()
        {
            try
            {
                if (!isTitleScreenTextPending || UnityEngine.Time.frameCount <= titlePromptArmFrame)
                    return;

                string text = pendingTitleText;
                DisarmTitlePrompt();
                if (!string.IsNullOrWhiteSpace(text))
                    FFII_ScreenReaderMod.SpeakText(text, interrupt: false);
            }
            catch { }
        }

        /// <summary>Drops an armed prompt (the title main menu opened, i.e. the prompt was dismissed).</summary>
        private static void DisarmTitlePrompt()
        {
            pendingTitleText = null;
            isTitleScreenTextPending = false;
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
                    // The prompt is gone; never let a stale arm speak on a later Hide (in game).
                    DisarmTitlePrompt();
                }
            }
            catch { }
        }

        #endregion
    }
}
