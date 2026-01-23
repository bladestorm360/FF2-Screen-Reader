using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using Il2CppLast.Management;
using Il2CppLast.Systems;
using ExpTableType = Il2CppLast.Defaine.Master.ExpTableType;

// Type aliases for IL2CPP types
// FF2 uses Il2CppSerial.FF2.UI.KeyInput for magic menu controllers
using AbilityContentListController = Il2CppSerial.FF2.UI.KeyInput.AbilityContentListController;
using AbilityWindowController = Il2CppSerial.FF2.UI.KeyInput.AbilityWindowController;
using AbilityCommandController = Il2CppSerial.FF2.UI.KeyInput.AbilityCommandController;
using AbilityCommandContentView = Il2CppSerial.FF2.UI.KeyInput.AbilityCommandContentView;
using AbilityUseContentListController = Il2CppSerial.FF2.UI.KeyInput.AbilityUseContentListController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using AbilityCommandId = Il2CppLast.Defaine.UI.AbilityCommandId;
using GameCursor = Il2CppLast.UI.Cursor;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using CommonGauge = Il2CppLast.UI.CommonGauge;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for magic menu with proper suppression pattern.
    /// FF2 uses MP cost system and spell proficiency levels 1-16.
    /// </summary>
    public static class MagicMenuState
    {
        private static bool _isSpellListFocused = false;
        private static bool _isTargetSelectionActive = false;
        private static bool _isCommandMenuActive = false;
        private static int lastSpellId = -1;
        private static string lastTargetAnnouncement = "";
        private static string lastCommandAnnouncement = "";
        private static OwnedCharacterData _currentCharacter = null;

        // AbilityWindowController.State enum values (from dump.cs line 279777)
        public const int STATE_NONE = 0;
        public const int STATE_USE_LIST = 1;      // Spell list for Use command
        public const int STATE_USE_TARGET = 2;    // Character selection after selecting spell
        public const int STATE_FORGET = 3;        // Spell list for Forget command
        public const int STATE_COMMAND = 4;       // Command menu (Use/Forget)
        public const int STATE_POPUP = 5;         // Yes/No popup
        public const int STATE_ORDERLY = 6;
        public const int STATE_SELF_ORDERLY = 7;
        public const int STATE_SELF_ORDERLY_TARGET = 8;

        // Memory offsets for KeyInput.AbilityWindowController (from dump.cs line 279546)
        private const int OFFSET_STATE_MACHINE = 0x88;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // Memory offsets for KeyInput.AbilityCommandController (from dump.cs line 278489)
        public const int OFFSET_COMMAND_CONTENT_LIST = 0x48;
        public const int OFFSET_COMMAND_SELECT_CURSOR = 0x58;

        public static bool IsSpellListActive => _isSpellListFocused;
        public static bool IsTargetSelectionActive => _isTargetSelectionActive;
        public static bool IsCommandMenuActive => _isCommandMenuActive;

        /// <summary>
        /// True when any magic menu sub-state is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsActive => MenuStateRegistry.IsActive(MenuStateRegistry.MAGIC_MENU);

        public static void OnSpellListFocused()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.MAGIC_MENU);
            _isSpellListFocused = true;
            lastSpellId = -1;
        }

        public static void OnSpellListUnfocused()
        {
            _isSpellListFocused = false;
            lastSpellId = -1;
            _currentCharacter = null;
            UpdateRegistryState();
        }

        public static void OnTargetSelectionActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.MAGIC_MENU);
            _isTargetSelectionActive = true;
            lastTargetAnnouncement = "";
        }

        public static void OnTargetSelectionInactive()
        {
            _isTargetSelectionActive = false;
            lastTargetAnnouncement = "";
            UpdateRegistryState();
        }

        public static void OnCommandMenuActive()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.MAGIC_MENU);
            _isCommandMenuActive = true;
            _isSpellListFocused = false;  // Command menu excludes spell list
            lastCommandAnnouncement = "";
        }

        public static void OnCommandMenuInactive()
        {
            _isCommandMenuActive = false;
            lastCommandAnnouncement = "";
            UpdateRegistryState();
        }

        /// <summary>
        /// Clears registry state if no sub-states are active.
        /// </summary>
        private static void UpdateRegistryState()
        {
            if (!_isSpellListFocused && !_isTargetSelectionActive && !_isCommandMenuActive)
            {
                MenuStateRegistry.Reset(MenuStateRegistry.MAGIC_MENU);
            }
        }

        public static bool ShouldAnnounceCommand(string announcement)
        {
            if (announcement == lastCommandAnnouncement)
                return false;
            lastCommandAnnouncement = announcement;
            return true;
        }

        public static OwnedCharacterData CurrentCharacter
        {
            get => _currentCharacter;
            set => _currentCharacter = value;
        }

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Suppresses when magic menu is active - specialized patches handle all states.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            // Validate the window controller exists
            var windowController = GameObjectCache.GetOrRefresh<AbilityWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
            {
                ResetState();
                return false;
            }

            int state = GetCurrentState(windowController);
            if (state == STATE_NONE)
            {
                ResetState();
                return false;  // Menu closed - don't suppress
            }

            // Suppress for ALL active states including COMMAND
            // CommandController_UpdateFocus_Postfix handles Use/Forget announcements
            // SetCursor_Postfix handles spell list announcements
            // We must suppress MenuTextDiscovery to prevent duplicate/wrong readings
            return true;
        }

        /// <summary>
        /// Reads the current state from AbilityWindowController's state machine.
        /// </summary>
        public static int GetCurrentState(AbilityWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }

        public static bool ShouldAnnounceSpell(int spellId)
        {
            if (spellId == lastSpellId)
                return false;
            lastSpellId = spellId;
            return true;
        }

        public static bool ShouldAnnounceTarget(string announcement)
        {
            if (announcement == lastTargetAnnouncement)
                return false;
            lastTargetAnnouncement = announcement;
            return true;
        }

        public static void ResetState()
        {
            _isSpellListFocused = false;
            _isTargetSelectionActive = false;
            _isCommandMenuActive = false;
            lastSpellId = -1;
            lastTargetAnnouncement = "";
            lastCommandAnnouncement = "";
            _currentCharacter = null;
            MenuStateRegistry.Reset(MenuStateRegistry.MAGIC_MENU);
        }

        public static string GetSpellName(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            try
            {
                string mesId = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return TextUtils.StripIconMarkup(localizedName);
                    }
                }
            }
            catch { }

            return null;
        }

        public static string GetSpellDescription(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            try
            {
                string mesId = ability.MesIdDescription;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedDesc = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedDesc))
                            return TextUtils.StripIconMarkup(localizedDesc);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets spell proficiency level (1-16) from OwnedAbility.
        /// FF2 specific: spells level up through use.
        /// Uses ExpUtility.GetExpLevel for accurate level calculation matching game UI.
        /// </summary>
        public static int GetSpellProficiency(OwnedAbility ability)
        {
            if (ability == null)
                return 0;

            try
            {
                // OwnedAbility.SkillLevel stores raw exp value
                // Use game's ExpUtility.GetExpLevel for accurate level calculation
                int rawExp = ability.SkillLevel;
                int level = ExpUtility.GetExpLevel(1, rawExp, ExpTableType.LevelExp);
                if (level < 1) level = 1;
                if (level > 16) level = 16;
                MelonLogger.Msg($"[Magic] Spell rawExp={rawExp} -> level={level}");
                return level;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic] Error getting spell proficiency: {ex.Message}");
            }

            return 1;
        }

        /// <summary>
        /// Gets MP cost for a spell.
        /// FF2 specific: spells cost MP to cast.
        /// </summary>
        public static int GetMPCost(OwnedAbility ability)
        {
            if (ability == null)
                return 0;

            try
            {
                var abilityData = ability.Ability;
                if (abilityData != null)
                {
                    return abilityData.UseValue;
                }
            }
            catch { }

            return 0;
        }
    }

    /// <summary>
    /// Patches for magic menu using manual Harmony patching.
    /// FF2 specific: MP cost system, spell proficiency levels 1-16.
    /// </summary>
    public static class MagicMenuPatches
    {
        private static bool isPatched = false;

        // Memory offsets for KeyInput.AbilityContentListController (from dump.cs line 278764)
        private const int OFFSET_CONTENT_LIST = 0x50;
        private const int OFFSET_TARGET_CHARACTER = 0x78;

        // Memory offsets for KeyInput.AbilityUseContentListController (from dump.cs line 279151)
        private const int OFFSET_USE_CONTENT_LIST = 0x40;
        private const int OFFSET_USE_SELECT_CURSOR = 0x48;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                MelonLogger.Msg("[Magic Menu] Applying magic menu patches...");

                // Patch spell list controller
                TryPatchSpellListController(harmony);

                // Patch window controller for state transitions
                TryPatchWindowController(harmony);

                // Patch command controller for Use/Forget menu
                TryPatchCommandController(harmony);

                // Patch target selection for healing spells
                TryPatchTargetSelection(harmony);

                isPatched = true;
                MelonLogger.Msg("[Magic Menu] Magic menu patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Magic Menu] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchSpellListController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                // Patch UpdateController to track when spell list is active
                MethodInfo updateMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "UpdateController")
                    {
                        updateMethod = method;
                        break;
                    }
                }

                if (updateMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(UpdateController_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityContentListController.UpdateController");
                }

                // Patch SetCursor for navigation
                MethodInfo setCursorMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetCursor")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = method;
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityContentListController.SetCursor");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error patching spell list controller: {ex.Message}");
            }
        }

        private static void TryPatchWindowController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityWindowController);

                // Patch SetNextState to detect state transitions
                MethodInfo setNextStateMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetNextState")
                    {
                        setNextStateMethod = method;
                        break;
                    }
                }

                if (setNextStateMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetNextState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityWindowController.SetNextState");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error patching window controller: {ex.Message}");
            }
        }

        private static void TryPatchCommandController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityCommandController);

                // Patch UpdateFocus to announce command selection
                MethodInfo updateFocusMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "UpdateFocus")
                    {
                        updateFocusMethod = method;
                        break;
                    }
                }

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(CommandController_UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityCommandController.UpdateFocus");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] AbilityCommandController.UpdateFocus not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error patching command controller: {ex.Message}");
            }
        }

        private static void TryPatchTargetSelection(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Target selection is handled by AbilityUseContentListController.SetCursor
                // This is called when navigating character targets for healing spells
                Type controllerType = typeof(AbilityUseContentListController);

                // Look for SetCursor method that handles cursor navigation
                MethodInfo setCursorMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetCursor")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = method;
                            MelonLogger.Msg($"[Magic Menu] Found AbilityUseContentListController.SetCursor");
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(TargetSetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityUseContentListController.SetCursor");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] AbilityUseContentListController.SetCursor not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error patching target selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetNextState - detects state transitions.
        /// Activates appropriate menu state based on transition.
        /// </summary>
        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                MelonLogger.Msg($"[Magic Menu] State transition to: {state}");

                if (state == MagicMenuState.STATE_COMMAND)
                {
                    // Transitioning to command menu - activate command state
                    MagicMenuState.OnCommandMenuActive();
                }
                else if (state == MagicMenuState.STATE_POPUP)
                {
                    // Transitioning to popup - clear all flags, let generic cursor handle
                    MagicMenuState.ResetState();
                }
                else if (state == MagicMenuState.STATE_USE_TARGET || state == MagicMenuState.STATE_SELF_ORDERLY_TARGET)
                {
                    // Transitioning to target selection
                    MagicMenuState.OnCommandMenuInactive();
                    MagicMenuState.OnSpellListUnfocused();
                    MagicMenuState.OnTargetSelectionActive();
                }
                else if (state == MagicMenuState.STATE_USE_LIST || state == MagicMenuState.STATE_FORGET)
                {
                    // Transitioning to spell list
                    MagicMenuState.OnCommandMenuInactive();
                    MagicMenuState.OnTargetSelectionInactive();
                    // OnSpellListFocused will be called by UpdateController_Postfix
                }
                else if (state == MagicMenuState.STATE_NONE)
                {
                    // Menu closing
                    MagicMenuState.ResetState();
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for AbilityCommandController.UpdateFocus - announces Use/Forget commands.
        /// </summary>
        public static void CommandController_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var controller = __instance as AbilityCommandController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // Verify we're in command state
                var windowController = GameObjectCache.GetOrRefresh<AbilityWindowController>();
                if (windowController != null)
                {
                    int currentState = MagicMenuState.GetCurrentState(windowController);
                    if (currentState != MagicMenuState.STATE_COMMAND)
                    {
                        return;
                    }
                }

                // Mark command menu as active
                if (!MagicMenuState.IsCommandMenuActive)
                {
                    MagicMenuState.OnCommandMenuActive();
                }

                // Read cursor index and content list using pointer offsets
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                unsafe
                {
                    // Get selectCursor at offset 0x58
                    IntPtr cursorPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + MagicMenuState.OFFSET_COMMAND_SELECT_CURSOR);
                    if (cursorPtr == IntPtr.Zero)
                        return;

                    var cursor = new GameCursor(cursorPtr);
                    int index = cursor.Index;

                    // Get contentList at offset 0x48
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + MagicMenuState.OFFSET_COMMAND_CONTENT_LIST);
                    if (contentListPtr == IntPtr.Zero)
                        return;

                    var contentList = new Il2CppSystem.Collections.Generic.List<AbilityCommandContentView>(contentListPtr);
                    if (index < 0 || index >= contentList.Count)
                        return;

                    var contentView = contentList[index];
                    if (contentView == null)
                        return;

                    var data = contentView.Data;
                    if (data == null)
                        return;

                    string commandName = data.Name;
                    if (string.IsNullOrEmpty(commandName))
                        return;

                    // Check for duplicate announcement
                    if (!MagicMenuState.ShouldAnnounceCommand(commandName))
                        return;

                    MelonLogger.Msg($"[Magic Command] {commandName}");
                    FFII_ScreenReaderMod.SpeakText(commandName, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in CommandController_UpdateFocus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UpdateController - tracks when spell list is actively handling input.
        /// Only activates during UseList or Forget states.
        /// </summary>
        public static void UpdateController_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // Check if we're in a spell list state
                var windowController = GameObjectCache.GetOrRefresh<AbilityWindowController>();
                if (windowController != null)
                {
                    int currentState = MagicMenuState.GetCurrentState(windowController);

                    // Only activate spell list during UseList or Forget states
                    if (currentState != MagicMenuState.STATE_USE_LIST &&
                        currentState != MagicMenuState.STATE_FORGET)
                    {
                        // Not in spell list state - don't activate
                        if (MagicMenuState.IsSpellListActive)
                        {
                            MagicMenuState.OnSpellListUnfocused();
                        }
                        return;
                    }
                }

                // Spell list is active in correct state
                if (!MagicMenuState.IsSpellListActive)
                {
                    MagicMenuState.OnSpellListFocused();
                }

                // Cache character data for MP display
                try
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        unsafe
                        {
                            IntPtr charPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TARGET_CHARACTER);
                            if (charPtr != IntPtr.Zero)
                            {
                                MagicMenuState.CurrentCharacter = new OwnedCharacterData(charPtr);
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for SetCursor - announces spell during navigation.
        /// Format: "Spell Name, Level X, MP cost Y: Description"
        /// </summary>
        public static void SetCursor_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // PRIMARY CHECK: Verify state machine FIRST before any flag checks.
                // This prevents reading spells when in COMMAND state (Use/Forget menu).
                // The state machine is the authoritative source, flags can race.
                var windowController = GameObjectCache.GetOrRefresh<AbilityWindowController>();
                if (windowController != null)
                {
                    int currentState = MagicMenuState.GetCurrentState(windowController);
                    // Only announce spells in USE_LIST or FORGET states
                    if (currentState != MagicMenuState.STATE_USE_LIST &&
                        currentState != MagicMenuState.STATE_FORGET)
                    {
                        return;
                    }
                }

                // Secondary check: flag must also indicate spell list is active
                if (!MagicMenuState.IsSpellListActive)
                    return;

                int cursorIndex = targetCursor.Index;
                AnnounceSpellAtIndex(controller, cursorIndex);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in SetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for AbilityUseContentListController.SetCursor - announces character target for healing spells.
        /// Format: "Name, HP current/max, MP current/max, Status effects"
        /// </summary>
        public static void TargetSetCursor_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as AbilityUseContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // Verify we're in target selection state
                var windowController = GameObjectCache.GetOrRefresh<AbilityWindowController>();
                if (windowController != null)
                {
                    int currentState = MagicMenuState.GetCurrentState(windowController);
                    if (currentState != MagicMenuState.STATE_USE_TARGET &&
                        currentState != MagicMenuState.STATE_SELF_ORDERLY_TARGET)
                    {
                        return;
                    }
                }

                int index = targetCursor.Index;

                // Read content list using pointer offset
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                unsafe
                {
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_USE_CONTENT_LIST);
                    if (contentListPtr == IntPtr.Zero)
                        return;

                    var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(contentListPtr);
                    if (contentList == null || contentList.Count == 0)
                        return;

                    if (index < 0 || index >= contentList.Count)
                        return;

                    var content = contentList[index];
                    if (content == null)
                        return;

                    var characterData = content.CurrentData;
                    if (characterData == null)
                        return;

                    // Build announcement: "Name, HP current/max, MP current/max, Status"
                    string charName = characterData.Name;
                    if (string.IsNullOrWhiteSpace(charName))
                        return;

                    string announcement = charName;

                    try
                    {
                        var parameter = characterData.Parameter;
                        if (parameter != null)
                        {
                            int currentHp = parameter.currentHP;
                            int maxHp = parameter.ConfirmedMaxHp();
                            announcement += $", HP {currentHp}/{maxHp}";

                            int currentMp = parameter.currentMP;
                            int maxMp = parameter.ConfirmedMaxMp();
                            announcement += $", MP {currentMp}/{maxMp}";

                            // Add status conditions
                            var conditionList = parameter.CurrentConditionList;
                            if (conditionList != null && conditionList.Count > 0)
                            {
                                var statusNames = new List<string>();
                                foreach (var condition in conditionList)
                                {
                                    string conditionName = LocalizationUtility.GetConditionName(condition);
                                    if (!string.IsNullOrWhiteSpace(conditionName))
                                    {
                                        statusNames.Add(conditionName);
                                    }
                                }

                                if (statusNames.Count > 0)
                                {
                                    announcement += ", " + string.Join(", ", statusNames);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Magic Target] Error getting character parameters: {ex.Message}");
                    }

                    // Skip duplicates
                    if (!MagicMenuState.ShouldAnnounceTarget(announcement))
                        return;

                    MagicMenuState.OnTargetSelectionActive();

                    MelonLogger.Msg($"[Magic Target] {announcement}");
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in TargetSetCursor_Postfix: {ex.Message}");
            }
        }

        private static void AnnounceSpellAtIndex(AbilityContentListController controller, int index)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                // Read contentList pointer at offset 0x50
                IntPtr contentListPtr;
                unsafe
                {
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                    return;

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                    return;

                var contentController = contentList[index];
                if (contentController == null)
                {
                    AnnounceEmpty();
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    AnnounceEmpty();
                    return;
                }

                // Pass contentController to read gauge for percentage
                AnnounceSpell(ability, contentController);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AnnounceSpellAtIndex: {ex.Message}");
            }
        }

        private static void AnnounceEmpty()
        {
            if (MagicMenuState.ShouldAnnounceSpell(-1))
            {
                FFII_ScreenReaderMod.SpeakText("Empty", interrupt: true);
            }
        }

        // Memory offset for CommonGauge.gaugeImage (private field)
        private const int OFFSET_GAUGE_IMAGE = 0x18;

        private static void AnnounceSpell(OwnedAbility ability, BattleAbilityInfomationContentController contentController = null)
        {
            try
            {
                int spellId = 0;
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        spellId = abilityData.Id;
                    }
                }
                catch
                {
                    return;
                }

                if (!MagicMenuState.ShouldAnnounceSpell(spellId))
                    return;

                string spellName = MagicMenuState.GetSpellName(ability);
                if (string.IsNullOrEmpty(spellName))
                    return;

                // Build FF2-specific announcement
                string announcement = spellName;

                // Add proficiency level (FF2 specific: spells level up 1-16 with use)
                int proficiency = MagicMenuState.GetSpellProficiency(ability);
                if (proficiency > 0)
                {
                    announcement += $" lv{proficiency}";
                }

                // Try to read percentage from gauge (FF2 specific: spell level progress)
                int percentage = -1;
                if (contentController != null)
                {
                    try
                    {
                        MelonLogger.Msg($"[Magic Menu] Attempting to read gauge from contentController...");
                        CommonGauge gauge = contentController.Gauge;
                        if (gauge != null)
                        {
                            MelonLogger.Msg($"[Magic Menu] Got CommonGauge, reading gaugeImage...");
                            // gaugeImage is private, access via offset
                            IntPtr gaugePtr = gauge.Pointer;
                            if (gaugePtr != IntPtr.Zero)
                            {
                                IntPtr imagePtr;
                                unsafe
                                {
                                    imagePtr = *(IntPtr*)((byte*)gaugePtr + OFFSET_GAUGE_IMAGE);
                                }
                                if (imagePtr != IntPtr.Zero)
                                {
                                    var gaugeImage = new Image(imagePtr);
                                    if (gaugeImage != null)
                                    {
                                        float fillAmount = gaugeImage.fillAmount;
                                        percentage = (int)(fillAmount * 100);
                                        if (percentage < 0) percentage = 0;
                                        if (percentage > 99) percentage = 99;
                                        MelonLogger.Msg($"[Magic Menu] Spell gauge fillAmount={fillAmount} -> {percentage}%");
                                    }
                                    else
                                    {
                                        MelonLogger.Msg("[Magic Menu] gaugeImage wrapper is null");
                                    }
                                }
                                else
                                {
                                    MelonLogger.Msg("[Magic Menu] gaugeImage pointer is zero");
                                }
                            }
                            else
                            {
                                MelonLogger.Msg("[Magic Menu] gauge pointer is zero");
                            }
                        }
                        else
                        {
                            MelonLogger.Msg("[Magic Menu] contentController.Gauge is null");
                        }
                    }
                    catch (Exception gaugeEx)
                    {
                        MelonLogger.Warning($"[Magic Menu] Error reading spell gauge: {gaugeEx.Message}");
                    }
                }

                // Add percentage if available
                if (percentage >= 0)
                {
                    announcement += $", {percentage} percent";
                }

                // Add MP cost (FF2 specific)
                int mpCost = MagicMenuState.GetMPCost(ability);
                if (mpCost > 0)
                {
                    announcement += $", MP {mpCost}";
                }

                // Add description
                string description = MagicMenuState.GetSpellDescription(ability);
                if (!string.IsNullOrEmpty(description))
                {
                    announcement += $": {description}";
                }

                MelonLogger.Msg($"[Magic Menu] {announcement}");
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AnnounceSpell: {ex.Message}");
            }
        }
    }
}
