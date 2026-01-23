using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.AnnouncementDeduplicator;
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
        /// <summary>
        /// True when keyword menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.KEYWORD_MENU);

        // Context keys for index-based deduplication
        private const string CONTEXT_COMMAND_INDEX = "Keyword.CommandIndex";
        private const string CONTEXT_WORD_INDEX = "Keyword.WordIndex";

        /// <summary>
        /// Sets the keyword menu as active, clearing other menu states.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.KEYWORD_MENU);
        }

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// State is cleared by transition patch when menu closes.
        /// </summary>
        public static bool ShouldSuppress() => IsActive;

        public static void ClearState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.KEYWORD_MENU);
            AnnouncementDeduplicator.Reset(CONTEXT_KEYWORD_COMMAND, CONTEXT_KEYWORD_WORD, CONTEXT_COMMAND_INDEX, CONTEXT_WORD_INDEX);
        }

        public static bool CommandIndexChanged(int index)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_COMMAND_INDEX, index);
        }

        public static bool WordIndexChanged(int index)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_WORD_INDEX, index);
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
        /// <summary>
        /// True when words menu is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.WORDS_MENU);

        // Context key for index-based deduplication
        private const string CONTEXT_WORD_INDEX = "WordsMenu.WordIndex";

        /// <summary>
        /// Sets the words menu as active, clearing other menu states.
        /// </summary>
        public static void SetActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.WORDS_MENU);
        }

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// State is cleared by transition patch when menu closes.
        /// </summary>
        public static bool ShouldSuppress() => IsActive;

        public static void ClearState()
        {
            MenuStateRegistry.Reset(MenuStateRegistry.WORDS_MENU);
            AnnouncementDeduplicator.Reset(CONTEXT_WORDS_MENU, CONTEXT_WORD_INDEX);
        }

        public static bool WordIndexChanged(int index)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_WORD_INDEX, index);
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
                    MelonLogger.Warning("[Keyword] Could not find SelectCommand method");
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
                    MelonLogger.Warning("[Keyword] Could not find SelectContentByWord method");
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
                    MelonLogger.Warning("[Keyword] Could not find SelectContentByItem method");
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
                    MelonLogger.Warning("[Keyword] Could not find WordsContentListController.SetDescriptionText method");
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
        /// </summary>
        public static void SelectCommand_Postfix(KeyInputSecretWordController __instance, int index)
        {
            try
            {
                if (!KeywordMenuState.CommandIndexChanged(index))
                    return;

                string commandName = KeywordMenuState.GetCommandName(index);

                if (!ShouldAnnounce(CONTEXT_KEYWORD_COMMAND, commandName))
                    return;

                // Set active state AFTER validation
                KeywordMenuState.SetActive();

                // Use interrupt: false to avoid cutting off NPC intro dialogue
                FFII_ScreenReaderMod.SpeakText(commandName, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] SelectCommand error: {ex.Message}");
            }
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

                if (!KeywordMenuState.WordIndexChanged(index))
                    return;

                string keywordAnnouncement = GetKeywordAtIndex(__instance, index);
                if (string.IsNullOrEmpty(keywordAnnouncement))
                    return;

                if (!ShouldAnnounce(CONTEXT_KEYWORD_WORD, keywordAnnouncement))
                    return;

                KeywordMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(keywordAnnouncement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] SelectContentByWord error: {ex.Message}");
            }
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

                if (!KeywordMenuState.WordIndexChanged(index))
                    return;

                string itemAnnouncement = GetItemAtIndex(__instance, index);
                if (string.IsNullOrEmpty(itemAnnouncement))
                    return;

                if (!ShouldAnnounce(CONTEXT_KEYWORD_WORD, itemAnnouncement))
                    return;

                KeywordMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(itemAnnouncement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] SelectContentByItem error: {ex.Message}");
            }
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

                if (!WordsMenuState.WordIndexChanged(index))
                    return;

                // Get keyword name and description from keyWordContentDictionary
                // This uses the same data source as the Ask menu
                string keywordAnnouncement = GetWordsKeywordFromDictionary(__instance, index);
                if (string.IsNullOrEmpty(keywordAnnouncement))
                    return;

                if (!ShouldAnnounce(CONTEXT_WORDS_MENU, keywordAnnouncement))
                    return;

                WordsMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(keywordAnnouncement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Words] SetDescriptionText error: {ex.Message}");
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

                if (!WordsMenuState.WordIndexChanged(id))
                    return;

                // Get keyword name from Touch controller
                string keywordAnnouncement = GetWordsTouchKeywordAtIndex(__instance, id);
                if (string.IsNullOrEmpty(keywordAnnouncement))
                    return;

                if (!ShouldAnnounce(CONTEXT_WORDS_MENU, keywordAnnouncement))
                    return;

                WordsMenuState.SetActive();
                FFII_ScreenReaderMod.SpeakText(keywordAnnouncement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Words Touch] SetSelectContent error: {ex.Message}");
            }
        }

        // SecretWordControllerBase offsets (from dump.cs)
        private const int OFFSET_SELECT_CONTENT_CURSOR = 0x30;
        private const int OFFSET_WORD_DATA_LIST = 0x60;     // IEnumerable<SelectFieldContentData>
        private const int OFFSET_ITEM_DATA_LIST = 0x68;     // IEnumerable<ItemListContentData>

        // SelectFieldContentData offsets
        private const int OFFSET_SFCD_NAME_MESSAGE_ID = 0x18;
        private const int OFFSET_SFCD_DESCRIPTION_MESSAGE_ID = 0x20;

        // ItemListContentData offsets
        private const int OFFSET_ILCD_NAME = 0x20;
        private const int OFFSET_ILCD_DESCRIPTION = 0x28;

        // Cursor.Index offset
        private const int OFFSET_CURSOR_INDEX = 0x20;

        // KeyInput.WordsContentListController offsets
        private const int OFFSET_WORDS_CONTENT_LIST = 0x28;
        private const int OFFSET_WORDS_SELECT_CURSOR = 0x30;
        private const int OFFSET_WORDS_KEYWORD_DICTIONARY = 0x38;

        // CommonCommandContentController (KeyInput) offsets
        private const int OFFSET_CCCC_NAME = 0x20;

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
                    IntPtr wordDataListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_WORD_DATA_LIST);
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] GetKeywordAtIndex error: {ex.Message}");
            }

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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] FormatKeywordAnnouncement error: {ex.Message}");
            }

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
                    IntPtr itemDataListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_ITEM_DATA_LIST);
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] GetItemAtIndex error: {ex.Message}");
            }

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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Keyword] FormatItemAnnouncement error: {ex.Message}");
            }

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
                    IntPtr cursorPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_WORDS_SELECT_CURSOR);
                    if (cursorPtr == IntPtr.Zero)
                        return -1;

                    // Create managed cursor wrapper and get Index
                    var cursor = new GameCursor(cursorPtr);
                    return cursor.Index;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Words] GetWordsContentCursorIndex error: {ex.Message}");
            }

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
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_WORDS_CONTENT_LIST);
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
                    IntPtr dictPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_WORDS_KEYWORD_DICTIONARY);
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Words] GetWordsKeywordFromDictionary error: {ex.Message}");
            }

            return null;
        }

        // Touch.WordsContentListController offsets
        private const int OFFSET_TOUCH_WORDS_VIEW = 0x18;
        private const int OFFSET_TOUCH_WORDS_CONTENT_LIST = 0x20;
        private const int OFFSET_TOUCH_WORDS_SELECT_CURSOR = 0x28;

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
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TOUCH_WORDS_CONTENT_LIST);
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Words Touch] GetWordsTouchKeywordAtIndex error: {ex.Message}");
            }

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
                    IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TOUCH_WORDS_VIEW);
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
