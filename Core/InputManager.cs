using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Patches;
using FFII_ScreenReader.Utils;
using Object = UnityEngine.Object;
using static FFII_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP config controllers
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Uses KeyBindingRegistry for declarative, context-aware dispatch.
    /// </summary>
    public class InputManager : IDisposable
    {
        private readonly FFII_ScreenReaderMod mod;
        private readonly KeyBindingRegistry registry = new KeyBindingRegistry();

        public InputManager(FFII_ScreenReaderMod mod)
        {
            this.mod = mod;
            InitializeBindings();
        }

        /// <summary>
        /// Initializes the input manager.
        /// </summary>
        public void Initialize()
        {
        }

        private void RegisterFieldWithBattleFeedback(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            registry.Register(key, modifier, KeyContext.Field, action, description);
            registry.Register(key, modifier, KeyContext.Battle, NotAvailableInBattle, description + " (battle blocked)");
        }

        private static void NotAvailableInBattle()
        {
            FFII_ScreenReaderMod.SpeakText(T("Not available in battle"), interrupt: true);
        }

        private void InitializeBindings()
        {
            // --- Status screen: navigation ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToTop, "Jump to first stat");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToPreviousGroup, "Jump to previous stat group");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.Status, StatusNavigationReader.NavigatePrevious, "Previous stat");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToBottom, "Jump to last stat");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToNextGroup, "Jump to next stat group");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.Status, StatusNavigationReader.NavigateNext, "Next stat");
            registry.Register(KeyCode.R, KeyContext.Status, StatusNavigationReader.ReadCurrentStat, "Repeat current stat");

            // --- Field: entity navigation (brackets + backslash) -- with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.None, mod.AnnounceCurrentEntity, "Announce current entity");

            // --- Field: alternate keys (J/K/L/P) -- with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.None, mod.AnnounceCurrentEntity, "Announce current entity (alt)");

            // --- Field: waypoint keys ---
            registry.Register(KeyCode.Comma, KeyModifier.Shift, KeyContext.Field, mod.WaypointCyclePreviousCategory, "Previous waypoint category");
            registry.Register(KeyCode.Comma, KeyModifier.None, KeyContext.Field, mod.WaypointCyclePrevious, "Previous waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Ctrl, KeyContext.Field, mod.WaypointRename, "Rename waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Shift, KeyContext.Field, mod.WaypointCycleNextCategory, "Next waypoint category");
            registry.Register(KeyCode.Period, KeyModifier.None, KeyContext.Field, mod.WaypointCycleNext, "Next waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.CtrlShift, KeyContext.Field, mod.WaypointClearAll, "Clear all waypoints for map");
            registry.Register(KeyCode.Slash, KeyModifier.Ctrl, KeyContext.Field, mod.WaypointDelete, "Remove current waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.Shift, KeyContext.Field, mod.WaypointAdd, "Add waypoint with name");
            registry.Register(KeyCode.Slash, KeyModifier.None, KeyContext.Field, mod.WaypointPathfind, "Pathfind to waypoint");

            // --- Field: teleport (Ctrl+Arrow) ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(0, 16)), "Teleport north");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(0, -16)), "Teleport south");
            registry.Register(KeyCode.LeftArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(-16, 0)), "Teleport west");
            registry.Register(KeyCode.RightArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(16, 0)), "Teleport east");

            // --- Global: info/announcements ---
            registry.Register(KeyCode.G, KeyContext.Global, GameInfoAnnouncer.AnnounceGilAmount, "Announce Gil");
            registry.Register(KeyCode.M, KeyModifier.Shift, KeyContext.Global, mod.ToggleMapExitFilter, "Toggle map exit filter");
            registry.Register(KeyCode.M, KeyModifier.None, KeyContext.Global, GameInfoAnnouncer.AnnounceCurrentMap, "Announce current map");
            registry.Register(KeyCode.V, KeyContext.Global, AnnounceVehicleState, "Announce vehicle state");
            registry.Register(KeyCode.I, KeyModifier.Shift, KeyContext.Global, KeyHelpReader.AnnounceKeyHelp, "Announce visible controls");
            registry.Register(KeyCode.I, KeyModifier.None, KeyContext.Global, HandleItemDetailsKey, "Item details / config tooltip");

            // --- Battle-only: character status ---
            registry.Register(KeyCode.H, KeyContext.Battle, GameInfoAnnouncer.AnnounceCharacterStatus, "Announce character status");

            // --- Field-only toggles (blocked in battle with feedback) ---
            RegisterFieldWithBattleFeedback(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldWithBattleFeedback(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldWithBattleFeedback(KeyCode.Alpha9, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts ---
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.Shift, mod.ResetToAllCategory, "Reset to All category");
            RegisterFieldWithBattleFeedback(KeyCode.Equals, KeyModifier.None, mod.CycleNextCategory, "Next entity category (global)");
            RegisterFieldWithBattleFeedback(KeyCode.Minus, KeyModifier.None, mod.CyclePreviousCategory, "Previous entity category (global)");

            // --- Debug: dump untranslated entity names ---
            registry.Register(KeyCode.Alpha0, KeyContext.Global, DumpUntranslatedEntityNames, "Dump untranslated entity names");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        /// <summary>
        /// Called each frame to check for mod hotkey input.
        /// Uses early exit when no key is pressed to minimize overhead.
        /// </summary>
        public void CheckInput()
        {
            // Handle text input window first (consumes all input when open)
            if (TextInputWindow.HandleInput())
                return;

            // Handle confirmation dialog (consumes all input when open)
            if (ConfirmationDialog.HandleInput())
                return;

            // Handle mod menu input (consumes all input when open)
            if (ModMenu.HandleInput())
                return;

            if (!Input.anyKeyDown)
                return;

            // F8 to open mod menu (only when not in battle)
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (!FFII_ScreenReaderMod.IsInBattle)
                    ModMenu.Open();
                else
                    FFII_ScreenReaderMod.SpeakText(T("Unavailable in battle"), interrupt: true);
                return;
            }

            // Handle function keys (F1/F3/F5 -- special coroutine/toggle logic)
            HandleFunctionKeyInput();

            // Skip hotkeys when player is typing in a text field
            if (IsInputFieldFocused())
                return;

            // Determine active context and modifiers
            KeyContext activeContext = DetermineContext();
            KeyModifier currentModifiers = GetCurrentModifiers();

            // Dispatch all registered bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private KeyContext DetermineContext()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive && tracker.ValidateState())
                return KeyContext.Status;

            if (FFII_ScreenReaderMod.IsInBattle)
                return KeyContext.Battle;

            return KeyContext.Field;
        }

        private KeyModifier GetCurrentModifiers()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (Input.GetKeyDown(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            // F1 toggles walk/run speed - announce after game processes it
            if (Input.GetKeyDown(KeyCode.F1))
            {
                CoroutineManager.StartManaged(AnnounceWalkRunState());
                return;
            }

            // F3 toggles encounters - announce after game processes it
            if (Input.GetKeyDown(KeyCode.F3))
            {
                CoroutineManager.StartManaged(AnnounceEncounterState());
                return;
            }

            // F5 cycles enemy HP display (only when not in battle)
            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (!FFII_ScreenReaderMod.IsInBattle)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);

                    string[] options = { "Numbers", "Percentage", "Hidden" };
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), T(options[next])), interrupt: true);
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText(T("Unavailable in battle"), interrupt: true);
                }
            }
        }

        private void AnnounceVehicleState()
        {
            if (!mod.EnsureFieldContext())
                return;

            try
            {
                int moveState = MoveStateHelper.GetCurrentMoveState();
                string stateName = MoveStateHelper.GetMoveStateName(moveState);
                FFII_ScreenReaderMod.SpeakText(stateName);
            }
            catch { }
        }

        private void HandleItemDetailsKey()
        {
            if (IsConfigMenuActive())
            {
                AnnounceConfigTooltip();
            }
            else if (ShopMenuTracker.ValidateState())
            {
                ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
            }
        }

        /// <summary>
        /// Checks if a config menu is currently active.
        /// </summary>
        private bool IsConfigMenuActive()
        {
            try
            {
                var keyInputController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                    return true;

                var touchController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                    return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Announces the description/tooltip text for the currently highlighted config option.
        /// </summary>
        private void AnnounceConfigTooltip()
        {
            try
            {
                // Try KeyInput controller first (keyboard/gamepad mode)
                var keyInputController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    string description = GetConfigDescriptionText(keyInputController);
                    if (!string.IsNullOrEmpty(description))
                    {
                        FFII_ScreenReaderMod.SpeakText(description);
                        return;
                    }
                }

                // Try Touch controller
                var touchController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    string description = GetConfigDescriptionTextTouch(touchController);
                    if (!string.IsNullOrEmpty(description))
                    {
                        FFII_ScreenReaderMod.SpeakText(description);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading config tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the description text from a KeyInput ConfigActualDetailsControllerBase.
        /// Uses pointer offset 0xA0 for descriptionText field.
        /// </summary>
        private string GetConfigDescriptionText(ConfigActualDetailsControllerBase_KeyInput controller)
        {
            if (controller == null) return null;

            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return null;

                IntPtr textPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(ptr + 0xA0);
                if (textPtr == IntPtr.Zero) return null;

                var descText = new UnityEngine.UI.Text(textPtr);
                if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                {
                    return descText.text.Trim();
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets the description text from a Touch ConfigActualDetailsControllerBase.
        /// Uses pointer offset 0x50 for descriptionText field.
        /// </summary>
        private string GetConfigDescriptionTextTouch(ConfigActualDetailsControllerBase_Touch controller)
        {
            if (controller == null) return null;

            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return null;

                IntPtr textPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(ptr + 0x50);
                if (textPtr == IntPtr.Zero) return null;

                var descText = new UnityEngine.UI.Text(textPtr);
                if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                {
                    return descText.text.Trim();
                }
            }
            catch { }

            return null;
        }

        private void DumpUntranslatedEntityNames()
        {
            try
            {
                string result = EntityTranslator.DumpUntranslatedNames();
                FFII_ScreenReaderMod.SpeakText(result, true);
            }
            catch
            {
                FFII_ScreenReaderMod.SpeakText(T("Failed to dump entity names"), true);
            }
        }

        /// <summary>
        /// Checks if a Unity InputField is currently focused.
        /// </summary>
        private bool IsInputFieldFocused()
        {
            try
            {
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;
                if (currentObj == null)
                    return false;

                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Coroutine that announces walk/run state after game processes F1 key.
        /// </summary>
        private static IEnumerator AnnounceWalkRunState()
        {
            // Wait 3 frames for game to fully process F1 and update dashFlag
            yield return null;
            yield return null;
            yield return null;

            try
            {
                bool isDashing = MoveStateHelper.GetDashFlag();
                string state = isDashing ? T("Run") : T("Walk");
                FFII_ScreenReaderMod.SpeakText(state, interrupt: true);
            }
            catch { }
        }

        /// <summary>
        /// Coroutine that announces encounter state after game processes F3 key.
        /// </summary>
        private static IEnumerator AnnounceEncounterState()
        {
            yield return null;
            try
            {
                var userData = Il2CppLast.Management.UserDataManager.Instance();
                if (userData?.CheatSettingsData != null)
                {
                    bool enabled = userData.CheatSettingsData.IsEnableEncount;
                    string state = enabled ? T("Encounters on") : T("Encounters off");
                    FFII_ScreenReaderMod.SpeakText(state, interrupt: true);
                }
            }
            catch { }
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
