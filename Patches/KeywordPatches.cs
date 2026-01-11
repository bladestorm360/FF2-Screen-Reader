using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputSecretWordController = Il2CppLast.UI.KeyInput.SecretWordController;
using SecretWordControllerBase = Il2CppLast.UI.SecretWordControllerBase;
using KeyInputWordsWindowController = Il2CppLast.UI.KeyInput.WordsWindowController;
using KeyInputWordsContentListController = Il2CppLast.UI.KeyInput.WordsContentListController;
using MenuManager = Il2CppLast.UI.MenuManager;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for keyword dialogue menu (Ask/Remember/Item during NPC dialogue).
    /// Follows the ItemMenuPatches pattern with state machine validation.
    /// </summary>
    public static class KeywordMenuState
    {
        public static bool IsActive { get; set; } = false;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;
        private static int lastCommandIndex = -1;
        private static int lastWordIndex = -1;

        // StateMachine offsets (from dump.cs SecretWordControllerBase)
        // stateMachine at offset 0x20
        private const int OFFSET_STATE_MACHINE = 0x20;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // SecretWordControllerBase.State values
        private const int STATE_NONE = 0;
        private const int STATE_COMMAND_SELECT = 1;
        private const int STATE_COMMAND_SELECTING = 2;
        private const int STATE_WORD_SELECT = 3;
        private const int STATE_WORD_SELECTING = 4;
        private const int STATE_ITEM_SELECT = 5;
        private const int STATE_ITEM_SELECTING = 6;
        private const int STATE_NEW_WORD_VIEW = 7;
        private const int STATE_NEW_WORD_VIEWING = 8;
        private const int STATE_END = 9;
        private const int STATE_END_WAIT = 10;

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Uses state machine to determine if we're in active selection.
        /// Auto-clears when returning to inactive state.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<KeyInputSecretWordController>();
                if (controller == null)
                {
                    ClearState();
                    return false;
                }

                int currentState = GetCurrentState(controller);

                // If state is None or End, menu is closing - clear and don't suppress
                if (currentState == STATE_NONE || currentState == STATE_END ||
                    currentState == STATE_END_WAIT || currentState < 0)
                {
                    ClearState();
                    return false;
                }

                // We're in an active state - suppress generic cursor
                return true;
            }
            catch
            {
                ClearState();
                return false;
            }
        }

        /// <summary>
        /// Reads the current state from SecretWordControllerBase's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        public static int GetCurrentState(KeyInputSecretWordController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read stateMachine pointer at offset 0x20
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }

        public static void ClearState()
        {
            if (IsActive)
            {
                MelonLogger.Msg("[Keyword] State cleared");
            }
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
            lastCommandIndex = -1;
            lastWordIndex = -1;
        }

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.15f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        public static bool CommandIndexChanged(int index)
        {
            if (index == lastCommandIndex)
                return false;
            lastCommandIndex = index;
            return true;
        }

        public static bool WordIndexChanged(int index)
        {
            if (index == lastWordIndex)
                return false;
            lastWordIndex = index;
            return true;
        }

        public static string GetCommandName(int commandId)
        {
            return commandId switch
            {
                0 => "Ask",
                1 => "Learn",
                2 => "Key Items",
                3 => "Cancel",
                _ => $"Command {commandId}"
            };
        }
    }

    /// <summary>
    /// State tracker for Words menu (main menu keyword browser).
    /// Requires MenuManager.IsOpen to be true.
    /// </summary>
    public static class WordsMenuState
    {
        public static bool IsActive { get; set; } = false;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;
        private static int lastWordIndex = -1;

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Verifies MenuManager.IsOpen before suppressing.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            try
            {
                // Words menu is part of main menu - verify menu is still open
                var menuManager = MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen)
                {
                    ClearState();
                    return false;
                }

                // Verify WordsWindowController still exists and is active
                var controller = UnityEngine.Object.FindObjectOfType<KeyInputWordsWindowController>();
                if (controller == null)
                {
                    ClearState();
                    return false;
                }

                var mono = controller.TryCast<MonoBehaviour>();
                if (mono == null || mono.gameObject == null || !mono.gameObject.activeInHierarchy)
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

        public static void ClearState()
        {
            if (IsActive)
            {
                MelonLogger.Msg("[Words] State cleared");
            }
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
            lastWordIndex = -1;
        }

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.15f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        public static bool WordIndexChanged(int index)
        {
            if (index == lastWordIndex)
                return false;
            lastWordIndex = index;
            return true;
        }
    }

    /// <summary>
    /// Harmony patches for keyword system following ItemMenuPatches pattern.
    /// </summary>
    public static class KeywordPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                MelonLogger.Msg("[Keyword] Applying keyword patches...");

                // Patch SecretWordController.SelectCommand for command bar navigation
                var selectCommandMethod = AccessTools.Method(
                    typeof(KeyInputSecretWordController),
                    "SelectCommand",
                    new Type[] { typeof(int) });

                if (selectCommandMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(SelectCommand_Postfix));
                    harmony.Patch(selectCommandMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Keyword] Patched SecretWordController.SelectCommand");
                }
                else
                {
                    MelonLogger.Warning("[Keyword] Could not find SelectCommand method");
                }

                // Patch SecretWordController.SelectContentByWord for keyword list navigation
                var selectContentMethod = AccessTools.Method(
                    typeof(KeyInputSecretWordController),
                    "SelectContentByWord");

                if (selectContentMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(SelectContentByWord_Postfix));
                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Keyword] Patched SecretWordController.SelectContentByWord");
                }
                else
                {
                    MelonLogger.Warning("[Keyword] Could not find SelectContentByWord method");
                }

                // Patch WordsContentListController.SetSelectContent for Words menu navigation
                var wordsSelectMethod = AccessTools.Method(
                    typeof(KeyInputWordsContentListController),
                    "SetSelectContent");

                if (wordsSelectMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(WordsSetSelectContent_Postfix));
                    harmony.Patch(wordsSelectMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Keyword] Patched WordsContentListController.SetSelectContent");
                }
                else
                {
                    MelonLogger.Warning("[Keyword] Could not find WordsContentListController.SetSelectContent method");
                }

                isPatched = true;
                MelonLogger.Msg("[Keyword] Keyword patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Keyword] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for command selection (Ask/Learn/Key Items/Cancel).
        /// </summary>
        public static void SelectCommand_Postfix(KeyInputSecretWordController __instance, int index)
        {
            try
            {
                if (!KeywordMenuState.CommandIndexChanged(index))
                    return;

                string commandName = KeywordMenuState.GetCommandName(index);

                if (!KeywordMenuState.ShouldAnnounce(commandName))
                    return;

                // Set active state AFTER validation
                FFII_ScreenReaderMod.ClearOtherMenuStates("Keyword");
                KeywordMenuState.IsActive = true;

                MelonLogger.Msg($"[Keyword] Command: {commandName}");
                FFII_ScreenReaderMod.SpeakText(commandName, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] SelectCommand error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for keyword list navigation.
        /// </summary>
        public static void SelectContentByWord_Postfix(KeyInputSecretWordController __instance)
        {
            try
            {
                // Get the cursor index from selectContentCursor field
                int index = GetContentCursorIndex(__instance);
                if (index < 0)
                    return;

                if (!KeywordMenuState.WordIndexChanged(index))
                    return;

                // Get keyword name from wordDataList
                string keywordName = GetKeywordAtIndex(__instance, index);
                if (string.IsNullOrEmpty(keywordName))
                    return;

                if (!KeywordMenuState.ShouldAnnounce(keywordName))
                    return;

                // Set active state AFTER validation
                FFII_ScreenReaderMod.ClearOtherMenuStates("Keyword");
                KeywordMenuState.IsActive = true;

                MelonLogger.Msg($"[Keyword] Word: {keywordName}");
                FFII_ScreenReaderMod.SpeakText(keywordName, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] SelectContentByWord error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for Words menu content selection.
        /// </summary>
        public static void WordsSetSelectContent_Postfix(KeyInputWordsContentListController __instance)
        {
            try
            {
                // Verify MenuManager is open before processing
                var menuManager = MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen)
                    return;

                // Get cursor index
                int index = GetWordsContentCursorIndex(__instance);
                if (index < 0)
                    return;

                if (!WordsMenuState.WordIndexChanged(index))
                    return;

                // Get keyword name
                string keywordName = GetWordsKeywordAtIndex(__instance, index);
                if (string.IsNullOrEmpty(keywordName))
                    return;

                if (!WordsMenuState.ShouldAnnounce(keywordName))
                    return;

                // Set active state AFTER validation
                FFII_ScreenReaderMod.ClearOtherMenuStates("Words");
                WordsMenuState.IsActive = true;

                MelonLogger.Msg($"[Words] Keyword: {keywordName}");
                FFII_ScreenReaderMod.SpeakText(keywordName, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Words] SetSelectContent error: {ex.Message}");
            }
        }

        // Offset for selectContentCursor in SecretWordControllerBase (0x30 from dump.cs)
        private const int OFFSET_SELECT_CONTENT_CURSOR = 0x30;
        // Offset for Cursor.Index property backing field
        private const int OFFSET_CURSOR_INDEX = 0x20;

        private static int GetContentCursorIndex(KeyInputSecretWordController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read selectContentCursor at offset 0x30
                    IntPtr cursorPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_SELECT_CONTENT_CURSOR);
                    if (cursorPtr == IntPtr.Zero)
                        return -1;

                    // Read Index at offset 0x20 (typical backing field location)
                    int index = *(int*)((byte*)cursorPtr.ToPointer() + OFFSET_CURSOR_INDEX);
                    return index;
                }
            }
            catch
            {
                return -1;
            }
        }

        private static string GetKeywordAtIndex(KeyInputSecretWordController controller, int index)
        {
            try
            {
                // Use reflection to get wordDataList from base class
                var baseType = typeof(SecretWordControllerBase);
                var wordDataListField = baseType.GetField("wordDataList",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (wordDataListField == null)
                    return null;

                var wordDataList = wordDataListField.GetValue(controller);
                if (wordDataList == null)
                    return null;

                // Iterate to find item at index
                var enumerator = ((System.Collections.IEnumerable)wordDataList).GetEnumerator();
                int currentIndex = 0;
                while (enumerator.MoveNext())
                {
                    if (currentIndex == index)
                    {
                        var data = enumerator.Current;
                        if (data != null)
                        {
                            var nameMessageIdProp = data.GetType().GetProperty("NameMessageId");
                            if (nameMessageIdProp != null)
                            {
                                string messageId = nameMessageIdProp.GetValue(data) as string;
                                if (!string.IsNullOrEmpty(messageId))
                                {
                                    var messageManager = MessageManager.Instance;
                                    if (messageManager != null)
                                    {
                                        return TextUtils.StripIconMarkup(
                                            messageManager.GetMessage(messageId, false));
                                    }
                                }
                            }
                        }
                        break;
                    }
                    currentIndex++;
                }
            }
            catch { }

            return null;
        }

        private static int GetWordsContentCursorIndex(KeyInputWordsContentListController controller)
        {
            try
            {
                // Try to get cursor via reflection
                var cursorField = controller.GetType().GetField("cursor",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (cursorField != null)
                {
                    var cursor = cursorField.GetValue(controller) as GameCursor;
                    if (cursor != null)
                    {
                        return cursor.Index;
                    }
                }

                // Try selectCursor
                var selectCursorField = controller.GetType().GetField("selectCursor",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (selectCursorField != null)
                {
                    var cursor = selectCursorField.GetValue(controller) as GameCursor;
                    if (cursor != null)
                    {
                        return cursor.Index;
                    }
                }
            }
            catch { }

            return -1;
        }

        private static string GetWordsKeywordAtIndex(KeyInputWordsContentListController controller, int index)
        {
            try
            {
                // Try to get contentDataList via reflection
                var dataListField = controller.GetType().GetField("contentDataList",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (dataListField == null)
                {
                    dataListField = controller.GetType().GetField("dataList",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                if (dataListField == null)
                    return null;

                var dataList = dataListField.GetValue(controller);
                if (dataList == null)
                    return null;

                // Iterate to find item at index
                var enumerator = ((System.Collections.IEnumerable)dataList).GetEnumerator();
                int currentIndex = 0;
                while (enumerator.MoveNext())
                {
                    if (currentIndex == index)
                    {
                        var data = enumerator.Current;
                        if (data != null)
                        {
                            // Try NameMessageId property
                            var nameMessageIdProp = data.GetType().GetProperty("NameMessageId");
                            if (nameMessageIdProp != null)
                            {
                                string messageId = nameMessageIdProp.GetValue(data) as string;
                                if (!string.IsNullOrEmpty(messageId))
                                {
                                    var messageManager = MessageManager.Instance;
                                    if (messageManager != null)
                                    {
                                        return TextUtils.StripIconMarkup(
                                            messageManager.GetMessage(messageId, false));
                                    }
                                }
                            }

                            // Try Name property directly
                            var nameProp = data.GetType().GetProperty("Name");
                            if (nameProp != null)
                            {
                                string name = nameProp.GetValue(data) as string;
                                if (!string.IsNullOrEmpty(name))
                                {
                                    return TextUtils.StripIconMarkup(name);
                                }
                            }
                        }
                        break;
                    }
                    currentIndex++;
                }
            }
            catch { }

            return null;
        }
    }
}
