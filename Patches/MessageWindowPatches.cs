using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Patches
{
    // ============================================================
    // Per-Page Dialogue System
    // Announces dialogue text page-by-page as player advances,
    // formatted as "Speaker: Text" with speaker announced only on change.
    // ============================================================

    /// <summary>
    /// Tracks dialogue state for per-page announcements.
    /// Stores content from SetContent, announces via PlayingInit hook.
    /// </summary>
    public static class DialogueTracker
    {
        private static string[] storedMessages = null;
        private static string currentSpeaker = "";
        private static string lastAnnouncedSpeaker = "";
        private static int nextAnnouncementIndex = 0;  // Our own index, not the game's stale messageLineIndex

        /// <summary>
        /// Known invalid speaker names (locations, menu labels, etc.)
        /// </summary>
        private static readonly string[] InvalidSpeakers = new string[]
        {
            "Load", "Save", "New Game", "Continue", "Config", "Quit",
            "Yes", "No", "OK", "Cancel"
        };

        /// <summary>
        /// Store content from SetContent for per-page retrieval.
        /// Uses reflection to access BaseContent.ContentText property.
        /// </summary>
        public static void StoreContent(object contentList)
        {
            if (contentList == null)
            {
                storedMessages = null;
                return;
            }

            try
            {
                var countProp = contentList.GetType().GetProperty("Count");
                if (countProp == null) return;

                int count = (int)countProp.GetValue(contentList);
                if (count == 0)
                {
                    storedMessages = null;
                    return;
                }

                var indexer = contentList.GetType().GetProperty("Item");
                if (indexer == null) return;

                // Extract text from each content item
                var messages = new System.Collections.Generic.List<string>();
                for (int i = 0; i < count; i++)
                {
                    var content = indexer.GetValue(contentList, new object[] { i });
                    if (content != null)
                    {
                        // Get ContentText property from BaseContent
                        var contentTextProp = content.GetType().GetProperty("ContentText");
                        if (contentTextProp != null)
                        {
                            string text = contentTextProp.GetValue(content) as string;
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                messages.Add(text.Trim());
                            }
                        }
                    }
                }

                storedMessages = messages.ToArray();
                nextAnnouncementIndex = 0; // Reset our index for new dialogue
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error storing content: {ex.Message}");
                storedMessages = null;
            }
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
        /// Announce the current page. Called from PlayingInit.
        /// Uses our own nextAnnouncementIndex instead of game's stale messageLineIndex.
        /// </summary>
        public static void AnnounceForLineIndex(int gameLineIndex, string speakerFromInstance)
        {
            // Update speaker from instance if available
            if (!string.IsNullOrWhiteSpace(speakerFromInstance))
            {
                SetSpeaker(speakerFromInstance);
            }

            // Skip if no stored messages
            if (storedMessages == null || storedMessages.Length == 0)
                return;

            // Use our own index instead of game's potentially stale messageLineIndex
            int localIndex = nextAnnouncementIndex;

            // Skip if we've already announced all pages
            if (localIndex >= storedMessages.Length)
                return;

            string text = storedMessages[localIndex];
            if (string.IsNullOrWhiteSpace(text))
            {
                nextAnnouncementIndex++; // Still advance past empty page
                return;
            }

            // Clean up the text
            string cleanText = TextUtils.StripIconMarkup(text);
            if (string.IsNullOrWhiteSpace(cleanText))
            {
                nextAnnouncementIndex++;
                return;
            }

            // Build announcement with speaker if changed
            string announcement;
            if (!string.IsNullOrEmpty(currentSpeaker) && currentSpeaker != lastAnnouncedSpeaker)
            {
                announcement = $"{currentSpeaker}: {cleanText}";
                lastAnnouncedSpeaker = currentSpeaker;
            }
            else
            {
                announcement = cleanText;
            }

            FFII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            nextAnnouncementIndex++;
        }

        /// <summary>
        /// Reset the tracker (e.g., when dialogue ends).
        /// </summary>
        public static void Reset()
        {
            storedMessages = null;
            currentSpeaker = "";
            lastAnnouncedSpeaker = "";
            nextAnnouncementIndex = 0;
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
    /// </summary>
    public static class MessageWindowPatches
    {
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

                // Patch SetContent - stores dialogue pages for per-page retrieval
                var setContentMethod = AccessTools.Method(managerType, "SetContent");
                if (setContentMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("SetContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setContentMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("SetContent method not found");
                }

                // Patch SetSpeker - stores speaker name for announcement
                var setSpekerMethod = AccessTools.Method(managerType, "SetSpeker");
                if (setSpekerMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("SetSpeker_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setSpekerMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("SetSpeker method not found");
                }

                // Patch PlayingInit - fires once per page, triggers announcement
                var playingInitMethod = AccessTools.Method(managerType, "PlayingInit");
                if (playingInitMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("PlayingInit_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playingInitMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("PlayingInit method not found");
                }

                // Patch Close - resets dialogue state
                var closeMethod = AccessTools.Method(managerType, "Close");
                if (closeMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("Close_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("Close method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying message window patches: {ex.Message}");
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
        /// Postfix for MessageWindowManager.SetContent - stores dialogue pages.
        /// Content is stored for per-page announcement via PlayingInit.
        /// </summary>
        public static void SetContent_Postfix(object __instance, object __0)
        {
            try
            {
                // __0 is the contentList parameter (List<BaseContent>)
                DialogueTracker.StoreContent(__0);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetContent_Postfix: {ex.Message}");
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
                // __0 is the name parameter (string)
                string speaker = __0 as string;
                if (!string.IsNullOrWhiteSpace(speaker))
                {
                    DialogueTracker.SetSpeaker(speaker);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetSpeker_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.PlayingInit - announces current page.
        /// Fires once per page when text starts displaying.
        /// </summary>
        public static void PlayingInit_Postfix(object __instance)
        {
            try
            {
                var managerType = __instance.GetType();

                // Get messageLineIndex from instance
                int lineIndex = 0;
                var lineIndexField = AccessTools.Field(managerType, "messageLineIndex");
                if (lineIndexField != null)
                {
                    lineIndex = (int)lineIndexField.GetValue(__instance);
                }

                // Get spekerValue from instance (fallback for speaker)
                string speaker = null;
                var spekerField = AccessTools.Field(managerType, "spekerValue");
                if (spekerField != null)
                {
                    speaker = spekerField.GetValue(__instance) as string;
                }

                DialogueTracker.AnnounceForLineIndex(lineIndex, speaker);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in PlayingInit_Postfix: {ex.Message}");
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
