using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for scrolling intro/outro messages and fade messages.
    /// The intro uses ScrollMessageManager which displays scrolling text.
    /// Auto-advancing text is handled by FadeMessageManager and LineFadeMessageManager.
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
                MelonLogger.Msg("Applying scroll/fade message patches...");

                // Patch FadeMessageManager.Play - receives single message string
                Type fadeManagerType = FindType("Il2CppLast.Message.FadeMessageManager");
                if (fadeManagerType != null)
                {
                    MelonLogger.Msg($"Found FadeMessageManager: {fadeManagerType.FullName}");

                    var playMethod = AccessTools.Method(fadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("FadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched FadeMessageManager.Play");
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
                    MelonLogger.Msg($"Found LineFadeMessageManager: {lineFadeManagerType.FullName}");

                    // Patch Play method
                    var playMethod = AccessTools.Method(lineFadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched LineFadeMessageManager.Play");
                    }

                    // Patch AsyncPlay method
                    var asyncPlayMethod = AccessTools.Method(lineFadeManagerType, "AsyncPlay");
                    if (asyncPlayMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(asyncPlayMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched LineFadeMessageManager.AsyncPlay");
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
                    MelonLogger.Msg($"Found ScrollMessageManager: {scrollManagerType.FullName}");

                    var playMethod = AccessTools.Method(scrollManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched ScrollMessageManager.Play");
                    }
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageManager type not found");
                }

                MelonLogger.Msg("Scroll/Fade message patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying scroll message patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
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

                MelonLogger.Msg($"[Fade Message] {cleanMessage}");
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

                MelonLogger.Msg($"[Line Fade Message] {cleanMessage}");
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

                MelonLogger.Msg($"[Scroll Message] {cleanMessage}");
                FFII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollManagerPlay_Postfix: {ex.Message}");
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
