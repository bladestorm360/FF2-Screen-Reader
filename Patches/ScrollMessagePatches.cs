using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Patches
{
    // ============================================================
    // LineFade Per-Line Announcement System
    // Announces each line of story text as it appears on screen,
    // using the game's internal timing via PlayInit hook.
    // ============================================================

    /// <summary>
    /// Tracks LineFade message state for per-line announcements.
    /// Used for auto-scrolling story text, credits, etc.
    /// </summary>
    public static class LineFadeMessageTracker
    {
        private static string[] storedMessages = null;
        private static int currentLineIndex = 0;

        /// <summary>
        /// Store messages when SetData is called.
        /// </summary>
        public static void SetMessages(object messagesObj)
        {
            if (messagesObj == null)
            {
                storedMessages = null;
                currentLineIndex = 0;
                return;
            }

            try
            {
                var countProp = messagesObj.GetType().GetProperty("Count");
                if (countProp == null) return;

                int count = (int)countProp.GetValue(messagesObj);
                if (count == 0)
                {
                    storedMessages = null;
                    currentLineIndex = 0;
                    return;
                }

                var indexer = messagesObj.GetType().GetProperty("Item");
                if (indexer == null) return;

                storedMessages = new string[count];
                for (int i = 0; i < count; i++)
                {
                    storedMessages[i] = indexer.GetValue(messagesObj, new object[] { i }) as string;
                }
                currentLineIndex = 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error storing LineFade messages: {ex.Message}");
                storedMessages = null;
                currentLineIndex = 0;
            }
        }

        /// <summary>
        /// Get and announce the next line. Called when PlayInit fires.
        /// </summary>
        public static void AnnounceNextLine()
        {
            if (storedMessages == null || currentLineIndex >= storedMessages.Length)
            {
                return;
            }

            string line = storedMessages[currentLineIndex];
            if (!string.IsNullOrWhiteSpace(line))
            {
                string cleanLine = line.Trim();
                FFII_ScreenReaderMod.SpeakText(cleanLine, interrupt: false);
            }

            currentLineIndex++;
        }

        /// <summary>
        /// Reset the tracker.
        /// </summary>
        public static void Reset()
        {
            storedMessages = null;
            currentLineIndex = 0;
        }
    }

    /// <summary>
    /// Patches for scrolling intro/outro messages and fade messages.
    /// The intro uses ScrollMessageManager which displays scrolling text.
    /// Auto-advancing text is handled by FadeMessageManager and LineFadeMessageManager.
    /// LineFadeMessageWindowController provides per-line announcements for story text.
    /// </summary>
    public static class ScrollMessagePatches
    {
        private static string lastScrollMessage = "";

        /// <summary>
        /// Applies scroll message patches using manual Harmony patching.
        /// Patches the Manager classes which receive the actual message text as parameters.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch FadeMessageManager.Play - receives single message string
                Type fadeManagerType = FindType("Il2CppLast.Message.FadeMessageManager");
                if (fadeManagerType != null)
                {
                    var playMethod = AccessTools.Method(fadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("FadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("FadeMessageManager type not found");
                }

                // Patch LineFadeMessageManager.Play and AsyncPlay - receives List<string> messages
                Type lineFadeManagerType = FindType("Il2CppLast.Message.LineFadeMessageManager");
                if (lineFadeManagerType != null)
                {
                    // Patch Play method
                    var playMethod = AccessTools.Method(lineFadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }

                    // Patch AsyncPlay method
                    var asyncPlayMethod = AccessTools.Method(lineFadeManagerType, "AsyncPlay");
                    if (asyncPlayMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(asyncPlayMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("LineFadeMessageManager type not found");
                }

                // Patch ScrollMessageManager.Play - receives scroll message string
                Type scrollManagerType = FindType("Il2CppLast.Message.ScrollMessageManager");
                if (scrollManagerType != null)
                {
                    var playMethod = AccessTools.Method(scrollManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageManager type not found");
                }

                // Patch LineFadeMessageWindowController for per-line announcements
                Type lineFadeControllerType = FindType("Il2CppLast.UI.Message.LineFadeMessageWindowController");
                if (lineFadeControllerType != null)
                {
                    // Patch SetData to store messages
                    var setDataMethod = AccessTools.Method(lineFadeControllerType, "SetData");
                    if (setDataMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeController_SetData_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setDataMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("LineFadeMessageWindowController.SetData not found");
                    }

                    // Patch PlayInit to announce each line
                    var playInitMethod = AccessTools.Method(lineFadeControllerType, "PlayInit");
                    if (playInitMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeController_PlayInit_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playInitMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("LineFadeMessageWindowController.PlayInit not found");
                    }
                }
                else
                {
                    MelonLogger.Warning("LineFadeMessageWindowController type not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying scroll message patches: {ex.Message}");
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
        /// Postfix for FadeMessageManager.Play - captures the message parameter directly.
        /// FadeMessageManager.Play(string message, int fontSize, Color32 color, float fadeinTime, float fadeoutTime, float waitTime, bool isCenterAnchor, float postionX, float postionY)
        /// </summary>
        public static void FadeManagerPlay_Postfix(object __0)
        {
            try
            {
                // __0 is the first parameter (message string)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (message == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = message;

                // Clean up the message
                string cleanMessage = CleanMessage(message);

                // Check for duplicate location announcement
                // E.g., skip "Altair – 1F" if "Entering Altair – 1F" was just announced
                if (!LocationMessageTracker.ShouldAnnounceFadeMessage(cleanMessage))
                {
                    return;
                }

                FFII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in FadeManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageManager.Play and AsyncPlay - captures the messages list parameter.
        /// LineFadeMessageManager.Play(List<string> messages, Color32 color, float fadeinTime, float fadeoutTime, float waitTime)
        /// Note: This reads ALL lines at once. For per-line announcements, use LineFadeMessageWindowController patches.
        /// </summary>
        public static void LineFadeManagerPlay_Postfix(object __0)
        {
            try
            {
                // __0 is the first parameter (List<string> messages)
                if (__0 == null)
                {
                    return;
                }

                string combinedMessage = "";

                // Try to iterate the list
                if (__0 is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item != null)
                        {
                            string line = item.ToString();
                            if (!string.IsNullOrEmpty(line))
                            {
                                if (combinedMessage.Length > 0)
                                    combinedMessage += " ";
                                combinedMessage += line;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(combinedMessage))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (combinedMessage == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = combinedMessage;

                // Clean up the message
                string cleanMessage = CleanMessage(combinedMessage);

                FFII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ScrollMessageManager.Play - captures the message parameter.
        /// ScrollMessageManager.Play(ScrollMessageClient.ScrollType type, string message, float scrollTime, int fontSize, Color32 color, TextAnchor anchor, Rect margin)
        /// </summary>
        public static void ScrollManagerPlay_Postfix(object __1)
        {
            try
            {
                // __1 is the second parameter (message string, first is ScrollType)
                string message = __1?.ToString();
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (message == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = message;

                // Clean up the message
                string cleanMessage = CleanMessage(message);

                FFII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageWindowController.SetData - stores messages for per-line announcement.
        /// </summary>
        public static void LineFadeController_SetData_Postfix(object __0)
        {
            try
            {
                // __0 is the messages parameter (List<string>)
                LineFadeMessageTracker.SetMessages(__0);

                // Clear speaker context so next regular dialogue re-announces the speaker
                // This re-establishes context after auto-scrolling text events
                DialogueTracker.ClearLastAnnouncedSpeaker();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeController_SetData_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageWindowController.PlayInit - announces each line as it appears.
        /// PlayInit is called once per line by the game's internal state machine.
        /// </summary>
        public static void LineFadeController_PlayInit_Postfix()
        {
            try
            {
                LineFadeMessageTracker.AnnounceNextLine();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeController_PlayInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up a message by removing line breaks and extra spaces.
        /// </summary>
        private static string CleanMessage(string message)
        {
            string cleanMessage = message.Replace("\n", " ").Replace("\r", " ");
            while (cleanMessage.Contains("  "))
            {
                cleanMessage = cleanMessage.Replace("  ", " ");
            }
            return cleanMessage.Trim();
        }
    }
}
