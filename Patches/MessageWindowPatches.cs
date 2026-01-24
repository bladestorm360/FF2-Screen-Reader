using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppInterop.Runtime;

namespace FFII_ScreenReader.Patches
{
    // ============================================================
    // Per-Page Dialogue System with Multi-Line Support
    // Announces dialogue text page-by-page as player advances,
    // combining multiple display lines per page.
    // Uses pointer-based access for IL2CPP fields.
    // ============================================================

    /// <summary>
    /// Tracks dialogue state for per-page announcements.
    /// Stores content from SetContent, announces via PlayingInit hook.
    /// Handles multi-line pages by combining lines within page boundaries.
    /// </summary>
    public static class DialogueTracker
    {
        // Store message list for page-by-page reading
        private static List<string> currentMessageList = new List<string>();
        private static List<int> currentPageBreaks = new List<int>(); // Line indices where new pages start
        private static int lastAnnouncedPageIndex = -1;

        // Speaker tracking
        private static string currentSpeaker = "";
        private static string lastAnnouncedSpeaker = "";

        // Track if we're in a dialogue sequence
        private static bool isInDialogue = false;

        /// <summary>
        /// Known invalid speaker names (locations, menu labels, etc.)
        /// </summary>
        private static readonly string[] InvalidSpeakers = new string[]
        {
            "Load", "Save", "New Game", "Continue", "Config", "Quit",
            "Yes", "No", "OK", "Cancel"
        };

        /// <summary>
        /// Store messages and page breaks for per-page retrieval.
        /// Called from SetContent_Postfix with data read from instance.
        /// </summary>
        public static void StoreMessages(List<string> messages, List<int> pageBreaks)
        {
            currentMessageList.Clear();
            currentPageBreaks.Clear();

            if (messages == null || messages.Count == 0)
            {
                isInDialogue = false;
                return;
            }

            // Store cleaned messages
            foreach (var msg in messages)
            {
                currentMessageList.Add(msg != null ? CleanMessage(msg) : "");
            }

            // Convert page breaks (ending indices) to start indices
            // newPageLineList contains the ENDING line index (inclusive) for each page
            // e.g., [0, 2] means: page 0 ends at line 0, page 1 ends at line 2
            // We convert these to START indices for easier processing
            currentPageBreaks.Add(0); // First page always starts at line 0

            if (pageBreaks != null && pageBreaks.Count > 0)
            {
                for (int i = 0; i < pageBreaks.Count; i++)
                {
                    int nextStart = pageBreaks[i] + 1;
                    if (nextStart < currentMessageList.Count)
                    {
                        currentPageBreaks.Add(nextStart);
                    }
                }
            }

            // Reset page tracking
            lastAnnouncedPageIndex = -1;
            isInDialogue = true;
        }

        /// <summary>
        /// Set the current speaker. Will be included in announcement if changed.
        /// </summary>
        public static void SetSpeaker(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker))
                return;

            string cleanSpeaker = speaker.Trim();

            // Filter out invalid speakers (locations, menu labels)
            if (!IsValidSpeaker(cleanSpeaker))
                return;

