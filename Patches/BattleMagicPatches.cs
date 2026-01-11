using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle magic menu.
    /// FF2 uses MP cost system instead of FF3's spell charges.
    /// </summary>
    public static class BattleMagicPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Magic] Applying battle magic menu patches...");

                // Try to find FF2's battle ability controller
                // FF2 may use a different type than FF3's BattleFrequencyAbilityInfomationController
                Type controllerType = null;

                // Search for FF2-specific controller types
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.FullName != null &&
                                type.FullName.Contains("BattleAbility") &&
                                type.FullName.Contains("Controller") &&
                                type.FullName.Contains("FF2"))
                            {
                                MelonLogger.Msg($"[Battle Magic] Found candidate type: {type.FullName}");
                                controllerType = type;
                            }
                        }
                    }
                    catch { }
                }

                // Try common type names
                if (controllerType == null)
                {
                    string[] typeNames = new[]
                    {
                        "Il2CppSerial.FF2.UI.KeyInput.BattleFrequencyAbilityInfomationController",
                        "Il2CppSerial.FF2.UI.KeyInput.BattleAbilityInfomationController",
                        "Il2CppLast.UI.KeyInput.BattleAbilityInfomationController"
                    };

                    foreach (var typeName in typeNames)
                    {
                        controllerType = Type.GetType(typeName);
                        if (controllerType != null)
                        {
                            MelonLogger.Msg($"[Battle Magic] Found controller type: {typeName}");
                            break;
                        }
                    }
                }

                if (controllerType != null)
                {
                    PatchSelectContent(harmony, controllerType);
                }
                else
                {
                    MelonLogger.Warning("[Battle Magic] Could not find battle ability controller type");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error applying patches: {ex.Message}");
            }
        }

        private static void PatchSelectContent(HarmonyLib.Harmony harmony, Type controllerType)
        {
            try
            {
                MethodInfo selectContentMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(int))
                        {
                            selectContentMethod = m;
                            MelonLogger.Msg($"[Battle Magic] Found SelectContent method");
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleMagicSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Magic] Patched SelectContent successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Magic] SelectContent method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error patching SelectContent: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// State tracking for battle magic menu.
    /// FF2 uses MP cost system instead of spell charges.
    /// </summary>
    public static class BattleMagicMenuState
    {
        public static bool IsActive { get; set; } = false;

        // State machine offsets for BattleCommandSelectController
        private const int OFFSET_STATE_MACHINE = 0x48;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        private const int STATE_NORMAL = 1;
        private const int STATE_EXTRA = 2;

        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                var cmdController = UnityEngine.Object.FindObjectOfType<BattleCommandSelectController>();
                if (cmdController != null && cmdController.gameObject.activeInHierarchy)
                {
                    int state = GetCommandState(cmdController);
                    if (state == STATE_NORMAL || state == STATE_EXTRA)
                    {
                        Reset();
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                Reset();
                return false;
            }
        }

        private static int GetCommandState(BattleCommandSelectController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return -1;

                unsafe
                {
                    IntPtr smPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (smPtr == IntPtr.Zero) return -1;

                    IntPtr currentPtr = *(IntPtr*)((byte*)smPtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentPtr == IntPtr.Zero) return -1;

                    return *(int*)((byte*)currentPtr.ToPointer() + OFFSET_STATE_TAG);
                }
            }
            catch { return -1; }
        }

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.15f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        public static void Reset()
        {
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
            CurrentPlayer = null;
        }

        public static BattlePlayerData CurrentPlayer { get; set; } = null;
    }

    /// <summary>
    /// Patch for battle magic selection.
    /// FF2 format: "Spell Name, Level X, MP cost Y: Description"
    /// </summary>
    public static class BattleMagicSelectContent_Patch
    {
        public static void Postfix(
            object __instance,
            Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController> contents,
            int index,
            GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || contents == null)
                    return;

                MelonLogger.Msg($"[Battle Magic] SelectContent called, index: {index}, contents count: {contents.Count}");

                if (index < 0 || index >= contents.Count)
                {
                    MelonLogger.Msg($"[Battle Magic] Index {index} out of range");
                    return;
                }

                var contentController = contents[index];
                if (contentController == null)
                {
                    if (BattleMagicMenuState.ShouldAnnounce("Empty"))
                    {
                        FFII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                        BattleMagicMenuState.IsActive = true;
                        MelonLogger.Msg("[Battle Magic] Announcing: Empty");
                        FFII_ScreenReaderMod.SpeakText("Empty", interrupt: true);
                    }
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    if (BattleMagicMenuState.ShouldAnnounce("Empty"))
                    {
                        FFII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                        BattleMagicMenuState.IsActive = true;
                        MelonLogger.Msg("[Battle Magic] Announcing: Empty");
                        FFII_ScreenReaderMod.SpeakText("Empty", interrupt: true);
                    }
                    return;
                }

                string announcement = FormatAbilityAnnouncement(ability);
                if (string.IsNullOrEmpty(announcement))
                    return;

                if (!BattleMagicMenuState.ShouldAnnounce(announcement))
                    return;

                FFII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                BattleMagicMenuState.IsActive = true;

                MelonLogger.Msg($"[Battle Magic] Announcing: {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error in SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Format ability data into announcement string.
        /// FF2 Format: "Spell Name, Level X, MP cost Y: Description"
        /// </summary>
        private static string FormatAbilityAnnouncement(OwnedAbility ability)
        {
            try
            {
                // Get spell name
                string name = null;
                string mesIdName = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesIdName))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        name = messageManager.GetMessage(mesIdName, false);
                    }
                }

                if (string.IsNullOrEmpty(name))
                    return null;

                name = TextUtils.StripIconMarkup(name);
                if (string.IsNullOrEmpty(name))
                    return null;

                string announcement = name;

                // FF2 specific: Add proficiency level (1-16)
                try
                {
                    int proficiency = ability.SkillLevel;
                    if (proficiency > 0)
                    {
                        announcement += $", Level {proficiency}";
                    }
                }
                catch { }

                // FF2 specific: Add MP cost
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        // UseValue is the MP cost in FF2
                        int mpCost = abilityData.UseValue;
                        if (mpCost > 0)
                        {
                            announcement += $", MP cost {mpCost}";
                        }
                    }
                }
                catch { }

                // Add description
                try
                {
                    string mesIdDesc = ability.MesIdDescription;
                    if (!string.IsNullOrEmpty(mesIdDesc))
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string description = messageManager.GetMessage(mesIdDesc, false);
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                description = TextUtils.StripIconMarkup(description);
                                if (!string.IsNullOrWhiteSpace(description))
                                {
                                    announcement += ": " + description;
                                }
                            }
                        }
                    }
                }
                catch { }

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error formatting announcement: {ex.Message}");
                return null;
            }
        }
    }
}
