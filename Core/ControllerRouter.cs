using System;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Patches;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

namespace FFII_ScreenReader.Core
{
    public enum ControllerState
    {
        Normal,
        ModMode,
        ModMenu
    }

    /// <summary>
    /// Central controller routing — state machine that decides where each SDL input goes.
    /// SDL consumes ALL controller buttons. Default behavior is pass to game.
    /// Mod functions consume specific buttons; everything else passes through via InputPassthroughPatches.
    /// </summary>
    public static class ControllerRouter
    {
        public static ControllerState State { get; private set; } = ControllerState.Normal;

        /// <summary>Current game context, set each frame by InputManager.</summary>
        public static KeyContext CurrentGameContext { get; set; } = KeyContext.Global;

        /// <summary>
        /// True when the player is actively on the field with no menu or battle overlay.
        /// Single source of truth — used by ControllerRouter, InputPassthroughPatches,
        /// and FFII_ScreenReaderMod audio suppression. Computed once per frame in Update().
        /// </summary>
        public static bool IsFieldActive { get; private set; } = false;

        /// <summary>
        /// True when the game should receive no input at all.
        /// </summary>
        public static bool SuppressGameInput =>
            State == ControllerState.ModMode
            || State == ControllerState.ModMenu
            || ModMenu.IsOpen
            || TextInputWindow.IsOpen
            || ConfirmationDialog.IsOpen;

        /// <summary>Buttons consumed by the mod this frame (not passed to game).</summary>
        private static readonly bool[] consumedButtons = new bool[SDL3.SDL_GAMEPAD_BUTTON_COUNT];

        public static bool IsButtonConsumed(int btn) =>
            btn >= 0 && btn < SDL3.SDL_GAMEPAD_BUTTON_COUNT && consumedButtons[btn];

        // --- Input device tracking for context-aware help ---
        public enum LastInputDevice { Keyboard, Controller }
        public static LastInputDevice LastDevice { get; private set; } = LastInputDevice.Keyboard;

        /// <summary>Called by InputManager when keyboard input is detected.</summary>
        public static void NotifyKeyboardInput() => LastDevice = LastInputDevice.Keyboard;

        // --- Field state tracking ---
        private static bool leftTriggerWasActive = false;
        private static bool wasLeftStickActive = false;

        // =====================================================================
        // Main update — called from InputManager.Update() every frame
        // =====================================================================

        public static void Update(KeyContext gameContext)
        {
            CurrentGameContext = gameContext;

            // FF1 pattern: don't trust cached menu-state bools — revalidate against reality.
            // If we look like we're on the field but a menu flag is still set, run the
            // self-healing suppression check so flags whose menus have actually closed
            // (notably the keyword menu, whose SetActive(false) hook is unreliable) clear
            // themselves via their ShouldSuppress()/ValidateState() logic. One-shot: a
            // stuck flag self-clears within ~1 frame, after which AnyActive() short-circuits.
            if (gameContext == KeyContext.Field
                && !FFII_ScreenReaderMod.IsInBattle
                && MenuStateRegistry.AnyActive())
            {
                CursorSuppressionCheck.Check(); // invoked for its self-healing side effect
            }

            // Compute field-active state once per frame (single source of truth)
            // This runs even without a gamepad so audio suppression works for keyboard-only users.
            IsFieldActive = gameContext == KeyContext.Field
                && !MenuStateRegistry.AnyActive()
                && !FFII_ScreenReaderMod.IsInBattle;

            if (!GamepadManager.IsAvailable)
                return;

            // Track that controller is being used
            for (int i = 0; i < SDL3.SDL_GAMEPAD_BUTTON_COUNT; i++)
            {
                if (GamepadManager.IsButtonPressed(i))
                {
                    LastDevice = LastInputDevice.Controller;
                    break;
                }
            }

            // Clear consumed flags — all buttons start as "pass to game"
            Array.Clear(consumedButtons, 0, consumedButtons.Length);

            // State transitions (Start → mod menu, Back → mod mode)
            HandleStateTransitions();

            // Route inputs based on current state
            switch (State)
            {
                case ControllerState.Normal:
                    HandleNormalState(gameContext);
                    break;
                case ControllerState.ModMode:
                    HandleModModeState();
                    break;
                case ControllerState.ModMenu:
                    HandleModMenuState();
                    break;
            }
        }

        public static void Reset()
        {
            State = ControllerState.Normal;
            Array.Clear(consumedButtons, 0, consumedButtons.Length);
        }

