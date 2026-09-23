using System;
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

        private void RegisterFieldOnly(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            // Field-only action. Off-field (menu/battle/title) the active context is never
            // Field, so this binding has no match and dispatch silently does nothing.
            registry.Register(key, modifier, KeyContext.Field, action, description);
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

            // --- Bestiary detail: navigation ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToTop, "Jump to first stat (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToPreviousGroup, "Jump to previous group (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigatePrevious, "Previous stat (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToBottom, "Jump to last stat (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToNextGroup, "Jump to next group (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigateNext, "Next stat (bestiary)");

            // --- Controls list (config Gamepad/Keyboard help): navigation (flat list, no groups) ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.KeyHelp, KeyHelpReader.JumpToTop, "Jump to first control");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.KeyHelp, KeyHelpReader.NavigatePrevious, "Previous control");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.KeyHelp, KeyHelpReader.JumpToBottom, "Jump to last control");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.KeyHelp, KeyHelpReader.NavigateNext, "Next control");

            // --- Field: entity navigation (brackets + backslash) -- with battle feedback ---
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.None, () =>
            {
                NavigationTargetTracker.MarkEntity();
                if (PreferencesManager.AudioBeaconsEnabled) mod.RestartEntityBeacon();
                else mod.AnnounceCurrentEntity();
            }, "Announce current entity / restart beacon");

            // --- Field: manual entity rescan (backtick) ---
            RegisterFieldOnly(KeyCode.BackQuote, KeyModifier.None, mod.ForceEntityRescan, "Force entity rescan");

            // --- Field: alternate keys (J/K/L/P) -- with battle feedback ---
            RegisterFieldOnly(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldOnly(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldOnly(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.None, () =>
            {
                NavigationTargetTracker.MarkEntity();
                if (PreferencesManager.AudioBeaconsEnabled) mod.RestartEntityBeacon();
                else mod.AnnounceCurrentEntity();
            }, "Announce current entity / restart beacon (alt)");

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
            registry.Register(KeyCode.V, KeyContext.Global, GameInfoAnnouncer.AnnounceVehicleState, "Announce vehicle state");
            registry.Register(KeyCode.I, KeyModifier.Shift, KeyContext.Global, ControllerRouter.AnnounceContextControls, "Announce controls");
            registry.Register(KeyCode.I, KeyModifier.None, KeyContext.Global, HandleItemDetailsKey, "Item details / config tooltip");
            registry.Register(KeyCode.U, KeyContext.Global, AnnounceEquipRestrictions, "Who can equip");
            registry.Register(KeyCode.Tab, KeyContext.Global, HandleTabKey, "Clear stale battle state");
            // R repeats the current dialogue page (silent when no message window is open).
            registry.Register(KeyCode.R, KeyModifier.None, KeyContext.Global, HandleRepeatDialogueKey, "Repeat dialogue");
            // H: active character's status in battle; says so when pressed outside battle (FF1 parity).
            registry.Register(KeyCode.H, KeyContext.Global, GameInfoAnnouncer.AnnounceCharacterStatus, "Announce character status");

            // --- Field-only toggles (blocked in battle with feedback) ---
            RegisterFieldOnly(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldOnly(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldOnly(KeyCode.F6, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts ---
            RegisterFieldOnly(KeyCode.K, KeyModifier.Shift, mod.ResetToAllCategory, "Reset to All category");
            RegisterFieldOnly(KeyCode.Equals, KeyModifier.None, mod.CycleNextCategory, "Next entity category (global)");
            RegisterFieldOnly(KeyCode.Minus, KeyModifier.None, mod.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        /// <summary>
        /// Called each frame to check for mod hotkey input.
        /// Uses early exit when no key is pressed to minimize overhead.
        /// </summary>
        public void Update()
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

            // Skip ALL mod hotkeys (including F8 and the function keys) while the player is
            // typing in the game's own text field, so naming/input screens aren't disrupted.
            if (IsInputFieldFocused()) return;

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
            // The config controls list takes priority while shown.
            if (KeyHelpReader.IsScreenActive)
                return KeyContext.KeyHelp;

            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive && tracker.ValidateState())
                return KeyContext.Status;

            var bestiaryTracker = BestiaryNavigationTracker.Instance;
            if (bestiaryTracker.IsNavigationActive && bestiaryTracker.ValidateState())
                return KeyContext.BestiaryDetail;

            if (FFII_ScreenReaderMod.IsInBattle)
                return KeyContext.Battle;

            // Field keys only fire while actively on a field map with no menu open.
            // Otherwise fall through to Global so field/entity/waypoint/toggle hotkeys
            // are silent no-ops off-field, while Global info keys still work everywhere.
            if (IsOnValidMap() && !MenuStateRegistry.AnyActive())
                return KeyContext.Field;

            return KeyContext.Global;
        }

        private static bool IsOnValidMap()
        {
            // Self-heal the cache (like every other FieldPlayerController reader) so a cleared or
            // stale entry can't wedge the field context into Global and silently disable field hotkeys.
            try
            {
                var pc = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>()
                         ?? GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();
                return pc?.fieldPlayer != null;
            }
            catch { return false; }
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

        private static bool IsBufferContext(KeyContext ctx)
            => ctx == KeyContext.Status || ctx == KeyContext.BestiaryDetail || ctx == KeyContext.KeyHelp;

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (GamepadManager.IsKeyCodePressed(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }

            // W/S as alternative Up/Down arrows ONLY in navigation-buffer screens, so game WASD
            // movement and letter hotkeys elsewhere are untouched. Modifiers carry (Shift+W = previous group).
            if (IsBufferContext(activeContext))
            {
                if (GamepadManager.IsKeyCodePressed(KeyCode.W)) registry.TryExecute(KeyCode.UpArrow, currentModifiers, activeContext);
                if (GamepadManager.IsKeyCodePressed(KeyCode.S)) registry.TryExecute(KeyCode.DownArrow, currentModifiers, activeContext);
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

            // F1 walk/run is announced by GameToggleAnnouncer.Poll() (handles F1 + config menu);
            // F3 encounters by GameToggleAnnouncer's CheatSettingsClient.SetIsEnableEncount hook.

            // F5 cycles enemy HP display. Enemy HP Display is a battle feature, so gate on
            // in-battle (not IsFieldActive, which is false during battle).
            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                if (FFII_ScreenReaderMod.IsInBattle)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);

                    string[] options = { T("Numbers"), T("Percentage"), T("Hidden") };
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), options[next]), interrupt: true);
                }
                else
                {
                    ControllerRouter.SpeakModMenuUnavailable();
                }
            }
        }

        /// <summary>
        /// U key: FF2 has no class/job equip restrictions, so in the shop and equipment menus (where
        /// FF1's U names the classes that can equip) it says so. Silent elsewhere.
        /// </summary>
        internal static void AnnounceEquipRestrictions()
        {
            if (ShopMenuTracker.ValidateState() || EquipMenuState.IsActive)
                FFII_ScreenReaderMod.SpeakText(T("Any character can equip this"), interrupt: true);
        }

        /// <summary>
        /// Tab opens the field menu. If the in-battle flag is still set while no battle exists (an exit
        /// path that skipped every battle-end hook), clear it so menus and field keys work again.
        /// </summary>
        private static void HandleTabKey()
        {
            try
            {
                if (FFII_ScreenReaderMod.IsInBattle && Object.FindObjectOfType<Il2CppLast.Battle.BattleController>() == null)
                    FFII_ScreenReaderMod.ClearBattleState();
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
            else if (KeywordMenuState.IsActive || WordsMenuState.IsActive)
            {
                // Keyword/Words entries have no live stats panel — the cached description is the detail.
                string detail = MenuDetailCache.LastDetail;
                FFII_ScreenReaderMod.SpeakText(
                    string.IsNullOrWhiteSpace(detail) ? T("No details") : detail,
                    interrupt: true);
            }
            else if (BattleMagicMenuState.ShouldSuppress() || BattleItemMenuState.ShouldSuppress())
            {
                // Battle spell/item lists (validated: the list is still on screen): the focused entry's
                // description is cached on every cursor move — the only way to reach it in battle with
                // AutoDetail off (FF1 parity).
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
        /// Cleans up resources.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
