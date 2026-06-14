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
            // R repeats the current dialogue page (silent when no message window is open).
            // The Status context registers its own R (repeat current stat) above; the
            // registry prefers the more-specific Status binding on the status screen.
            registry.Register(KeyCode.R, KeyModifier.None, KeyContext.Global, HandleRepeatDialogueKey, "Repeat dialogue");

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
            // Poll SDL3 gamepad + GetAsyncKeyState keyboard once per frame.
            // Must come before any mod input handling so edge-detection state is fresh.
            GamepadManager.Update();

            // Suppress Unity legacy Input when the mod is consuming. Safe because the mod reads
            // keyboard via GetAsyncKeyState (unaffected by ResetInputAxes). This + the
            // InputSystemManager patches = complete game keyboard suppression, and it works even
            // when no gamepad is connected (the passthrough patches early-return without one).
            if (ControllerRouter.SuppressGameInput)
                Input.ResetInputAxes();

            // Route controller inputs to the appropriate state-machine bucket.
            // ControllerRouter also computes IsFieldActive for audio suppression even
            // without a gamepad, so it runs every frame.
            ControllerRouter.Update(DetermineContext());

            // Per-frame footstep tile-crossing poll (field-active gated, silent in vehicles).
            // Cadence naturally tracks actual movement speed — walk slower than dash. Runs in
            // the existing input loop, not a new per-frame Harmony patch.
            MovementSoundPatches.PollFootsteps();

            // Light per-frame poll for walk/run (auto-dash) — announces on change so F1, L3,
            // the config menu and cheat menu all surface through the screen reader. Read-only.
            Handlers.GameToggleAnnouncer.Poll();

            if (GamepadManager.AnyKeyboardKeyDown())
                ControllerRouter.NotifyKeyboardInput();

            // Handle text input window first (consumes all input when open)
            if (TextInputWindow.HandleInput())
                return;

            // Handle confirmation dialog (consumes all input when open)
            if (ConfirmationDialog.HandleInput())
                return;

            // Handle mod menu input (consumes all input when open)
            if (ModMenu.HandleInput())
                return;

            // Game-context hotkeys below only fire when the game window is the foreground
            // window, so mod functions don't trigger while the player is in another app.
            // (The mod's own dialogs/menu above handle their own input and may hold focus.)
            if (!WindowsFocusHelper.IsGameWindowFocused())
                return;

            if (!GamepadManager.AnyKeyboardKeyDown())
                return;

            // Bare F-keys only fire with no modifier held, so OS shortcuts like Alt+F4
            // (close window), Ctrl+F-keys and Shift+F-keys don't trigger the screen
            // reader. Explicit Shift/Ctrl bindings still match via GetCurrentModifiers.
            bool anyModifierHeld = IsAnyModifierHeld();

            // F8 to open mod menu — gated to field-only via ControllerRouter.IsFieldActive
            // (blocks battle, in-game menus, title/boot screen, transitions — IsFieldActive is
            // robust because DetermineContext only reports Field when a field player exists).
            // Rejection wording lives in SpeakModMenuUnavailable so Start and F8 stay in sync.
            if (!anyModifierHeld && GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                if (ControllerRouter.IsFieldActive)
                    ModMenu.Open();
                else
                    ControllerRouter.SpeakModMenuUnavailable();
                return;
            }

            // Handle function keys (F1/F3/F5 -- special coroutine/toggle logic) — bare keypress only
            if (!anyModifierHeld)
                HandleFunctionKeyInput();

            // Skip hotkeys when player is typing in a text field
            if (IsInputFieldFocused())
                return;

            // Determine active context and modifiers
            KeyContext activeContext = DetermineContext();
            KeyModifier currentModifiers = GetCurrentModifiers();

            // Alt held with no registered Alt-binding → skip dispatch so Alt+<key> doesn't
            // accidentally trigger the unmodified binding. (Shift/Ctrl are routed through
            // currentModifiers and matched exactly by the registry, so they still work.)
            if (IsAltHeld())
                return;

            // Dispatch all registered bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private static bool IsAltHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private static bool IsAnyModifierHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private KeyContext DetermineContext()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive && tracker.ValidateState())
                return KeyContext.Status;

            if (FFII_ScreenReaderMod.IsInBattle)
                return KeyContext.Battle;

            // Only return Field if a field player actually exists (matches FF1). Prevents the
            // Field context — and thus mod-menu/F5 opening, which gate on IsFieldActive — from
            // being reported on the title screen, boot screen, or during map transitions, where
            // no FieldPlayerController is present. Everything else is Global (Global bindings
            // still fire; Field-only bindings correctly don't).
            try
            {
                var pc = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (pc?.fieldPlayer != null)
                    return KeyContext.Field;
            }
            catch { }

            return KeyContext.Global;
        }

        private KeyModifier GetCurrentModifiers()
        {
            bool shift = GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift) || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift);
            bool ctrl = GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl) || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (GamepadManager.IsKeyCodePressed(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            // F7 toggles AutoDetail (auto-announce descriptions/stats on focus)
            if (GamepadManager.IsKeyCodePressed(KeyCode.F7))
            {
                FFII_ScreenReaderMod.Instance?.ToggleAutoDetail();
                return;
            }

            // F1 walk/run is announced by GameToggleAnnouncer.Poll() (handles F1 + config menu).

            // F3 toggles encounters - announce after game processes it
            if (GamepadManager.IsKeyCodePressed(KeyCode.F3))
            {
                CoroutineManager.StartManaged(AnnounceEncounterState());
                return;
            }

            // F5 cycles enemy HP display (field-only via ControllerRouter.IsFieldActive,
            // which now excludes battle, menus, and non-field screens — matches FF1).
            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                if (ControllerRouter.IsFieldActive)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);

                    string[] options = { "Numbers", "Percentage", "Hidden" };
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), T(options[next])), interrupt: true);
                }
                else
                {
                    ControllerRouter.SpeakModMenuUnavailable();
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

        /// <summary>
        /// R key: re-speak the current dialogue page. Silent when no message window is
        /// open so R does nothing on the field/in battle (matches FF1).
        /// </summary>
        private static void HandleRepeatDialogueKey()
        {
            if (DialogueTracker.IsInDialogue)
                DialogueTracker.RepeatLastDialogue();
        }

        internal static void HandleItemDetailsKey()
        {
            // Read the live UI panel for the active menu (FF1 pattern) so the detail key
            // reads exactly what's on screen — the full stats panel or the description,
            // whichever the player has toggled to — instead of master-data lookups.
            if (IsConfigMenuActive())
            {
                AnnounceConfigTooltip();
            }
            else if (ShopMenuTracker.ValidateState())
            {
                ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
            }
            else if (EquipMenuState.IsActive)
            {
                EquipDetailsAnnouncer.AnnounceCurrentItemDetails();
            }
            else if (ItemMenuState.IsActive)
            {
                ItemDetailsAnnouncer.AnnounceCurrentItemDetails();
            }
            else if (MagicMenuState.IsActive)
            {
                // Spells have no stats panel — the cached description is the detail.
                string detail = MenuDetailCache.LastDetail;
                FFII_ScreenReaderMod.SpeakText(
                    string.IsNullOrWhiteSpace(detail) ? T("No details") : detail,
                    interrupt: true);
            }
        }

        /// <summary>
        /// Checks if a config menu is currently active.
        /// </summary>
        private static bool IsConfigMenuActive()
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
        private static void AnnounceConfigTooltip()
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
        private static string GetConfigDescriptionText(ConfigActualDetailsControllerBase_KeyInput controller)
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
        private static string GetConfigDescriptionTextTouch(ConfigActualDetailsControllerBase_Touch controller)
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
