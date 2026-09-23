using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;
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
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.MAGIC_MENU);

        private static bool _isSpellListFocused = false;
        private static bool _isTargetSelectionActive = false;
        private static bool _isCommandMenuActive = false;
        private static int lastSpellId = -1;
        private static string lastTargetAnnouncement = "";
        private static string lastCommandAnnouncement = "";
        private static OwnedCharacterData _currentCharacter = null;

        static MagicMenuState()
        {
            _helper.RegisterResetHandler(() =>
            {
                _isSpellListFocused = false;
                _isTargetSelectionActive = false;
                _isCommandMenuActive = false;
                lastSpellId = -1;
                lastTargetAnnouncement = "";
                lastCommandAnnouncement = "";
                _currentCharacter = null;
            });
        }

        // AbilityWindowController.State enum values - centralized in FF2Constants
        public const int STATE_NONE = FF2Constants.MagicMenuStates.STATE_NONE;
        public const int STATE_USE_LIST = FF2Constants.MagicMenuStates.STATE_USE_LIST;
        public const int STATE_USE_TARGET = FF2Constants.MagicMenuStates.STATE_USE_TARGET;
        public const int STATE_FORGET = FF2Constants.MagicMenuStates.STATE_FORGET;
        public const int STATE_COMMAND = FF2Constants.MagicMenuStates.STATE_COMMAND;
        public const int STATE_POPUP = FF2Constants.MagicMenuStates.STATE_POPUP;
        public const int STATE_ORDERLY = FF2Constants.MagicMenuStates.STATE_ORDERLY;
        public const int STATE_SELF_ORDERLY = FF2Constants.MagicMenuStates.STATE_SELF_ORDERLY;
        public const int STATE_SELF_ORDERLY_TARGET = FF2Constants.MagicMenuStates.STATE_SELF_ORDERLY_TARGET;

        // Memory offsets for KeyInput.AbilityCommandController (from dump.cs line 278489)
        public const int OFFSET_COMMAND_CONTENT_LIST = 0x48;
        public const int OFFSET_COMMAND_SELECT_CURSOR = 0x58;

        public static bool IsSpellListActive => _isSpellListFocused;
        public static bool IsTargetSelectionActive => _isTargetSelectionActive;
        public static bool IsCommandMenuActive => _isCommandMenuActive;

        public static bool IsActive => _helper.IsActive;

        public static void OnSpellListFocused()
        {
            _helper.SetActiveExclusive();
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
            _helper.SetActiveExclusive();
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
            _helper.SetActiveExclusive();
            _isCommandMenuActive = true;
            _isSpellListFocused = false;
            lastCommandAnnouncement = "";
        }

        public static void OnCommandMenuInactive()
        {
            _isCommandMenuActive = false;
            lastCommandAnnouncement = "";
            UpdateRegistryState();
        }

        private static void UpdateRegistryState()
        {
            if (!_isSpellListFocused && !_isTargetSelectionActive && !_isCommandMenuActive)
            {
                _helper.IsActive = false;
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

        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

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
                return false;
            }

            return true;
        }

        public static int GetCurrentState(AbilityWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Magic.OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + IL2CppOffsets.StateMachine.OFFSET_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + IL2CppOffsets.StateMachine.OFFSET_TAG);
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

        public static void ResetState() => _helper.IsActive = false;

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
                return level;
            }
            catch
            {
            }

            return 1;
        }
    }

    /// <summary>
    /// Patches for magic menu using manual Harmony patching.
    /// FF2 specific: MP cost system, spell proficiency levels 1-16.
    /// </summary>
    public static class MagicMenuPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch spell list controller
                TryPatchSpellListController(harmony);

                // Patch window controller for state transitions
                TryPatchWindowController(harmony);

                // Patch command controller for Use/Forget menu
                TryPatchCommandController(harmony);

                // Patch target selection for healing spells
                TryPatchTargetSelection(harmony);

                isPatched = true;
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

                // Patch SetCursor for navigation (the list's active state and initial read now come from
                // the window's UseListInit / ForgetInit — see TryPatchWindowController)
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
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// State-entry hooks on the KeyInput AbilityWindowController (all state-machine Inits with unique
        /// RVAs; they run from UpdateController → StateMachine.Change(nextState) once per transition):
        /// CommandInit 0x3B8610, UseListInit 0x3BCA20, ForgetInit 0x3B92B0, InitializeSelfOrderly
        /// 0x3B9BD0, InitializeSelfOrderlyTarget 0x3B9AC0. They replace the per-frame UpdateController
        /// postfixes (CLAUDE.md rule 3) and the SetNextState postfix, whose body (0x2AECD0) is folded
        /// with eight other methods and has no direct callers (inlined), so it never saw a transition.
        /// </summary>
        private static void TryPatchWindowController(HarmonyLib.Harmony harmony)
        {
            PatchWindow(harmony, "CommandInit", nameof(CommandInit_Prefix), nameof(CommandInit_Postfix));
            PatchWindow(harmony, "UseListInit", nameof(ListInit_Prefix), nameof(ListInit_Postfix));
            PatchWindow(harmony, "ForgetInit", nameof(ListInit_Prefix), nameof(ListInit_Postfix));
            PatchWindow(harmony, "InitializeSelfOrderly", null, nameof(SelfOrderlyInit_Postfix));
            PatchWindow(harmony, "InitializeSelfOrderlyTarget", null, nameof(SelfOrderlyInit_Postfix));
        }

        private static void PatchWindow(HarmonyLib.Harmony harmony, string method, string prefixName, string postfixName)
        {
            try
            {
                var target = AccessTools.Method(typeof(AbilityWindowController), method, Type.EmptyTypes);
                if (target == null)
                {
                    MelonLogger.Error($"[Magic Menu] AbilityWindowController.{method} not found");
                    return;
                }
                harmony.Patch(target,
                    prefix: prefixName == null ? null : new HarmonyMethod(AccessTools.Method(typeof(MagicMenuPatches), prefixName)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(MagicMenuPatches), postfixName)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Magic Menu] Error patching AbilityWindowController.{method}: {ex.Message}");
            }
        }

        /// <summary>
        /// Use/Forget command-bar navigation: AbilityCommandController.SetCommandSelectCursor (private,
        /// unique RVA 0x40E8C0). Callers: the Cursor.NextIndex/PrevIndex callbacks of the bar's input
        /// lambda, a click lambda, ResetCursor and SelectCommandByIndex (CommandInit) — event-driven.
        /// Replaces the UpdateFocus postfix: UpdateCommandSelect calls UpdateFocus every frame.
        /// </summary>
        private static void TryPatchCommandController(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(AbilityCommandController), "SetCommandSelectCursor", Type.EmptyTypes);
                if (target != null)
                    harmony.Patch(target, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(MagicMenuPatches), nameof(CommandSelectCursor_Postfix))));
                else
                    MelonLogger.Error("[Magic Menu] AbilityCommandController.SetCommandSelectCursor not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Magic Menu] Error patching SetCommandSelectCursor: {ex.Message}");
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
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(TargetSetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch
            {
            }
        }

        // AbilityWindowController (Serial.FF2 KeyInput, dump.cs:279525): commandController / listController
        private const int OFFSET_WINDOW_COMMAND_CONTROLLER = 0x48;
        private const int OFFSET_WINDOW_LIST_CONTROLLER = 0x58;
        private const int MAX_RETRY_FRAMES = 30;

        // True while an Init body runs: it moves the cursor (SelectCommandByIndex / SelectContentByIndex)
        // before the view is settled, so the cursor postfixes stay quiet and the deferred read after the
        // Init speaks the settled focus once.
        private static bool _commandInitInProgress = false;
        private static bool _listInitInProgress = false;
        // Bumped per Init; an older deferred read exits when superseded.
        private static int _commandReadGen = 0;
        private static int _listReadGen = 0;

        private enum ReadResult { Spoken, NotReady, Abandon }

        /// <summary>
        /// Command bar (Use / Forget / …) entered — magic menu open and every return from a list. Takes
        /// the command state, which also forgets the last spoken command so the entry reads again.
        /// </summary>
        public static void CommandInit_Prefix()
        {
            MagicMenuState.OnCommandMenuActive();
            _commandInitInProgress = true;
        }

        public static void CommandInit_Postfix(AbilityWindowController __instance)
        {
            _commandInitInProgress = false;
            try
            {
                if (__instance != null)
                    CoroutineManager.StartManaged(DeferredCommandRead(__instance, ++_commandReadGen));
            }
            catch { }
        }

        /// <summary>
        /// Use / Forget spell list entered (from the command bar, or back from a target / popup). Marks
        /// the list active (SetCursor_Postfix is gated on it) and forgets the last spoken spell. The
        /// command flag is left as it was, so the generic-reader suppression is unchanged.
        /// </summary>
        public static void ListInit_Prefix()
        {
            MagicMenuState.OnSpellListFocused();
            _listInitInProgress = true;
        }

        public static void ListInit_Postfix(AbilityWindowController __instance)
        {
            _listInitInProgress = false;
            try
            {
                if (__instance != null)
                    CoroutineManager.StartManaged(DeferredSpellRead(__instance, ++_listReadGen));
            }
            catch { }
        }

        /// <summary>
        /// Manual sort states reuse the spell list outside Use/Forget: the list is no longer the focused
        /// reader (the per-frame UpdateController postfix unfocused it in these states).
        /// </summary>
        public static void SelfOrderlyInit_Postfix()
        {
            if (MagicMenuState.IsSpellListActive)
                MagicMenuState.OnSpellListUnfocused();
        }

        /// <summary>
        /// Postfix for AbilityCommandController.SetCommandSelectCursor — announces the Use/Forget command
        /// the cursor moved to. Deduplicated with the entry read via ShouldAnnounceCommand.
        /// </summary>
        public static void CommandSelectCursor_Postfix(AbilityCommandController __instance)
        {
            try
            {
                if (_commandInitInProgress || __instance == null || !__instance.gameObject.activeInHierarchy)
                    return;

                var windowController = GameObjectCache.GetOrRefresh<AbilityWindowController>();
                if (windowController == null || MagicMenuState.GetCurrentState(windowController) != MagicMenuState.STATE_COMMAND)
                    return;

                if (!MagicMenuState.IsCommandMenuActive)
                    MagicMenuState.OnCommandMenuActive();

                TryAnnounceFocusedCommand(__instance);
            }
            catch { }
        }

        // yield stays outside the try (yield-in-try-with-catch is illegal).
        private static IEnumerator DeferredCommandRead(AbilityWindowController window, int gen)
        {
            for (int frame = 0; frame < MAX_RETRY_FRAMES; frame++)
            {
                yield return null;
                if (gen != _commandReadGen) yield break;

                ReadResult result;
                try
                {
                    result = ReadCommandEntry(window);
                }
                catch { result = ReadResult.NotReady; }
                if (result != ReadResult.NotReady) yield break;
            }
        }

        private static ReadResult ReadCommandEntry(AbilityWindowController window)
        {
            if (window == null || window.gameObject == null || !window.gameObject.activeInHierarchy)
                return ReadResult.NotReady;
            int state = MagicMenuState.GetCurrentState(window);
            if (state != MagicMenuState.STATE_COMMAND)
                return state < 0 ? ReadResult.NotReady : ReadResult.Abandon;   // already left the bar

            IntPtr cmdPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(window.Pointer, OFFSET_WINDOW_COMMAND_CONTROLLER);
            if (cmdPtr == IntPtr.Zero)
                return ReadResult.NotReady;
            return TryAnnounceFocusedCommand(new AbilityCommandController(cmdPtr)) ? ReadResult.Spoken : ReadResult.NotReady;
        }

        /// <summary>
        /// Reads selectCursor (0x58) → contentList (0x48)[index].Data.Name and speaks it with "(X of Y)"
        /// unless it was the last command spoken. Returns false only while the focus isn't readable yet.
        /// </summary>
        private static bool TryAnnounceFocusedCommand(AbilityCommandController controller)
        {
            IntPtr controllerPtr = controller.Pointer;
            if (controllerPtr == IntPtr.Zero)
                return false;

            unsafe
            {
                IntPtr cursorPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + MagicMenuState.OFFSET_COMMAND_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero)
                    return false;
                int index = new GameCursor(cursorPtr).Index;

                IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + MagicMenuState.OFFSET_COMMAND_CONTENT_LIST);
                if (contentListPtr == IntPtr.Zero)
                    return false;

                var contentList = new Il2CppSystem.Collections.Generic.List<AbilityCommandContentView>(contentListPtr);
                if (index < 0 || index >= contentList.Count)
                    return false;

                var data = contentList[index]?.Data;
                string commandName = data?.Name;
                if (string.IsNullOrEmpty(commandName))
                    return false;

                if (MagicMenuState.ShouldAnnounceCommand(commandName))
                    FFII_ScreenReaderMod.SpeakText(MenuPosition.Format(commandName, index, contentList.Count), interrupt: true);
                return true;
            }
        }

        private static IEnumerator DeferredSpellRead(AbilityWindowController window, int gen)
        {
            for (int frame = 0; frame < MAX_RETRY_FRAMES; frame++)
            {
                yield return null;
                if (gen != _listReadGen) yield break;

                ReadResult result;
                try
                {
                    result = ReadSpellEntry(window);
                }
                catch { result = ReadResult.NotReady; }
                if (result != ReadResult.NotReady) yield break;
            }
        }

        /// <summary>
        /// Announces the initially-focused spell on Use/Forget list entry (the game places the cursor
        /// inside the Init, before the list is readable) and caches the list's character.
        /// </summary>
        private static ReadResult ReadSpellEntry(AbilityWindowController window)
        {
            if (window == null || window.gameObject == null || !window.gameObject.activeInHierarchy)
                return ReadResult.NotReady;
            int state = MagicMenuState.GetCurrentState(window);
            if (state != MagicMenuState.STATE_USE_LIST && state != MagicMenuState.STATE_FORGET)
                return state < 0 ? ReadResult.NotReady : ReadResult.Abandon;
            if (!MagicMenuState.IsSpellListActive)
                return ReadResult.Abandon;   // reset (popup) since the Init

            IntPtr listPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(window.Pointer, OFFSET_WINDOW_LIST_CONTROLLER);
            if (listPtr == IntPtr.Zero)
                return ReadResult.NotReady;

            unsafe
            {
                IntPtr cursorPtr = *(IntPtr*)((byte*)listPtr.ToPointer() + IL2CppOffsets.Magic.OFFSET_LIST_SELECT_CURSOR);
                IntPtr contentListPtr = *(IntPtr*)((byte*)listPtr.ToPointer() + IL2CppOffsets.Magic.OFFSET_CONTENT_LIST);
                if (cursorPtr == IntPtr.Zero || contentListPtr == IntPtr.Zero)
                    return ReadResult.NotReady;
                int index = new GameCursor(cursorPtr).Index;
                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);
                if (index < 0 || index >= contentList.Count)
                    return ReadResult.NotReady;

                IntPtr charPtr = *(IntPtr*)((byte*)listPtr.ToPointer() + IL2CppOffsets.Magic.OFFSET_TARGET_CHARACTER);
                if (charPtr != IntPtr.Zero)
                    MagicMenuState.CurrentCharacter = new OwnedCharacterData(charPtr);
            }

            AnnounceSpellAtIndex(new AbilityContentListController(listPtr), GetListCursorIndex(listPtr));   // deduplicated by spell id
            return ReadResult.Spoken;
        }

        private static int GetListCursorIndex(IntPtr listPtr)
        {
            IntPtr cursorPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(listPtr, IL2CppOffsets.Magic.OFFSET_LIST_SELECT_CURSOR);
            return cursorPtr == IntPtr.Zero ? -1 : new GameCursor(cursorPtr).Index;
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

                if (_listInitInProgress)
                    return;   // the entry read after UseListInit / ForgetInit speaks the initial focus

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
            catch
            {
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
                    IntPtr contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Magic.OFFSET_USE_CONTENT_LIST);
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
                            announcement += $", {T("HP")} {currentHp}/{maxHp}";

                            int currentMp = parameter.currentMP;
                            int maxMp = parameter.ConfirmedMaxMp();
                            announcement += $", {T("MP")} {currentMp}/{maxMp}";

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
                    catch
                    {
                    }

                    // Skip duplicates
                    if (!MagicMenuState.ShouldAnnounceTarget(announcement))
                        return;

                    MagicMenuState.OnTargetSelectionActive();

                    announcement = MenuPosition.Format(announcement, index, contentList.Count);
                    FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch
            {
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
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + IL2CppOffsets.Magic.OFFSET_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                    return;

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                    return;

                int count = contentList.Count;
                var contentController = contentList[index];
                if (contentController == null)
                {
                    AnnounceEmpty(index, count);
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    AnnounceEmpty(index, count);
                    return;
                }

                // Pass contentController to read gauge for percentage
                AnnounceSpell(ability, contentController, index, count);
            }
            catch
            {
            }
        }

        private static void AnnounceEmpty(int index, int count)
        {
            if (MagicMenuState.ShouldAnnounceSpell(-1))
            {
                FFII_ScreenReaderMod.SpeakText(MenuPosition.Format(T("Empty"), index, count), interrupt: true);
            }
        }

        private static void AnnounceSpell(OwnedAbility ability, BattleAbilityInfomationContentController contentController, int index, int count)
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
                    announcement = string.Format(T("{0} lv{1}"), announcement, proficiency);
                }

                // Try to read percentage from gauge (FF2 specific: spell level progress)
                int percentage = -1;
                if (contentController != null)
                {
                    try
                    {
                        CommonGauge gauge = contentController.Gauge;
                        if (gauge != null)
                        {
                            // gaugeImage is private, access via offset
                            IntPtr gaugePtr = gauge.Pointer;
                            if (gaugePtr != IntPtr.Zero)
                            {
                                IntPtr imagePtr;
                                unsafe
                                {
                                    imagePtr = *(IntPtr*)((byte*)gaugePtr + IL2CppOffsets.Magic.OFFSET_GAUGE_IMAGE);
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
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                // Add percentage if available
                if (percentage >= 0)
                {
                    announcement = string.Format(T("{0}, {1} percent"), announcement, percentage);
                }

                // Spell list reads name + level + percentage only; the level already conveys the
                // MP cost (cost = spell level), so no separate MP-cost line.

                // Description appended only when AutoDetail is on; cached for the I key.
                string description = MagicMenuState.GetSpellDescription(ability);
                MenuDetailCache.Set(description);
                if (PreferencesManager.AutoDetailEnabled && !string.IsNullOrEmpty(description))
                {
                    announcement += $": {description}";
                }

                announcement = MenuPosition.Format(announcement, index, count);
                FFII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch
            {
            }
        }
    }
}
