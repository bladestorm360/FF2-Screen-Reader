using System;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Patches;
using Object = UnityEngine.Object;

// Type aliases for IL2CPP config controllers
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Uses Unity's legacy Input system for simplicity and reliability.
    /// </summary>
    public class InputManager : IDisposable
    {
        private readonly FFII_ScreenReaderMod mod;

        public InputManager(FFII_ScreenReaderMod mod)
        {
            this.mod = mod;
        }

        /// <summary>
        /// Initializes the input manager.
        /// </summary>
        public void Initialize()
        {
            MelonLogger.Msg("InputManager initialized");
        }

        /// <summary>
        /// Called each frame to check for mod hotkey input.
        /// Uses early exit when no key is pressed to minimize overhead.
        /// </summary>
        public void CheckInput()
        {
            // Early exit if no key pressed this frame
            if (!Input.anyKeyDown)
                return;

            // Skip if an input field is focused
            if (IsInputFieldFocused())
                return;

            // Get modifier state
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // Status navigation keys (takes priority)
            if (HandleStatusDetailsInput(shiftHeld, ctrlHeld))
                return;

            // Global hotkeys
            HandleGlobalInput(shiftHeld, ctrlHeld);

            // Field-specific hotkeys
            HandleFieldInput(shiftHeld);
        }

        /// <summary>
        /// Handles input when on the field (entity navigation).
        /// </summary>
        private void HandleFieldInput(bool shiftHeld)
        {
            // J or [ to cycle backwards
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                if (shiftHeld)
                    mod.CyclePreviousCategory();
                else
                    mod.CyclePrevious();
                return;
            }

            // K to repeat current entity (without shift)
            if (Input.GetKeyDown(KeyCode.K))
            {
                if (!shiftHeld)
                    mod.AnnounceEntityOnly();
                return;
            }

            // L or ] to cycle forwards
            if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                if (shiftHeld)
                    mod.CycleNextCategory();
                else
                    mod.CycleNext();
                return;
            }

            // P or \ to pathfind/announce current entity
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
            {
                if (shiftHeld)
                    mod.TogglePathfindingFilter();
                else
                    mod.AnnounceCurrentEntity();
                return;
            }
        }

        /// <summary>
        /// Handles global input (works everywhere).
        /// </summary>
        private void HandleGlobalInput(bool shiftHeld, bool ctrlHeld)
        {
            // Ctrl+Arrow to teleport
            if (ctrlHeld)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, 16));
                    return;
                }
                if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, -16));
                    return;
                }
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    mod.TeleportInDirection(new Vector2(-16, 0));
                    return;
                }
                if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    mod.TeleportInDirection(new Vector2(16, 0));
                    return;
                }
            }

            // H to announce character health/status (battle only)
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (FFII_ScreenReaderMod.IsInBattle)
                    mod.AnnounceCharacterStatus();
                return;
            }

            // G to announce current gil amount
            if (Input.GetKeyDown(KeyCode.G))
            {
                mod.AnnounceGilAmount();
                return;
            }

            // M to announce current map name
            if (Input.GetKeyDown(KeyCode.M))
            {
                if (shiftHeld)
                    mod.ToggleMapExitFilter();
                else
                    mod.AnnounceCurrentMap();
                return;
            }

            // 0 (Alpha0) to reset to All category
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                mod.ResetToAllCategory();
                return;
            }

            // Shift+K to reset to All category
            if (Input.GetKeyDown(KeyCode.K) && shiftHeld)
            {
                mod.ResetToAllCategory();
                return;
            }

            // = (Equals) to cycle to next category
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                mod.CycleNextCategory();
                return;
            }

            // - (Minus) to cycle to previous category
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                mod.CyclePreviousCategory();
                return;
            }

            // V to announce current vehicle/movement mode
            if (Input.GetKeyDown(KeyCode.V))
            {
                AnnounceCurrentVehicle();
                return;
            }

            // I to announce tooltip/description (config menu or shop)
            if (Input.GetKeyDown(KeyCode.I))
            {
                if (IsConfigMenuActive())
                {
                    AnnounceConfigTooltip();
                }
                else if (ShopMenuTracker.ValidateState())
                {
                    ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                }
                return;
            }
        }

        /// <summary>
        /// Handles input for status details screen navigation.
        /// Returns true if input was consumed (status navigation is active and key was handled).
        /// </summary>
        private bool HandleStatusDetailsInput(bool shiftHeld, bool ctrlHeld)
        {
            var tracker = StatusNavigationTracker.Instance;

            // Check if status navigation is active
            if (!tracker.IsNavigationActive || !tracker.ValidateState())
                return false;

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (ctrlHeld)
                    StatusNavigationReader.JumpToTop();
                else if (shiftHeld)
                    StatusNavigationReader.JumpToPreviousGroup();
                else
                    StatusNavigationReader.NavigatePrevious();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (ctrlHeld)
                    StatusNavigationReader.JumpToBottom();
                else if (shiftHeld)
                    StatusNavigationReader.JumpToNextGroup();
                else
                    StatusNavigationReader.NavigateNext();
                return true;
            }

            // R: Repeat current stat
            if (Input.GetKeyDown(KeyCode.R))
            {
                StatusNavigationReader.ReadCurrentStat();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a config menu is currently active.
        /// </summary>
        private bool IsConfigMenuActive()
        {
            try
            {
                // Check for KeyInput config controller
                var keyInputController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    return true;
                }

                // Check for Touch config controller
                var touchController = Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking config menu state: {ex.Message}");
            }

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
                        MelonLogger.Msg($"[Config Tooltip] {description}");
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
                        MelonLogger.Msg($"[Config Tooltip] {description}");
                        FFII_ScreenReaderMod.SpeakText(description);
                        return;
                    }
                }

                MelonLogger.Msg("[Config Tooltip] No description available");
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
                // Access descriptionText at offset 0xA0
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error accessing KeyInput description text: {ex.Message}");
            }

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
                // Access descriptionText at offset 0x50
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error accessing Touch description text: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Announces the current vehicle/movement mode.
        /// </summary>
        private void AnnounceCurrentVehicle()
        {
            // Only announce if on field map (not title screen, menus, etc.)
            if (!mod.EnsureFieldContext())
                return;

            try
            {
                int moveState = Utils.MoveStateHelper.GetCurrentMoveState();
                string stateName = Utils.MoveStateHelper.GetMoveStateName(moveState);
                MelonLogger.Msg($"[Vehicle] Current movement mode: {stateName}");
                FFII_ScreenReaderMod.SpeakText(stateName);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing vehicle state: {ex.Message}");
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Dispose()
        {
            MelonLogger.Msg("InputManager disposed");
        }
    }
}
