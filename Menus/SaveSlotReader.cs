using System;
using MelonLoader;
using UnityEngine;
using static FFII_ScreenReader.Utils.TextUtils;
using SaveContentController_KeyInput = Il2CppLast.UI.KeyInput.SaveContentController;
using SaveContentController_Touch = Il2CppLast.UI.Touch.SaveContentController;
using SaveSlotData = Il2CppLast.Management.SaveSlotData;
using MessageManager = Il2CppLast.Management.MessageManager;

namespace FFII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading save slot information for the save/load game menu.
    /// Extracts and announces: slot type, character name, location, play time.
    /// Uses SlotData for reliable data extraction instead of View text fields.
    /// Note: FF2 has no traditional levels - characters grow through use.
    /// </summary>
    public static class SaveSlotReader
    {
        /// <summary>
        /// Try to read save slot information from the current cursor position.
        /// Returns a formatted string with all relevant save information, or null if not a save slot.
        /// </summary>
        public static string TryReadSaveSlot(Transform cursorTransform, int cursorIndex)
        {
            if (cursorTransform == null)
                return null;

            try
            {
                // Walk up the hierarchy to find the save list structure
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    // Look for save/load menu structures
                    string lowerName = current.name.ToLower();
                    if (lowerName.Contains("save") || lowerName.Contains("load") ||
                        lowerName.Contains("data_select"))
                    {
                        // Try to find Content list (common pattern: Scroll View -> Viewport -> Content)
                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursorIndex >= 0 && cursorIndex < contentList.childCount)
                        {
                            Transform saveSlot = contentList.GetChild(cursorIndex);

                            // Try to read from the slot using SlotData
                            string slotInfo = ReadSlotFromTransform(saveSlot, cursorIndex);
                            if (slotInfo != null)
                            {
                                return slotInfo;
                            }
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SaveSlotReader error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find the Content transform within a ScrollView structure.
        /// </summary>
        private static Transform FindContentList(Transform root)
        {
            try
            {
                var content = FindTransformInChildren(root, "Content");
                if (content != null && content.parent != null &&
                    (content.parent.name == "Viewport" || content.parent.parent?.name == "Scroll View"))
                {
                    return content;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error finding content list: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Read save slot data from a slot transform using SlotData.
        /// </summary>
        private static string ReadSlotFromTransform(Transform slotTransform, int slotIndex)
        {
            if (slotTransform == null)
                return null;

            try
            {
                // Try KeyInput SaveContentController first
                var keyInputController = slotTransform.GetComponent<SaveContentController_KeyInput>();
                if (keyInputController == null)
                {
                    keyInputController = slotTransform.GetComponentInChildren<SaveContentController_KeyInput>();
                }

                if (keyInputController != null)
                {
                    return ReadFromController(keyInputController.SlotName, keyInputController.SlotData, slotIndex);
                }

                // Try Touch SaveContentController
                var touchController = slotTransform.GetComponent<SaveContentController_Touch>();
                if (touchController == null)
                {
                    touchController = slotTransform.GetComponentInChildren<SaveContentController_Touch>();
                }

                if (touchController != null)
                {
                    return ReadFromController(touchController.SlotName, touchController.SlotData, slotIndex);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading slot transform: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read save slot data from controller's SlotData property.
        /// </summary>
        private static string ReadFromController(string slotName, SaveSlotData slotData, int slotIndex)
        {
            try
            {
                // Get slot identifier
                string slotId = GetSlotIdentifier(slotName, slotIndex);

                // Check if slot has data by checking SlotData
                if (slotData == null)
                {
                    return $"{slotId}: Empty";
                }

                // Extract data from SlotData
                string location = slotData.CurrentArea;
                string floor = slotData.CurrentLocation;
                double playTimeSeconds = slotData.PlayTime;
                string saveDate = slotData.TimeStamp;

                // Translate message keys to localized text
                var msgManager = MessageManager.Instance;
                if (msgManager != null)
                {
                    if (!string.IsNullOrEmpty(location))
                    {
                        location = msgManager.GetMessage(location, true);
                    }
                    if (!string.IsNullOrEmpty(floor))
                    {
                        floor = msgManager.GetMessage(floor, true);
                    }
                }

                // Get character info from party list
                // Note: FF2 has no traditional levels - characters grow through damage/use
                string characterName = null;

                var partyList = slotData.PartyList;
                if (partyList != null && partyList.Count > 0)
                {
                    var leadCharacter = partyList[0];
                    if (leadCharacter != null)
                    {
                        characterName = leadCharacter.Name;
                    }
                }

                // Check if we actually have meaningful data
                bool hasData = !string.IsNullOrEmpty(location) ||
                               !string.IsNullOrEmpty(characterName) ||
                               playTimeSeconds > 0;

                if (!hasData)
                {
                    return $"{slotId}: Empty";
                }

                // Convert play time from seconds to hours:minutes
                string hours = null;
                string minutes = null;
                if (playTimeSeconds > 0)
                {
                    int totalMinutes = (int)(playTimeSeconds / 60);
                    int h = totalMinutes / 60;
                    int m = totalMinutes % 60;
                    hours = h.ToString();
                    minutes = m.ToString("D2");
                }

                // Combine location and floor if both present
                if (!string.IsNullOrEmpty(floor) && !string.IsNullOrEmpty(location))
                {
                    location += " - " + floor;
                }

                return BuildAnnouncement(slotId, saveDate, location, characterName, hours, minutes);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading from SlotData: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get slot identifier from controller's SlotName property or build from index.
        /// </summary>
        private static string GetSlotIdentifier(string slotName, int slotIndex)
        {
            if (!string.IsNullOrEmpty(slotName))
            {
                return slotName;
            }

            // Fallback based on index
            // Index 0 = Autosave, Index 1 = Quicksave, Index 2+ = File (index - 1)
            if (slotIndex == 0)
                return "Autosave";
            if (slotIndex == 1)
                return "Quicksave";
            return $"File {slotIndex - 1}";
        }

        /// <summary>
        /// Build the announcement string from collected values.
        /// </summary>
        private static string BuildAnnouncement(string slotId, string saveDate, string location,
            string characterName, string hours, string minutes)
        {
            string announcement = slotId;

            // Add save date
            if (!string.IsNullOrEmpty(saveDate))
            {
                announcement += ", " + saveDate;
            }

            // Add location
            if (!string.IsNullOrEmpty(location))
            {
                announcement += ": " + location;
            }

            // Add character name (no level in FF2)
            if (!string.IsNullOrEmpty(characterName))
            {
                announcement += ", " + characterName;
            }

            // Add play time
            if (!string.IsNullOrEmpty(hours) && !string.IsNullOrEmpty(minutes))
            {
                announcement += $", {hours}:{minutes}";
            }

            return announcement;
        }
    }
}
