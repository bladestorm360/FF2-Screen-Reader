using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the main dialogue window (MessageWindowManager).
    /// Handles NPC dialogue, story text, and speaker announcements.
    /// </summary>
    public static class MessageWindowPatches
    {
        private static string lastDialogueMessage = "";
        private static string lastSpeaker = "";
        private static string pendingDialogue = "";  // Buffer dialogue until speaker is announced

        /// <summary>
        /// Applies message window patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("Applying message window patches...");

                Type managerType = FindType("Il2CppLast.Message.MessageWindowManager");
                if (managerType == null)
                {
                    MelonLogger.Warning("MessageWindowManager type not found");
                    return;
                }

                MelonLogger.Msg($"Found MessageWindowManager: {managerType.FullName}");

                // Patch SetContent - called when dialogue text is set
                var setContentMethod = AccessTools.Method(managerType, "SetContent");
                if (setContentMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("SetContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched SetContent");
                }
                else
                {
                    MelonLogger.Warning("SetContent method not found");
                }

                // Patch Play - called when dialogue starts playing
                var playMethod = AccessTools.Method(managerType, "Play");
                if (playMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("Play_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched Play");
                }
                else
                {
                    MelonLogger.Warning("Play method not found");
                }

                // Patch Close - called when dialogue window closes
                // Triggers entity refresh to update NPC/interactive object states after interaction
                var closeMethod = AccessTools.Method(managerType, "Close");
                if (closeMethod != null)
                {
                    var postfix = typeof(MessageWindowPatches).GetMethod("Close_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched Close for entity refresh");
                }
                else
                {
                    MelonLogger.Warning("Close method not found");
                }

                MelonLogger.Msg("Message window patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying message window patches: {ex.Message}");
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
        /// Postfix for MessageWindowManager.SetContent - reads dialogue text.
        /// </summary>
        public static void SetContent_Postfix(object __instance)
        {
            try
            {
                var managerType = __instance.GetType();

                // Try to get messageList field/property
                var messageListField = AccessTools.Field(managerType, "messageList");
                if (messageListField == null)
                {
                    var messageListProp = AccessTools.Property(managerType, "messageList");
                    if (messageListProp == null) return;

                    var listObj = messageListProp.GetValue(__instance);
                    ReadMessageList(listObj);
                    return;
                }

                var messageList = messageListField.GetValue(__instance);
                ReadMessageList(messageList);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads and announces messages from a message list.
        /// </summary>
        private static void ReadMessageList(object messageListObj)
        {
            if (messageListObj == null) return;

            try
            {
                var countProp = messageListObj.GetType().GetProperty("Count");
                if (countProp == null) return;

                int count = (int)countProp.GetValue(messageListObj);
                if (count == 0) return;

                var indexer = messageListObj.GetType().GetProperty("Item");
                if (indexer == null) return;

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    var msg = indexer.GetValue(messageListObj, new object[] { i }) as string;
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        sb.AppendLine(msg.Trim());
                    }
                }

                string fullText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(fullText) && fullText != lastDialogueMessage)
                {
                    lastDialogueMessage = fullText;
                    string cleanMessage = TextUtils.StripIconMarkup(fullText);
                    if (!string.IsNullOrWhiteSpace(cleanMessage))
                    {
                        // Buffer the dialogue - it will be spoken after speaker name in Play_Postfix
                        pendingDialogue = cleanMessage;
                        MelonLogger.Msg($"[Dialogue Buffered] {cleanMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading message list: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Play - reads speaker name.
        /// </summary>
        public static void Play_Postfix(object __instance)
        {
            try
            {
                var managerType = __instance.GetType();

                // Note: game code has typo "speker" not "speaker"
                var spekerField = AccessTools.Field(managerType, "spekerValue");
                if (spekerField == null)
                {
                    var spekerProp = AccessTools.Property(managerType, "spekerValue");
                    if (spekerProp != null)
                    {
                        var spekerValue = spekerProp.GetValue(__instance) as string;
                        AnnounceSpeaker(spekerValue);
                    }
                    return;
                }

                var speaker = spekerField.GetValue(__instance) as string;
                AnnounceSpeaker(speaker);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in Play_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the speaker name if it changed, then speaks any pending dialogue.
        /// </summary>
        private static void AnnounceSpeaker(string spekerValue)
        {
            // Announce speaker if changed
            if (!string.IsNullOrWhiteSpace(spekerValue) && spekerValue != lastSpeaker)
            {
                lastSpeaker = spekerValue;
                string cleanSpeaker = TextUtils.StripIconMarkup(spekerValue);
                if (!string.IsNullOrWhiteSpace(cleanSpeaker))
                {
                    MelonLogger.Msg($"[Speaker] {cleanSpeaker}");
                    FFII_ScreenReaderMod.SpeakText(cleanSpeaker, interrupt: false);
                }
            }

            // Now speak the pending dialogue (buffered from SetContent)
            if (!string.IsNullOrWhiteSpace(pendingDialogue))
            {
                MelonLogger.Msg($"[Dialogue] {pendingDialogue}");
                FFII_ScreenReaderMod.SpeakText(pendingDialogue, interrupt: false);
                pendingDialogue = "";  // Clear after speaking
            }
        }

        /// <summary>
        /// Resets the tracking state. Call when scene changes.
        /// </summary>
        public static void ResetTracking()
        {
            lastDialogueMessage = "";
            lastSpeaker = "";
            pendingDialogue = "";
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Close - clears dialogue deduplication state.
        /// This ensures the same NPC dialogue can be announced on subsequent interactions.
        /// Also triggers entity refresh to update NPC/interactive object states.
        /// </summary>
        public static void Close_Postfix()
        {
            // Reset dialogue state for next conversation
            lastDialogueMessage = "";
            pendingDialogue = "";

            // Trigger entity refresh after dialogue ends (NPC interaction complete)
            FFII_ScreenReader.Core.FFII_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }
}