        // =====================================================================
        // Context-aware controls announcement (RB on controller, Shift+I on keyboard)
        // =====================================================================

        /// <summary>
        /// Announces controls for the current context. Called by RB (controller)
        /// or Shift+I (keyboard). Reads game key help in game menus, mod controls elsewhere.
        /// </summary>
        public static void AnnounceContextControls()
        {
            if (State == ControllerState.ModMode)
            {
                AnnounceModModeControls();
            }
            else if (State == ControllerState.ModMenu || ModMenu.IsOpen)
            {
                AnnounceModMenuControls();
            }
            else
            {
                // On field or in game menus — read game's built-in key help
                KeyHelpReader.AnnounceKeyHelp();
            }
        }

        private static void AnnounceModModeControls()
        {
            string back = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_BACK);

            if (DialogueTracker.IsInDialogue)
            {
                string west = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_WEST);
                FFII_ScreenReaderMod.SpeakText(
                    string.Format(T("{0} to repeat dialogue. {1} to cancel."), west, back),
                    interrupt: true);
            }
            else if (FFII_ScreenReaderMod.IsInBattle)
            {
                string west = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_WEST);
                FFII_ScreenReaderMod.SpeakText(
                    string.Format(T("{0} for party HP. {1} to cancel."), west, back),
                    interrupt: true);
            }
            else
            {
                string west = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_WEST);
                string north = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_NORTH);
                FFII_ScreenReaderMod.SpeakText(
                    string.Format(T("{0} for Gil. {1} for location. Right stick to teleport. {2} to cancel."),
                    west, north, back),
                    interrupt: true);
            }
        }

        private static void AnnounceModMenuControls()
        {
            string confirm = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_SOUTH);
            string close = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_EAST);
            string start = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_START);

            FFII_ScreenReaderMod.SpeakText(
                string.Format(T("D-pad or Left Stick Up, Down to navigate. Left, Right to adjust values. {0} to toggle. {1} or {2} to close."),
                confirm, close, start),
                interrupt: true);
        }

        // =====================================================================
        // State transitions
        // =====================================================================

        private static void HandleStateTransitions()
        {
            // Start → mod menu toggle
            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_START))
            {
                ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_START);

                if (State == ControllerState.ModMenu)
                    CloseModMenu();
                else if (IsFieldActive)
                    OpenModMenu();
                else
                    SpeakModMenuUnavailable();
                return;
            }

            // Back/Select → mod mode toggle (Normal ↔ ModMode)
            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_BACK))
            {
                ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_BACK);

                if (State == ControllerState.Normal)
                {
                    State = ControllerState.ModMode;
                    FFII_ScreenReaderMod.SpeakText(T("Mod"), interrupt: true);
                }
                else if (State == ControllerState.ModMode)
                {
                    State = ControllerState.Normal;
                    FFII_ScreenReaderMod.SpeakText(T("Cancelled"), interrupt: true);
                }
            }
        }

        private static void OpenModMenu()
        {
            State = ControllerState.ModMenu;
            ModMenu.Open(); // speaks "Mod menu" + first item
        }

        /// <summary>
        /// Announces why the mod menu can't be opened. Shared by Start-button and F8 entry points.
        /// </summary>
        internal static void SpeakModMenuUnavailable()
        {
            string reason;
            if (FFII_ScreenReaderMod.IsInBattle)               reason = T("Unavailable in battle");
            else if (MenuStateRegistry.AnyActive())            reason = T("Unavailable in menu");
            else                                               reason = T("Unavailable here");
            FFII_ScreenReaderMod.SpeakText(reason, interrupt: true);
        }

        private static void CloseModMenu()
        {
            State = ControllerState.Normal;
            ModMenu.Close(); // speaks "Mod menu closed"
        }

        // =====================================================================
        // NORMAL state
        // =====================================================================

        private static void HandleNormalState(KeyContext context)
        {
            // Face buttons (A/B/X/Y) interrupt queued speech — same as Enter on keyboard.
            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_SOUTH)
             || GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_EAST)
             || GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST)
             || GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_NORTH))
                FFII_ScreenReaderMod.InterruptSpeech();

            if (IsFieldActive)
                HandleNormalField();
            else
                HandleNormalNonField(context);

            // LB/RB always pass through to game (used for tab switching in status/menus)
        }

        private static void HandleNormalField()
        {
            var mod = FFII_ScreenReaderMod.Instance;
            if (mod == null) return;

            // When Stick Click Normalization is ON, R3/L3 fall through to the game (encounter
            // toggle / auto-dash). Mod functions move to MOD_MODE. When OFF, mod handles them.
            if (!FFII_ScreenReaderMod.StickClickNormalizationEnabled)
            {
                // R3 → toggle pathfinding filter
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK))
                {
                    ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK);
                    mod.TogglePathfindingFilter();
                    return;
                }

                // L3 → toggle beacon navigation mode
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK))
                {
                    ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK);
                    mod.ToggleAudioBeacons();
                    return;
                }
            }

            // Interrupt speech on any navigation input
            bool leftStickActive = GamepadManager.LeftStickX != 0f || GamepadManager.LeftStickY != 0f;
            bool leftStickJustMoved = leftStickActive && !wasLeftStickActive;
            wasLeftStickActive = leftStickActive;

            bool anyNavInput = leftStickJustMoved
                || GamepadManager.DpadUpPressed || GamepadManager.DpadDownPressed
                || GamepadManager.DpadLeftPressed || GamepadManager.DpadRightPressed
                || GamepadManager.RStickUpPressed || GamepadManager.RStickDownPressed
                || GamepadManager.RStickLeftPressed || GamepadManager.RStickRightPressed;

            if (anyNavInput)
                FFII_ScreenReaderMod.InterruptSpeech();

            // D-pad → waypoint navigation (consumed). The callees mark the tracker.
            if (GamepadManager.DpadUpPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_UP); mod.WaypointCyclePrevious(); NavigationTargetTracker.MarkWaypoint(); }
            if (GamepadManager.DpadDownPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_DOWN); mod.WaypointCycleNext(); NavigationTargetTracker.MarkWaypoint(); }
            if (GamepadManager.DpadLeftPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_LEFT); mod.WaypointCyclePreviousCategory(); NavigationTargetTracker.MarkWaypoint(); }
            if (GamepadManager.DpadRightPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_RIGHT); mod.WaypointCycleNextCategory(); NavigationTargetTracker.MarkWaypoint(); }

            // Right stick → entity scanner (callees mark the tracker).
            if (GamepadManager.RStickUpPressed) { mod.CyclePrevious(); NavigationTargetTracker.MarkEntity(); }
            if (GamepadManager.RStickDownPressed) { mod.CycleNext(); NavigationTargetTracker.MarkEntity(); }
            if (GamepadManager.RStickLeftPressed) { mod.CyclePreviousCategory(); NavigationTargetTracker.MarkEntity(); }
            if (GamepadManager.RStickRightPressed) { mod.CycleNextCategory(); NavigationTargetTracker.MarkEntity(); }

            // Left trigger → pathfind to last selected target (or restart beacon in beacon nav mode)
            if (GamepadManager.LeftTrigger > 0.5f && !leftTriggerWasActive)
            {
                switch (NavigationTargetTracker.LastKind)
                {
                    case NavigationTargetTracker.Kind.Waypoint:
                        mod.WaypointPathfind();
                        break;
                    case NavigationTargetTracker.Kind.Entity:
                        if (PreferencesManager.AudioBeaconsEnabled) mod.RestartEntityBeacon();
                        else mod.AnnounceCurrentEntity();
                        break;
                    default:
                        FFII_ScreenReaderMod.SpeakText(T("No target selected"), interrupt: true);
                        break;
                }
            }
            leftTriggerWasActive = GamepadManager.LeftTrigger > 0.5f;
        }

        private static void HandleNormalNonField(KeyContext context)
        {
            // D-pad and left stick → virtual buffer navigation in Status
            if (context == KeyContext.Status)
            {
                if (GamepadManager.DpadUpPressed || GamepadManager.LeftStickUpPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_UP); StatusNavigationReader.NavigatePrevious(); }
                if (GamepadManager.DpadDownPressed || GamepadManager.LeftStickDownPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_DOWN); StatusNavigationReader.NavigateNext(); }
            }

            // Right stick up → read the focused item's description/stats (gamepad equivalent
            // of the I key). No-ops when no item/shop/magic/equip/config panel is active.
            if (GamepadManager.RStickUpPressed)
                InputManager.HandleItemDetailsKey();

            // Right stick down → announce visible game key help (gamepad equivalent of Shift+I).
            // Right stick isn't a game button so no ConsumeButton needed.
            if (GamepadManager.RStickDownPressed)
                KeyHelpReader.AnnounceKeyHelp();
        }

        // =====================================================================
        // MOD_MODE — face buttons → mod info, then auto-deactivate
        // =====================================================================

        private static void HandleModModeState()
        {
            // All buttons consumed in mod mode
            for (int i = 0; i < SDL3.SDL_GAMEPAD_BUTTON_COUNT; i++)
                consumedButtons[i] = true;

            var mod = FFII_ScreenReaderMod.Instance;
            if (mod == null) return;

            // Dialogue takes precedence over battle/field — if a message window is up,
            // the user wants to repeat the message, not check HP or Gil (matches FF1).
            // Needed because field dialogue still reads as IsFieldActive in FF2.
            if (DialogueTracker.IsInDialogue)
            {
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST))
                { DialogueTracker.RepeatLastDialogue(); State = ControllerState.Normal; return; }
            }
            else if (FFII_ScreenReaderMod.IsInBattle)
            {
                // Battle mod mode: X = party HP check
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST))
                { mod.AnnounceCharacterStatus(); State = ControllerState.Normal; return; }
            }
            else if (IsFieldActive)
            {
                // Field mod mode: X=Gil, Y=Location
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST))
                { mod.AnnounceGilAmount(); State = ControllerState.Normal; return; }

                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_NORTH))
                { mod.AnnounceCurrentMap(); State = ControllerState.Normal; return; }

                // When Stick Click Normalization is on, the stick-click mod functions move
                // here so the player can still reach them via mod button + R3/L3.
                if (FFII_ScreenReaderMod.StickClickNormalizationEnabled)
                {
                    if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK))
                    { mod.TogglePathfindingFilter(); State = ControllerState.Normal; return; }

                    if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK))
                    { mod.ToggleAudioBeacons(); State = ControllerState.Normal; return; }
                }

                // Right stick → teleport (field only)
                if (GamepadManager.RStickUpPressed)
                { mod.TeleportInDirection(new Vector2(0, 16)); State = ControllerState.Normal; return; }

                if (GamepadManager.RStickDownPressed)
                { mod.TeleportInDirection(new Vector2(0, -16)); State = ControllerState.Normal; return; }

                if (GamepadManager.RStickLeftPressed)
                { mod.TeleportInDirection(new Vector2(-16, 0)); State = ControllerState.Normal; return; }

                if (GamepadManager.RStickRightPressed)
                { mod.TeleportInDirection(new Vector2(16, 0)); State = ControllerState.Normal; return; }
            }
            else
            {
                // A menu or other non-field overlay is active (not battle, not field).
                // Field mod functions — especially teleport, which would move the player
                // underneath the menu — are unavailable here. Do nothing field-specific.
            }

            // Right stick down → announce mod mode controls. In field, right stick down
            // triggers teleport south above and returns before reaching here.
            if (!IsFieldActive && GamepadManager.RStickDownPressed)
                AnnounceModModeControls();
        }

        // =====================================================================
        // MOD_MENU — controller navigates mod menu, all game input suppressed
        // =====================================================================

        private static void HandleModMenuState()
        {
            // All buttons consumed
            for (int i = 0; i < SDL3.SDL_GAMEPAD_BUTTON_COUNT; i++)
                consumedButtons[i] = true;

            if (!ModMenu.IsOpen)
            {
                State = ControllerState.Normal;
                return;
            }

            bool up = GamepadManager.DpadUpPressed || GamepadManager.LeftStickUpPressed;
            bool down = GamepadManager.DpadDownPressed || GamepadManager.LeftStickDownPressed;
            bool left = GamepadManager.DpadLeftPressed || GamepadManager.LeftStickLeftPressed;
            bool right = GamepadManager.DpadRightPressed || GamepadManager.LeftStickRightPressed;

            if (up) ModMenu.ControllerNavigatePrevious();
            if (down) ModMenu.ControllerNavigateNext();
            if (left) ModMenu.ControllerAdjustCurrentItem(-1);
            if (right) ModMenu.ControllerAdjustCurrentItem(1);

            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_SOUTH))
                ModMenu.ControllerToggleCurrentItem();

            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_EAST))
                CloseModMenu();

            // Right stick down → announce mod menu controls
            if (GamepadManager.RStickDownPressed)
                AnnounceModMenuControls();
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static void ConsumeButton(int btn)
        {
            if (btn >= 0 && btn < SDL3.SDL_GAMEPAD_BUTTON_COUNT)
                consumedButtons[btn] = true;
        }
    }
}
