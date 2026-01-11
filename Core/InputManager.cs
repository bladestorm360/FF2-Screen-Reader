using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Patches;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Detects hotkeys and routes them to appropriate mod functions.
    /// </summary>
    public class InputManager
    {
        private readonly FFII_ScreenReaderMod mod;

        public InputManager(FFII_ScreenReaderMod mod)
        {
            this.mod = mod;
        }

        /// <summary>
        /// Called every frame to check for input and route hotkeys.
        /// </summary>
        public void Update()
        {
            if (!Input.anyKeyDown)
            {
                return;
            }

            if (IsInputFieldFocused())
            {
                return;
            }

            HandleFieldInput();
            HandleGlobalInput();
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
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handles input when on the field (entity navigation).
        /// </summary>
        private void HandleFieldInput()
        {
            // Hotkey: J or [ to cycle backwards
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                if (IsShiftHeld())
                {
                    mod.CyclePreviousCategory();
                }
                else
                {
                    mod.CyclePrevious();
                }
            }

            // Hotkey: K to repeat current entity
            if (Input.GetKeyDown(KeyCode.K))
            {
                mod.AnnounceEntityOnly();
            }

            // Hotkey: L or ] to cycle forwards
            if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                if (IsShiftHeld())
                {
                    mod.CycleNextCategory();
                }
                else
                {
                    mod.CycleNext();
                }
            }

            // Hotkey: P or \ to pathfind to current entity
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
            {
                if (IsShiftHeld())
                {
                    mod.TogglePathfindingFilter();
                }
                else
                {
                    mod.AnnounceCurrentEntity();
                }
            }
        }

        /// <summary>
        /// Handles global input (works everywhere).
        /// </summary>
        private void HandleGlobalInput()
        {
            // Check for status details navigation (takes priority when active)
            if (HandleStatusDetailsInput())
            {
                return; // Status navigation consumed the input
            }

            // Hotkey: Ctrl+Arrow to teleport
            if (IsCtrlHeld())
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, 16));
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, -16));
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    mod.TeleportInDirection(new Vector2(-16, 0));
                }
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    mod.TeleportInDirection(new Vector2(16, 0));
                }
            }

            // Hotkey: H to announce character health/status (battle only)
            if (Input.GetKeyDown(KeyCode.H) && FFII_ScreenReaderMod.IsInBattle)
            {
                mod.AnnounceCharacterStatus();
            }

            // Hotkey: G to announce current gil amount
            if (Input.GetKeyDown(KeyCode.G))
            {
                mod.AnnounceGilAmount();
            }

            // Hotkey: M to announce current map name
            if (Input.GetKeyDown(KeyCode.M))
            {
                if (IsShiftHeld())
                {
                    mod.ToggleMapExitFilter();
                }
                else
                {
                    mod.AnnounceCurrentMap();
                }
            }

            // Hotkey: 0 (Alpha0) or Shift+K to reset to All category
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                mod.ResetToAllCategory();
            }

            if (Input.GetKeyDown(KeyCode.K) && IsShiftHeld())
            {
                mod.ResetToAllCategory();
            }

            // Hotkey: = (Equals) to cycle to next category
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                mod.CycleNextCategory();
            }

            // Hotkey: - (Minus) to cycle to previous category
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                mod.CyclePreviousCategory();
            }

            // Hotkey: I to announce item details in shop
            if (Input.GetKeyDown(KeyCode.I))
            {
                if (ShopMenuTracker.ValidateState())
                {
                    ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                }
            }
        }

        private bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        /// <summary>
        /// Handles input for status details screen navigation.
        /// Returns true if input was consumed (status navigation is active and arrow was pressed).
        /// </summary>
        private bool HandleStatusDetailsInput()
        {
            var tracker = StatusNavigationTracker.Instance;

            // Check if status navigation is active
            if (!tracker.IsNavigationActive || !tracker.ValidateState())
            {
                return false;
            }

            // Handle arrow key navigation through stats
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (IsCtrlHeld())
                {
                    // Ctrl+Up: Jump to first stat
                    StatusNavigationReader.JumpToTop();
                }
                else if (IsShiftHeld())
                {
                    // Shift+Up: Jump to previous stat group
                    StatusNavigationReader.JumpToPreviousGroup();
                }
                else
                {
                    // Up: Navigate to previous stat
                    StatusNavigationReader.NavigatePrevious();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (IsCtrlHeld())
                {
                    // Ctrl+Down: Jump to last stat
                    StatusNavigationReader.JumpToBottom();
                }
                else if (IsShiftHeld())
                {
                    // Shift+Down: Jump to next stat group
                    StatusNavigationReader.JumpToNextGroup();
                }
                else
                {
                    // Down: Navigate to next stat
                    StatusNavigationReader.NavigateNext();
                }
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
    }
}
