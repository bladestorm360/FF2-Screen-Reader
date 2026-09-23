using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
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
            catch
            {
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
    /// The intro uses ScrollMessageManager, whose lines are spoken one by one on the visual scroll's
    /// timing (FF1 parity). Auto-advancing text is handled by FadeMessageManager, and
    /// LineFadeMessageWindowController provides per-line announcements for story text.
    /// All are game events, so none interrupt.
    /// </summary>
    public static class ScrollMessagePatches
    {
        private static string lastScrollMessage = "";
        private static IEnumerator activeScrollCoroutine = null;

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
                    MelonLogger.Error("FadeMessageManager type not found");
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
                    MelonLogger.Error("ScrollMessageManager type not found");
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
                        MelonLogger.Error("LineFadeMessageWindowController.SetData not found");
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
                        MelonLogger.Error("LineFadeMessageWindowController.PlayInit not found");
                    }
                }
                else
                {
                    MelonLogger.Error("LineFadeMessageWindowController type not found");
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

                FFII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch { }
        }

        /// <summary>
        /// Postfix for ScrollMessageManager.Play - captures the message parameter.
        /// ScrollMessageManager.Play(ScrollMessageClient.ScrollType type, string message, float scrollTime, int fontSize, Color32 color, TextAnchor anchor, Rect margin)
        /// __1 = message (string), __2 = scrollTime (float). Lines are spoken one at a time, paced to
        /// the linear visual scroll, instead of as one block.
        /// </summary>
        public static void ScrollManagerPlay_Postfix(object __1, object __2)
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

                // A new scroll replaces any one still being read.
                if (activeScrollCoroutine != null)
                {
                    CoroutineManager.StopManaged(activeScrollCoroutine);
                    activeScrollCoroutine = null;
                }

                float scrollTime = __2 is float st ? st : 30f;
                string[] lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                activeScrollCoroutine = SpeakScrollLinesWithTiming(lines, scrollTime);
                CoroutineManager.StartManaged(activeScrollCoroutine);
            }
            catch { }
        }

        /// <summary>
        /// Speaks scroll lines spaced evenly across the scroll's duration (the visual scroll is linear).
        /// </summary>
        private static IEnumerator SpeakScrollLinesWithTiming(string[] lines, float totalScrollTime)
        {
            float delayPerLine = totalScrollTime / (lines.Length + 1);
            float nextSpeakTime = Time.time;

            foreach (string line in lines)
            {
                string cleanLine = line.Trim();
                if (string.IsNullOrEmpty(cleanLine)) continue;

                while (Time.time < nextSpeakTime)
                    yield return null;

                FFII_ScreenReaderMod.SpeakText(cleanLine, interrupt: false);
                nextSpeakTime = Time.time + delayPerLine;
            }

            activeScrollCoroutine = null;
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
            catch { }
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
            catch { }
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
