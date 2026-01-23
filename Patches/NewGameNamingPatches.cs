using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using NewGameWindowController = Il2CppSerial.FF2.UI.KeyInput.NewGameWindowController;
// Use KeyInput versions - Touch versions have different methods that aren't called during keyboard navigation
using CharacterContentListController = Il2CppLast.UI.KeyInput.CharacterContentListController;
using CharacterContentController = Il2CppLast.UI.KeyInput.CharacterContentController;
using NameContentListController = Il2CppLast.UI.KeyInput.NameContentListController;
using NewGameSelectData = Il2CppLast.Data.NewGameSelectData;
using NewGamePopup = Il2CppSerial.FF2.UI.KeyInput.NewGamePopup;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the New Game character naming screen.
    /// FF2 has multiple suggested names per character that players can cycle through.
    /// Uses event-driven hooks (SetFocusContent, SetForcusIndex) instead of per-frame patches.
    /// </summary>
    public static class NewGameNamingPatches
    {
        private const string CONTEXT_NAME = "NewGame.Name";
        private const string CONTEXT_AUTO_INDEX = "NewGame.AutoNameIndex";
        private const string CONTEXT_SELECTED = "NewGame.SelectedIndex";
        private const string CONTEXT_SLOT_NAME = "NewGame.SlotName";

        // Reference to the current NewGameWindowController for accessing data
        private static object currentController = null;

        // Track last announced name per slot to detect name changes
        private static string[] lastSlotNames = new string[4];
        private static int lastTargetIndex = -1;

        /// <summary>
        /// Applies all naming screen patches using manual Harmony patching.
        /// Uses event-driven hooks instead of per-frame Update patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Use typeof() directly - much faster than assembly scanning
                Type controllerType = typeof(NewGameWindowController);

                // Patch InitNameSelect - called when entering name selection state
                var initNameSelectMethod = AccessTools.Method(controllerType, "InitNameSelect");
                if (initNameSelectMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("InitNameSelect_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initNameSelectMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("InitNameSelect method not found via AccessTools");
                }

                // Patch InitSelect - called when entering character selection
                var initSelectMethod = AccessTools.Method(controllerType, "InitSelect");
                if (initSelectMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("InitSelect_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initSelectMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("InitSelect method not found via AccessTools");
                }

                // Patch InitStartPopup - called when "Start game with these settings?" popup opens
                // NewGamePopup extends MonoBehaviour (not Popup), so base Popup.Open doesn't catch it
                var initStartPopupMethod = AccessTools.Method(controllerType, "InitStartPopup");
                if (initStartPopupMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("InitStartPopup_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initStartPopupMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("InitStartPopup method not found via AccessTools");
                }

                // EVENT-DRIVEN HOOK: CharacterContentListController.SetTargetSelectContent(int)
                // KeyInput version uses SetTargetSelectContent (private) instead of SetFocusContent
                // Fires once when cursor moves to new character slot
                Type charaListType = typeof(CharacterContentListController);
                var setTargetMethod = AccessTools.Method(charaListType, "SetTargetSelectContent", new[] { typeof(int) });
                if (setTargetMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("SetTargetSelectContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setTargetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("SetTargetSelectContent method not found on KeyInput.CharacterContentListController");
                }

                // EVENT-DRIVEN HOOK: NameContentListController.SetFocus(int)
                // KeyInput version uses SetFocus (private) instead of SetForcusIndex
                // Fires once when name focus changes during name cycling
                Type nameListType = typeof(NameContentListController);
                var setFocusMethod = AccessTools.Method(nameListType, "SetFocus", new[] { typeof(int) });
                if (setFocusMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("SetFocus_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("SetFocus method not found on KeyInput.NameContentListController");
                }

                // EVENT-DRIVEN HOOK: CharacterContentListController.UpdateView(List<NewGameSelectData>)
                // Fires when character data is refreshed, including after name assignment
                Type listControllerType = typeof(CharacterContentListController);
                var updateViewMethod = AccessTools.Method(listControllerType, "UpdateView");
                if (updateViewMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("UpdateView_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateViewMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("UpdateView method not found on KeyInput.CharacterContentListController");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying naming patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs available methods on a type for debugging IL2CPP reflection issues.
        /// Only logs during initial setup, not during gameplay.
        /// </summary>
        private static void LogAvailableMethods(Type type)
        {
            // Debug logging removed - type discovery working correctly
        }

        // FindType method removed - using typeof() directly is much faster

        /// <summary>
        /// Postfix for InitSelect - announces entering character selection and current slot.
        /// Also stores controller reference for event-driven hooks.
        /// </summary>
        public static void InitSelect_Postfix(object __instance)
        {
            try
            {
                // Store controller reference for event-driven hooks
                currentController = __instance;

                // Reset tracking when entering character selection
                // Don't pre-register any index - let first navigation announce correctly
                AnnouncementDeduplicator.Reset(CONTEXT_NAME, CONTEXT_AUTO_INDEX, CONTEXT_SELECTED, CONTEXT_SLOT_NAME);

                // Reset slot name tracking
                lastSlotNames = new string[4];
                lastTargetIndex = -1;

                // Only announce mode entry - slot will be announced on first navigation
                string announcement = "Character selection";
                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitStartPopup - announces "Start game with these names?" popup.
        /// NewGamePopup extends MonoBehaviour (not Popup), so base Popup.Open doesn't catch it.
        /// </summary>
        public static void InitStartPopup_Postfix(object __instance)
        {
            try
            {
                // Access popup field via property accessor
                var popupProp = AccessTools.Property(typeof(NewGameWindowController), "popup");
                if (popupProp == null)
                {
                    return;
                }

                // Access popup via reflection and cast to correct type
                // CRITICAL: NewGamePopup is in Serial.FF2.UI.KeyInput namespace, NOT Last.UI.KeyInput
                var controller = ((Il2CppSystem.Object)__instance).TryCast<NewGameWindowController>();
                var popupObj = popupProp.GetValue(controller);

                if (popupObj != null)
                {
                    var popup = ((Il2CppSystem.Object)popupObj).TryCast<NewGamePopup>();
                    if (popup != null)
                    {
                        string message = popup.Message;
                        if (!string.IsNullOrEmpty(message))
                        {
                            FFII_ScreenReaderMod.SpeakText(message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitStartPopup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// EVENT-DRIVEN: Postfix for KeyInput.CharacterContentListController.SetTargetSelectContent
        /// Fires once when cursor moves to a new character slot.
        /// </summary>
        public static void SetTargetSelectContent_Postfix(object __instance, int index)
        {
            try
            {
                // Only announce if index changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_SELECTED, index))
                {
                    return;
                }

                // Update tracking for UpdateView_Postfix
                lastTargetIndex = index;

                // Get slot info from stored controller reference
                if (currentController != null)
                {
                    string slotInfo = GetCharacterSlotInfo(currentController, index);
                    if (!string.IsNullOrEmpty(slotInfo))
                    {
                        // Also track the current name for this slot
                        if (index >= 0 && index < 4)
                        {
                            string nameOnly = GetCharacterSlotNameOnly(__instance, index);
                            lastSlotNames[index] = nameOnly;
                        }

                        FFII_ScreenReaderMod.SpeakText(slotInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetTargetSelectContent_Postfix: {ex.Message}");
            }
        }


        /// <summary>
        /// Gets character slot info in format "Character {n}: {name or unnamed}".
        /// </summary>
        private static string GetCharacterSlotInfo(object controller, int index)
        {
            // Ensure index is valid for display (1-based)
            int displayIndex = index >= 0 ? index + 1 : 1;

            try
            {
                // Get SelectedDataList property
                var listProp = AccessTools.Property(controller.GetType(), "SelectedDataList");
                if (listProp == null)
                {
                    return $"Character {displayIndex}: unnamed";
                }

                var list = listProp.GetValue(controller);
                if (list == null)
                {
                    return $"Character {displayIndex}: unnamed";
                }

                // Get count and item at index
                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null)
                {
                    return $"Character {displayIndex}: unnamed";
                }

                int count = (int)countProp.GetValue(list);
                if (index < 0 || index >= count)
                {
                    // Index beyond character list is the Done button
                    return index >= count ? "Done" : null;
                }

                // Get item at index using indexer
                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null)
                {
                    return $"Character {displayIndex}: unnamed";
                }

                var item = indexer.GetValue(list, new object[] { index });
                if (item == null)
                {
                    return $"Character {displayIndex}: unnamed";
                }

                // Get CharacterName from NewGameSelectData
                var nameProp = AccessTools.Property(item.GetType(), "CharacterName");
                if (nameProp != null)
                {
                    string name = nameProp.GetValue(item) as string;
                    if (string.IsNullOrEmpty(name))
                    {
                        return $"Character {displayIndex}: unnamed";
                    }
                    return $"Character {displayIndex}: {name}";
                }

                return $"Character {displayIndex}: unnamed";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character slot info: {ex.Message}");
                return $"Character {displayIndex}: unnamed";
            }
        }

        /// <summary>
        /// Postfix for InitNameSelect - announces entering name selection mode.
        /// Reads the current character name from the controller instance.
        /// Also stores controller reference for event-driven hooks.
        /// </summary>
        public static void InitNameSelect_Postfix(object __instance)
        {
            try
            {
                // Store controller reference for event-driven hooks
                currentController = __instance;

                // Reset tracking
                AnnouncementDeduplicator.Reset(CONTEXT_NAME, CONTEXT_AUTO_INDEX);

                // Try to get CurrentData property which has CharacterName
                string characterName = GetCurrentCharacterName(__instance);

                // Get the current suggested name
                string suggestedName = GetCurrentSuggestedName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = $"Select name for {characterName}";
                }
                else
                {
                    announcement = "Select name";
                }

                if (!string.IsNullOrEmpty(suggestedName))
                {
                    announcement += $". Current: {suggestedName}";
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_NAME, suggestedName);
                }

                FFII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// EVENT-DRIVEN: Postfix for KeyInput.NameContentListController.SetFocus
        /// Fires once when name focus changes during name cycling.
        /// </summary>
        public static void SetFocus_Postfix(int index)
        {
            try
            {
                // Check if index changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_AUTO_INDEX, index))
                {
                    return;
                }

                // Get the name at this index from stored NewGameWindowController
                string currentName = null;
                if (currentController != null)
                {
                    currentName = GetAutoNameByIndex(currentController, index);
                }

                if (!string.IsNullOrEmpty(currentName) &&
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_NAME, currentName))
                {
                    FFII_ScreenReaderMod.SpeakText(currentName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetFocus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// EVENT-DRIVEN: Postfix for CharacterContentListController.UpdateView
        /// Fires when the character list view is refreshed, including after name assignment.
        /// Detects name changes and announces the new name.
        /// </summary>
        public static void UpdateView_Postfix(object __instance)
        {
            try
            {
                // Use lastTargetIndex set by SetTargetSelectContent_Postfix
                // (reading from instance offset was unreliable)
                int currentSlot = lastTargetIndex;

                // Skip if no slot selected yet
                if (currentSlot < 0 || currentSlot >= 4)
                {
                    return;
                }

                // Get the current name for this slot
                string currentName = GetCharacterSlotNameOnly(__instance, currentSlot);
                string lastName = lastSlotNames[currentSlot];

                // Announce if name exists and is different from last known name for this slot
                if (!string.IsNullOrEmpty(currentName) && currentName != lastName)
                {
                    lastSlotNames[currentSlot] = currentName;
                    FFII_ScreenReaderMod.SpeakText(currentName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in UpdateView_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets just the character name for a slot (without "Character X:" prefix).
        /// </summary>
        private static string GetCharacterSlotNameOnly(object controller, int index)
        {
            try
            {
                // Get ContentList property which has CharacterContentController items
                var listProp = AccessTools.Property(controller.GetType(), "ContentList");
                if (listProp == null)
                {
                    return null;
                }

                var list = listProp.GetValue(controller);
                if (list == null)
                {
                    return null;
                }

                // Get count
                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null || index < 0 || index >= (int)countProp.GetValue(list))
                {
                    return null;
                }

                // Get item at index
                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null)
                {
                    return null;
                }

                var charController = indexer.GetValue(list, new object[] { index });
                if (charController == null)
                {
                    return null;
                }

                // Get CharacterName property from CharacterContentController
                var nameProp = AccessTools.Property(charController.GetType(), "CharacterName");
                if (nameProp != null)
                {
                    string name = nameProp.GetValue(charController) as string;
                    return string.IsNullOrEmpty(name) ? null : name;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the current character name from CurrentData property.
        /// </summary>
        private static string GetCurrentCharacterName(object controller)
        {
            try
            {
                // Get CurrentData property (NewGameSelectData)
                var currentDataProp = controller.GetType().GetProperty("CurrentData",
                    BindingFlags.Public | BindingFlags.Instance);

                if (currentDataProp != null)
                {
                    var currentData = currentDataProp.GetValue(controller);
                    if (currentData != null)
                    {
                        // Get CharacterName from NewGameSelectData
                        var charNameProp = currentData.GetType().GetProperty("CharacterName",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (charNameProp != null)
                        {
                            return charNameProp.GetValue(currentData) as string;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the current suggested name using GetAutoName method or field access.
        /// </summary>
        private static string GetCurrentSuggestedName(object controller)
        {
            try
            {
                // Try to get autoNameIndex field
                var autoNameIndexField = controller.GetType().GetField("autoNameIndex",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (autoNameIndexField != null)
                {
                    int index = (int)autoNameIndexField.GetValue(controller);
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_AUTO_INDEX, index);
                    return GetAutoNameByIndex(controller, index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting suggested name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets an auto-generated name by index using the GetAutoName method.
        /// </summary>
        private static string GetAutoNameByIndex(object controller, int index)
        {
            try
            {
                // Try calling GetAutoName(int index) method
                var getAutoNameMethod = controller.GetType().GetMethod("GetAutoName",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (getAutoNameMethod != null)
                {
                    var result = getAutoNameMethod.Invoke(controller, new object[] { index });
                    return result as string;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error calling GetAutoName: {ex.Message}");
            }
            return null;
        }
    }
}