            currentSpeaker = cleanSpeaker;
        }

        /// <summary>
        /// Check if a speaker name is valid (not a location or menu label).
        /// </summary>
        private static bool IsValidSpeaker(string speaker)
        {
            // Filter location names with separators
            if (speaker.Contains("–") || speaker.Contains("-"))
                return false;

            // Filter known invalid strings
            foreach (var invalid in InvalidSpeakers)
            {
                if (speaker.Equals(invalid, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets all lines for a given page index, combined into one string.
        /// </summary>
        public static string GetPageText(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= currentPageBreaks.Count)
                return null;

            int startLine = currentPageBreaks[pageIndex];
            int endLine = (pageIndex + 1 < currentPageBreaks.Count)
                ? currentPageBreaks[pageIndex + 1]
                : currentMessageList.Count;

            var sb = new StringBuilder();
            for (int i = startLine; i < endLine && i < currentMessageList.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(currentMessageList[i]))
                {
                    if (sb.Length > 0)
                        sb.Append(" ");
                    sb.Append(currentMessageList[i]);
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Announce the current page. Called from PlayingInit.
        /// </summary>
        public static void AnnounceForPage(int pageIndex, string speakerFromInstance)
        {
            // Update speaker from instance if available
            if (!string.IsNullOrWhiteSpace(speakerFromInstance))
            {
                SetSpeaker(speakerFromInstance);
            }

            // Skip if not in dialogue or no pages
            if (!isInDialogue || currentPageBreaks.Count == 0)
                return;

            // Skip if we've already announced this page
            if (pageIndex < 0 || pageIndex >= currentPageBreaks.Count || pageIndex == lastAnnouncedPageIndex)
                return;

            string pageText = GetPageText(pageIndex);
            if (string.IsNullOrWhiteSpace(pageText))
            {
                lastAnnouncedPageIndex = pageIndex; // Still advance past empty page
                return;
            }

            // Build announcement with speaker if changed
            string announcement;
            if (!string.IsNullOrEmpty(currentSpeaker) && currentSpeaker != lastAnnouncedSpeaker)
            {
                announcement = $"{currentSpeaker}: {pageText}";
                lastAnnouncedSpeaker = currentSpeaker;
            }
            else
            {
                announcement = pageText;
            }

            lastAnnouncedPageIndex = pageIndex;
            FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
        }

        /// <summary>
        /// Cleans up message text by removing extra whitespace and icon markup.
        /// </summary>
        private static string CleanMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            // Strip icon markup
            string clean = TextUtils.StripIconMarkup(message);

            // Normalize whitespace
            clean = clean.Replace("\n", " ").Replace("\r", " ");
            while (clean.Contains("  "))
            {
                clean = clean.Replace("  ", " ");
            }

            return clean.Trim();
        }

        /// <summary>
        /// Reset the tracker (e.g., when dialogue ends).
        /// </summary>
        public static void Reset()
        {
            currentMessageList.Clear();
            currentPageBreaks.Clear();
            lastAnnouncedPageIndex = -1;
            currentSpeaker = "";
            lastAnnouncedSpeaker = "";
            isInDialogue = false;
        }

        /// <summary>
        /// Clear last announced speaker to force re-announcement on next dialogue.
        /// Call on scene transitions and after auto-scroll events to re-establish context.
        /// </summary>
        public static void ClearLastAnnouncedSpeaker()
        {
            lastAnnouncedSpeaker = "";
        }
    }

    /// <summary>
    /// Patches for the main dialogue window (MessageWindowManager).
    /// Handles NPC dialogue, story text, and speaker announcements.
    /// Uses per-page announcement via PlayingInit hook.
    /// Uses pointer-based access for IL2CPP fields.
    /// </summary>
    public static class MessageWindowPatches
    {
        // Memory offsets for MessageWindowManager (from dump.cs)
        private const int OFFSET_MESSAGE_LIST = 0x88;        // List<string> messageList
        private const int OFFSET_NEW_PAGE_LINE_LIST = 0xA0;  // List<int> newPageLineList
        private const int OFFSET_SPEAKER_VALUE = 0xA8;       // string spekerValue
        private const int OFFSET_CURRENT_PAGE_NUMBER = 0xF8; // int currentPageNumber

        /// <summary>
        /// Applies message window patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type managerType = FindType("Il2CppLast.Message.MessageWindowManager");
                if (managerType == null)
                {
                    MelonLogger.Warning("MessageWindowManager type not found");
                    return;
                }

                MelonLogger.Msg($"[MessageWindow] Found MessageWindowManager: {managerType.FullName}");

                // Patch SetContent - stores dialogue pages for per-page retrieval
                var setContentMethod = AccessTools.Method(managerType, "SetContent");
                if (setContentMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("SetContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.SetContent");
                }
                else
                {
                    MelonLogger.Warning("[MessageWindow] SetContent method not found");
                }

                // Patch SetSpeker - stores speaker name for announcement
                var setSpekerMethod = AccessTools.Method(managerType, "SetSpeker");
                if (setSpekerMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("SetSpeker_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setSpekerMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.SetSpeker");
                }
                else
                {
                    MelonLogger.Warning("[MessageWindow] SetSpeker method not found");
                }

                // Patch PlayingInit - fires once per page, triggers announcement
                var playingInitMethod = AccessTools.Method(managerType, "PlayingInit");
                if (playingInitMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("PlayingInit_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playingInitMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.PlayingInit");
                }
                else
                {
                    MelonLogger.Warning("[MessageWindow] PlayingInit method not found");
                }

                // Patch Close - resets dialogue state
                var closeMethod = AccessTools.Method(managerType, "Close");
                if (closeMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("Close_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.Close");
                }
                else
                {
                    MelonLogger.Warning("[MessageWindow] Close method not found");
                }

                MelonLogger.Msg("[MessageWindow] Message window patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MessageWindow] Error applying message window patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Reads the messageList field from a manager instance using pointer-based access.
        /// </summary>
        private static List<string> ReadMessageListFromInstance(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    IntPtr listPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_MESSAGE_LIST);
                    if (listPtr == IntPtr.Zero)
                        return null;

                    var il2cppList = new Il2CppSystem.Collections.Generic.List<string>(listPtr);
                    if (il2cppList == null)
                        return null;

                    var result = new List<string>();
                    int count = il2cppList.Count;

                    for (int i = 0; i < count; i++)
                    {
                        var msg = il2cppList[i];
                        result.Add(msg ?? "");
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading messageList: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads the newPageLineList field from a manager instance using pointer-based access.
        /// </summary>
        private static List<int> ReadPageBreaksFromInstance(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    IntPtr listPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_NEW_PAGE_LINE_LIST);
                    if (listPtr == IntPtr.Zero)
                        return null;

                    var il2cppList = new Il2CppSystem.Collections.Generic.List<int>(listPtr);
                    if (il2cppList == null)
                        return null;

                    var result = new List<int>();
                    int count = il2cppList.Count;

                    for (int i = 0; i < count; i++)
                    {
                        result.Add(il2cppList[i]);
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading newPageLineList: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads the spekerValue field from a manager instance using pointer-based access.
        /// </summary>
        private static string ReadSpeakerFromInstance(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                unsafe
                {
                    IntPtr stringPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_SPEAKER_VALUE);
                    if (stringPtr == IntPtr.Zero)
                        return null;

                    return IL2CPP.Il2CppStringToManaged(stringPtr);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading speaker: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the current page number from MessageWindowManager instance using pointer-based access.
        /// </summary>
        private static int GetCurrentPageNumber(object instance)
        {
            if (instance == null)
                return -1;

            try
            {
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return -1;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    int pageNum = *(int*)((byte*)instancePtr.ToPointer() + OFFSET_CURRENT_PAGE_NUMBER);
                    return pageNum;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.SetContent - stores dialogue pages.
        /// Reads messageList and newPageLineList from instance for multi-line page support.
        /// </summary>
        public static void SetContent_Postfix(object __instance)
        {
            try
            {
                // Read messageList from the instance (contains all dialogue lines)
                var messageList = ReadMessageListFromInstance(__instance);

                // Read page breaks from the instance
                var pageBreaks = ReadPageBreaksFromInstance(__instance);

                // Store in tracker for per-page retrieval
                DialogueTracker.StoreMessages(messageList, pageBreaks);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in SetContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.SetSpeker - stores speaker name.
        /// Speaker is announced with the next dialogue page if changed.
        /// </summary>
        public static void SetSpeker_Postfix(object __0)
        {
            try
            {
                string speaker = __0 as string;
                if (!string.IsNullOrWhiteSpace(speaker))
                {
                    DialogueTracker.SetSpeaker(speaker);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in SetSpeker_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.PlayingInit - announces current page.
        /// Fires once per page when text starts displaying.
        /// Gets current page number from instance and combines multi-line content.
        /// </summary>
        public static void PlayingInit_Postfix(object __instance)
        {
            try
            {
                // Get current page number from instance
                int currentPage = GetCurrentPageNumber(__instance);

                // Get speaker from instance (fallback)
                string speaker = ReadSpeakerFromInstance(__instance);

                // Announce the page
                DialogueTracker.AnnounceForPage(currentPage, speaker);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in PlayingInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the tracking state. Call when scene changes.
        /// </summary>
        public static void ResetTracking()
        {
            DialogueTracker.Reset();
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Close - resets dialogue state.
        /// Ensures the same NPC dialogue can be announced on subsequent interactions.
        /// Also triggers entity refresh to update NPC/interactive object states.
        /// </summary>
        public static void Close_Postfix()
        {
            // Reset dialogue state for next conversation
            DialogueTracker.Reset();

            // Trigger entity refresh after dialogue ends (NPC interaction complete)
            FFII_ScreenReader.Core.FFII_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }
}
