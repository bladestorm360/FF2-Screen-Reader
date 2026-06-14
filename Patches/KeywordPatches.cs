using System;
using System.Collections;
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
using KeyInputWordsWindowController = Il2CppLast.UI.KeyInput.WordsWindowController;
using KeyInputWordsContentListController = Il2CppLast.UI.KeyInput.WordsContentListController;
using MenuManager = Il2CppLast.UI.MenuManager;
using GameCursor = Il2CppLast.UI.Cursor;
using SelectFieldContentData = Il2CppLast.UI.SelectFieldContentManager.SelectFieldContentData;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using CommonCommandContentController = Il2CppLast.UI.KeyInput.CommonCommandContentController;
using TouchWordsContentListController = Il2CppLast.UI.Touch.WordsContentListController;
using TouchWordsContentController = Il2CppLast.UI.Touch.WordsContentController;
using ContentData = Il2CppLast.Data.Master.Content;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for keyword dialogue menu (Ask/Remember/Item during NPC dialogue).
    /// Follows the ItemMenuPatches pattern with state machine validation.
    /// </summary>
    public static class KeywordMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.KEYWORD_MENU);

        static KeywordMenuState()
        {
            _helper.RegisterResetHandler();
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        /// <summary>
        /// Suppress the generic cursor reader for the entire active keyword menu
        /// (the command bar and the sub-lists are both announced by dedicated
        /// postfixes). State-validated like the shop tracker so it auto-clears when
        /// the controller is gone or the state machine returns to None.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            var controller = GameObjectCache.GetOrRefresh<KeyInputSecretWordController>();
            if (controller == null || !controller.gameObject.activeInHierarchy)
            {
                ClearState();
                return false;
            }

            int state = StateReaderHelper.ReadStateTag(controller.Pointer, IL2CppOffsets.Keyword.OFFSET_STATE_MACHINE);
            if (state == IL2CppOffsets.Keyword.STATE_NONE)
            {
                ClearState();
                return false;
            }
            return true;
        }

        public static void ClearState() => _helper.IsActive = false;

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
        // Words SetDescriptionText/UpdateView fire on open and can repeat for the same
        // focused keyword; a local single-slot index guard announces once per keyword.
        private static int _lastIndex = -1;

        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.WORDS_MENU);

        static WordsMenuState()
        {
            _helper.RegisterResetHandler(() => _lastIndex = -1);
        }

        public static bool IsActive => _helper.IsActive;

        public static void SetActive() => _helper.SetActiveExclusive();

        public static bool ShouldSuppress() => IsActive;

        public static void ClearState() => _helper.IsActive = false;

        /// <summary>True (and records it) when the focused keyword index changed.</summary>
        public static bool IsNewIndex(int index)
        {
            if (index == _lastIndex) return false;
            _lastIndex = index;
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
                // Patch SecretWordController.SelectCommand for command bar navigation
                var selectCommandMethod = AccessTools.Method(
                    typeof(KeyInputSecretWordController),
                    "SelectCommand",
                    new Type[] { typeof(int) });

                if (selectCommandMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(SelectCommand_Postfix));
                    harmony.Patch(selectCommandMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Keyword] Could not find SelectCommand method");
                }

                // Patch the command-bar interactive-entry method (CommandSelectingInit) only to
                // take ownership of the menu (engage suppression) the moment the bar appears.
                // It does NOT announce — SelectCommand fires on entry and navigation and is the
                // sole command speaker, so announcing here too would double it ("ask ask").
                var commandSelectingInit = AccessTools.Method(typeof(KeyInputSecretWordController), "CommandSelectingInit");
                if (commandSelectingInit != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(CommandSelectEntry_Postfix));
                    harmony.Patch(commandSelectingInit, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Keyword] Could not find CommandSelectingInit method");
                }

                // Patch SecretWordController.SelectContentByWord for keyword list navigation (Ask/Learn)
                var selectContentMethod = AccessTools.Method(
                    typeof(KeyInputSecretWordController),
                    "SelectContentByWord");

                if (selectContentMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(SelectContentByWord_Postfix));
                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Keyword] Could not find SelectContentByWord method");
                }

                // Patch SecretWordController.SelectContentByItem for Key Items list navigation
                var selectItemMethod = AccessTools.Method(
                    typeof(KeyInputSecretWordController),
                    "SelectContentByItem");

                if (selectItemMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(SelectContentByItem_Postfix));
                    harmony.Patch(selectItemMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Keyword] Could not find SelectContentByItem method");
                }

                // Patch WordsContentListController.SetDescriptionText for Words menu navigation (KeyInput)
                // This is called when the description is set, ensuring the text is available
                var wordsSetDescriptionMethod = AccessTools.Method(
                    typeof(KeyInputWordsContentListController),
                    "SetDescriptionText",
                    new Type[] { typeof(int) });

                if (wordsSetDescriptionMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(WordsSetDescriptionText_Postfix));
                    harmony.Patch(wordsSetDescriptionMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Error("[Keyword] Could not find WordsContentListController.SetDescriptionText method");
                }

                // Patch WordsContentListController.UpdateView (KeyInput) — fires when menu opens
                // with the keyword list, so we can announce the first keyword on entry.
                var wordsUpdateViewMethod = AccessTools.Method(
                    typeof(KeyInputWordsContentListController),
                    "UpdateView");
                if (wordsUpdateViewMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(WordsUpdateView_KeyInput_Postfix));
                    harmony.Patch(wordsUpdateViewMethod, postfix: new HarmonyMethod(postfix));
                }

                // Also try Touch version with SetSelectContent
                try
                {
                    var touchWordsController = typeof(Il2CppLast.UI.Touch.WordsContentListController);
                    var touchSetSelectMethod = AccessTools.Method(touchWordsController, "SetSelectContent");
                    if (touchSetSelectMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(WordsSetSelectContent_Touch_Postfix));
                        harmony.Patch(touchSetSelectMethod, postfix: new HarmonyMethod(postfix));
                    }

                    // Touch UpdateView for initial-focus announcement
                    var touchUpdateViewMethod = AccessTools.Method(touchWordsController, "UpdateView");
                    if (touchUpdateViewMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(KeywordPatches), nameof(WordsUpdateView_Touch_Postfix));
                        harmony.Patch(touchUpdateViewMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                catch { }

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Keyword] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for command selection (Ask/Learn/Key Items/Cancel).
        /// Also kicks off a delayed announcement of the first list entry so users
        /// hear the highlighted keyword/item on submenu entry (especially load-bearing
        /// when only one entry exists — navigation never fires).
        /// </summary>
        public static void SelectCommand_Postfix(KeyInputSecretWordController __instance, int index)
        {
            try
            {
                // Only speak the command when the controller is genuinely focused on the
                // command bar. SelectCommand can also fire as the cursor is reset while a
                // term is selected or the menu closes — those must stay silent.
                if (!IsAtCommandBar(__instance))
                    return;

                string commandName = KeywordMenuState.GetCommandName(index);
                if (string.IsNullOrEmpty(commandName))
                    return;

                KeywordMenuState.SetActive();

                // Command name only — the sub-list entry is announced when a command is
                // actually selected (SelectContentByWord/ByItem), not while arrowing.
                // interrupt: false to avoid cutting off NPC intro dialogue.
                FFII_ScreenReaderMod.SpeakText(commandName, interrupt: false);
            }
            catch { }
        }

        /// <summary>
        /// Postfix for the command-bar interactive-entry method (CommandSelectingInit). Takes
        /// ownership of the menu so the generic cursor reader is suppressed the moment the
        /// command bar appears. It does NOT announce — SelectCommand fires on entry AND
        /// navigation and is the sole command speaker, so announcing here too would double it.
        /// </summary>
        public static void CommandSelectEntry_Postfix(KeyInputSecretWordController __instance)
        {
            try
            {
                KeywordMenuState.SetActive();
            }
            catch { }
        }

        /// <summary>
        /// True only when the keyword controller's state machine is on the command bar
        /// (CommandSelect / CommandSelecting). Used to suppress command announces that
        /// would otherwise fire as the cursor resets during term-select or menu close.
        /// </summary>
        private static bool IsAtCommandBar(KeyInputSecretWordController controller)
        {
            if (controller == null)
                return false;
            int state = StateReaderHelper.ReadStateTag(controller.Pointer, IL2CppOffsets.Keyword.OFFSET_STATE_MACHINE);
            return state == IL2CppOffsets.Keyword.STATE_COMMAND_SELECT
                || state == IL2CppOffsets.Keyword.STATE_COMMAND_SELECTING;
        }

        /// <summary>
        /// Postfix for keyword list navigation (Ask/Learn submenus).
        /// </summary>
        public static void SelectContentByWord_Postfix(KeyInputSecretWordController __instance, int index)
        {
            try
            {
                if (index < 0)
                    return;

                string keywordAnnouncement = GetKeywordAtIndex(__instance, index);
                if (string.IsNullOrEmpty(keywordAnnouncement))
                    return;

                KeywordMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(keywordAnnouncement, interrupt: true);
            }
            catch { }
        }

        /// <summary>
        /// Postfix for Key Items list navigation in NPC dialogue.
        /// </summary>
        public static void SelectContentByItem_Postfix(KeyInputSecretWordController __instance, int index)
        {
            try
            {
                if (index < 0)
                    return;

                string itemAnnouncement = GetItemAtIndex(__instance, index);
                if (string.IsNullOrEmpty(itemAnnouncement))
                    return;

                KeywordMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(itemAnnouncement, interrupt: true);
            }
            catch { }
        }

        /// <summary>
        /// Postfix for Words menu SetDescriptionText (KeyInput).
        /// Called when the description text is set, ensuring it's available to announce.
        /// </summary>
        public static void WordsSetDescriptionText_Postfix(KeyInputWordsContentListController __instance, int index)
        {
            try
            {
                // Verify MenuManager is open before processing
                var menuManager = MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen)
                    return;

                if (index < 0)
                    return;

                if (!WordsMenuState.IsNewIndex(index))
                    return;

                // Get keyword name and description from keyWordContentDictionary
                // This uses the same data source as the Ask menu
                string keywordAnnouncement = GetWordsKeywordFromDictionary(__instance, index);
                if (string.IsNullOrEmpty(keywordAnnouncement))
                    return;

                WordsMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(keywordAnnouncement, interrupt: true);
            }
            catch { }
        }

        /// <summary>
        /// Postfix for Words menu UpdateView (KeyInput). Fires when the menu opens with
        /// the keyword list populated — announces the default-focused first keyword.
        /// </summary>
        public static void WordsUpdateView_KeyInput_Postfix(KeyInputWordsContentListController __instance)
        {
            try
            {
                var menuManager = MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen)
                    return;

                CoroutineManager.StartManaged(AnnounceWordsFirstKeyword_KeyInput(__instance));
            }
            catch { }
        }

        private static IEnumerator AnnounceWordsFirstKeyword_KeyInput(KeyInputWordsContentListController controller)
        {
            yield return null;
            yield return null;

            string announcement = null;
            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                // Skip if SetDescriptionText already announced the first keyword this open.
                if (!WordsMenuState.IsNewIndex(0))
                    yield break;

                announcement = GetWordsKeywordFromDictionary(controller, 0);
            }
            catch { }

            if (!string.IsNullOrEmpty(announcement))
            {
                WordsMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
        }

        /// <summary>
        /// Postfix for Words menu UpdateView (Touch). Same role as the KeyInput variant.
        /// </summary>
        public static void WordsUpdateView_Touch_Postfix(TouchWordsContentListController __instance)
        {
            try
            {
                var menuManager = MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen)
                    return;

                CoroutineManager.StartManaged(AnnounceWordsFirstKeyword_Touch(__instance));
            }
            catch { }
        }

        private static IEnumerator AnnounceWordsFirstKeyword_Touch(TouchWordsContentListController controller)
        {
            yield return null;
            yield return null;

            string announcement = null;
            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                // Skip if SetSelectContent already announced the first keyword this open.
                if (!WordsMenuState.IsNewIndex(0))
                    yield break;

                announcement = GetWordsTouchKeywordAtIndex(controller, 0);
            }
            catch { }

            if (!string.IsNullOrEmpty(announcement))
            {
                WordsMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
        }

        /// <summary>
        /// Postfix for Words menu content selection (Touch).
        /// </summary>
        public static void WordsSetSelectContent_Touch_Postfix(TouchWordsContentListController __instance, int id)
        {
            try
            {
                // Verify MenuManager is open before processing
                var menuManager = MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen)
                    return;

                if (!WordsMenuState.IsNewIndex(id))
                    return;

                // Get keyword name from Touch controller
                string keywordAnnouncement = GetWordsTouchKeywordAtIndex(__instance, id);
                if (string.IsNullOrEmpty(keywordAnnouncement))
                    return;

                WordsMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(keywordAnnouncement, interrupt: true);
            }
            catch { }
        }

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
                    IntPtr cursorPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_SELECT_CONTENT_CURSOR);
                    if (cursorPtr == IntPtr.Zero)
                        return -1;

                    // Read Index at offset 0x20 (typical backing field location)
                    int index = *(int*)((byte*)cursorPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_CURSOR_INDEX);
                    return index;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Gets keyword name and description at specified index from wordDataList.
        /// Uses pointer offsets to access IL2CPP data directly.
        /// Format: "keyword: description" or just "keyword" if no description.
        /// </summary>
        private static string GetKeywordAtIndex(KeyInputSecretWordController controller, int index)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // Read wordDataList pointer at offset 0x60
                    IntPtr wordDataListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_WORD_DATA_LIST);
                    if (wordDataListPtr == IntPtr.Zero)
                        return null;

                    // Try to access as a List by wrapping the pointer
                    // The IEnumerable is typically backed by a List at runtime
                    try
                    {
                        var listObj = new Il2CppSystem.Object(wordDataListPtr);
                        var list = listObj.TryCast<Il2CppSystem.Collections.Generic.List<SelectFieldContentData>>();

                        if (list != null && index >= 0 && index < list.Count)
                        {
                            var data = list[index];
                            if (data != null)
                            {
                                return FormatKeywordAnnouncement(data);
                            }
                        }
                    }
                    catch
                    {
                        // List cast failed - silent fail
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Formats keyword data as "keyword: description" or just "keyword".
        /// </summary>
        private static string FormatKeywordAnnouncement(SelectFieldContentData data)
        {
            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return null;

                string name = null;
                string description = null;

                // Get name from NameMessageId
                string nameMessageId = data.NameMessageId;
                if (!string.IsNullOrEmpty(nameMessageId))
                {
                    name = TextUtils.StripIconMarkup(messageManager.GetMessage(nameMessageId, false));
                }

                // Try to get description from DescriptionMessageId
                string descMessageId = data.DescriptionMessageId;
                if (!string.IsNullOrEmpty(descMessageId))
                {
                    description = TextUtils.StripIconMarkup(messageManager.GetMessage(descMessageId, false));
                }

                if (!string.IsNullOrEmpty(name))
                {
                    if (!string.IsNullOrEmpty(description))
                    {
                        return $"{name}: {description}";
                    }
                    return name;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets item name and description at specified index from itemDataList.
        /// Uses pointer offsets to access IL2CPP data directly.
        /// Format: "item name: description" or just "item name" if no description.
        /// </summary>
        private static string GetItemAtIndex(KeyInputSecretWordController controller, int index)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // Read itemDataList pointer at offset 0x68
                    IntPtr itemDataListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_ITEM_DATA_LIST);
                    if (itemDataListPtr == IntPtr.Zero)
                        return null;

                    // Try to access as a List by wrapping the pointer
                    try
                    {
                        var listObj = new Il2CppSystem.Object(itemDataListPtr);
                        var list = listObj.TryCast<Il2CppSystem.Collections.Generic.List<ItemListContentData>>();

                        if (list != null && index >= 0 && index < list.Count)
                        {
                            var data = list[index];
                            if (data != null)
                            {
                                return FormatItemAnnouncement(data);
                            }
                        }
                    }
                    catch
                    {
                        // List cast failed - silent fail
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Formats item data as "item name: description" or just "item name".
        /// </summary>
        private static string FormatItemAnnouncement(ItemListContentData data)
        {
            try
            {
                // ItemListContentData has Name and Description properties directly
                string name = data.Name;
                string description = data.Description;

                if (!string.IsNullOrEmpty(name))
                {
                    name = TextUtils.StripIconMarkup(name);

                    if (!string.IsNullOrEmpty(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        return $"{name}: {description}";
                    }
                    return name;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets cursor index from WordsContentListController using pointer offsets.
        /// </summary>
        private static int GetWordsContentCursorIndex(KeyInputWordsContentListController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read selectCursor at offset 0x30
                    IntPtr cursorPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_WORDS_SELECT_CURSOR);
                    if (cursorPtr == IntPtr.Zero)
                        return -1;

                    // Create managed cursor wrapper and get Index
                    var cursor = new GameCursor(cursorPtr);
                    return cursor.Index;
                }
            }
            catch { }

            return -1;
        }

        /// <summary>
        /// Gets keyword name and description from keyWordContentDictionary.
        /// This matches how the Ask menu accesses keyword data.
        /// Format: "keyword: description" or just "keyword" if no description.
        /// </summary>
        private static string GetWordsKeywordFromDictionary(KeyInputWordsContentListController controller, int index)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // First get the keyword ID from contentList[index]
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_WORDS_CONTENT_LIST);
                    if (contentListPtr == IntPtr.Zero)
                        return null;

                    var contentList = new Il2CppSystem.Collections.Generic.List<CommonCommandContentController>(contentListPtr);
                    if (contentList == null || index < 0 || index >= contentList.Count)
                        return null;

                    var contentItem = contentList[index];
                    if (contentItem == null)
                        return null;

                    int keywordId = contentItem.Id;

                    // Now look up the Content from keyWordContentDictionary
                    IntPtr dictPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_WORDS_KEYWORD_DICTIONARY);
                    if (dictPtr == IntPtr.Zero)
                        return null;

                    var dict = new Il2CppSystem.Collections.Generic.Dictionary<int, ContentData>(dictPtr);
                    if (dict == null)
                        return null;

                    ContentData contentData = null;
                    if (dict.ContainsKey(keywordId))
                    {
                        contentData = dict[keywordId];
                    }

                    if (contentData == null)
                    {
                        // Fallback to just the name from the UI if dictionary lookup fails
                        string fallbackName = contentItem.Name;
                        if (!string.IsNullOrEmpty(fallbackName))
                            return TextUtils.StripIconMarkup(fallbackName);
                        return null;
                    }

                    // Get name and description from Content using MessageManager
                    var messageManager = MessageManager.Instance;
                    if (messageManager == null)
                        return null;

                    string name = null;
                    string description = null;

                    string nameMessageId = contentData.MesIdName;
                    if (!string.IsNullOrEmpty(nameMessageId))
                    {
                        name = TextUtils.StripIconMarkup(messageManager.GetMessage(nameMessageId, false));
                    }

                    string descMessageId = contentData.MesIdDescription;
                    if (!string.IsNullOrEmpty(descMessageId))
                    {
                        description = TextUtils.StripIconMarkup(messageManager.GetMessage(descMessageId, false));
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        if (!string.IsNullOrEmpty(description))
                        {
                            return $"{name}: {description}";
                        }
                        return name;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets keyword name and description from Touch WordsContentListController.
        /// </summary>
        private static string GetWordsTouchKeywordAtIndex(TouchWordsContentListController controller, int index)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // Read contentList at offset 0x20
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_TOUCH_WORDS_CONTENT_LIST);
                    if (contentListPtr == IntPtr.Zero)
                        return null;

                    // Create managed list wrapper
                    var contentList = new Il2CppSystem.Collections.Generic.List<TouchWordsContentController>(contentListPtr);
                    if (contentList == null || index < 0 || index >= contentList.Count)
                        return null;

                    var content = contentList[index];
                    if (content == null)
                        return null;

                    // Get name from NameText property (returns Text component, get .text)
                    var nameTextComponent = content.NameText;
                    if (nameTextComponent == null)
                        return null;

                    string name = nameTextComponent.text;
                    if (string.IsNullOrEmpty(name))
                        return null;

                    name = TextUtils.StripIconMarkup(name);

                    // Try to get description from the view's descriptionText
                    string description = GetWordsTouchDescription(controller);
                    if (!string.IsNullOrEmpty(description))
                    {
                        return $"{name}: {description}";
                    }

                    return name;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets description text from Touch WordsContentListView.
        /// </summary>
        private static string GetWordsTouchDescription(TouchWordsContentListController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    // Read view at offset 0x18
                    IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Keyword.OFFSET_TOUCH_WORDS_VIEW);
                    if (viewPtr == IntPtr.Zero)
                        return null;

                    // Touch.WordsContentListView has descriptionText at offset 0x20
                    IntPtr descTextPtr = *(IntPtr*)((byte*)viewPtr.ToPointer() + 0x20);
                    if (descTextPtr == IntPtr.Zero)
                        return null;

                    var descText = new UnityEngine.UI.Text(descTextPtr);
                    if (descText != null && !string.IsNullOrEmpty(descText.text))
                    {
                        return TextUtils.StripIconMarkup(descText.text);
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
