using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using MelonLoader;
using FFII_ScreenReader.Patches;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Audio-only virtual menu for adjusting screen reader settings.
    /// Accessible via F8 key. No Unity UI overlay - purely navigational state + announcements.
    /// </summary>
    public static class ModMenu
    {
        /// <summary>
        /// Whether the mod menu is currently open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static int currentIndex = 0;
        private static List<MenuItem> items;

        #region Menu Item Types

        private abstract class MenuItem
        {
            public string Name { get; protected set; }
            public abstract string GetValueString();
            public abstract void Adjust(int delta);
            public abstract void Toggle();
        }

        private class ToggleItem : MenuItem
        {
            private readonly Func<bool> getter;
            private readonly Action toggle;

            public ToggleItem(string name, Func<bool> getter, Action toggle)
            {
                Name = name;
                this.getter = getter;
                this.toggle = toggle;
            }

            public override string GetValueString() => getter() ? "On" : "Off";
            public override void Adjust(int delta) => toggle();
            public override void Toggle() => toggle();
        }

        private class VolumeItem : MenuItem
        {
            private readonly Func<int> getter;
            private readonly Action<int> setter;

            public VolumeItem(string name, Func<int> getter, Action<int> setter)
            {
                Name = name;
                this.getter = getter;
                this.setter = setter;
            }

            public override string GetValueString() => $"{getter()}%";

            public override void Adjust(int delta)
            {
                int current = getter();
                int newValue = Math.Clamp(current + (delta * 5), 0, 100);
                setter(newValue);
            }

            public override void Toggle()
            {
                // Toggle between 0 and 50 for quick mute/unmute
                int current = getter();
                setter(current == 0 ? 50 : 0);
            }
        }

        private class EnumItem : MenuItem
        {
            private readonly string[] options;
            private readonly Func<int> getter;
            private readonly Action<int> setter;

            public EnumItem(string name, string[] options, Func<int> getter, Action<int> setter)
            {
                Name = name;
                this.options = options;
                this.getter = getter;
                this.setter = setter;
            }

            public override string GetValueString()
            {
                int index = getter();
                if (index >= 0 && index < options.Length)
                    return options[index];
                return "Unknown";
            }

            public override void Adjust(int delta)
            {
                int current = getter();
                int newValue = current + delta;
                if (newValue < 0) newValue = options.Length - 1;
                if (newValue >= options.Length) newValue = 0;
                setter(newValue);
            }

            public override void Toggle() => Adjust(1);
        }

        private class SectionHeader : MenuItem
        {
            public SectionHeader(string name)
            {
                Name = name;
            }

            public override string GetValueString() => "";
            public override void Adjust(int delta) { }
            public override void Toggle() { }
        }

        private class ActionItem : MenuItem
        {
            private readonly Action action;

            public ActionItem(string name, Action action)
            {
                Name = name;
                this.action = action;
            }

            public override string GetValueString() => "";
            public override void Adjust(int delta) => action();
            public override void Toggle() => action();
        }

        #endregion

        #region Windows API for Focus Control and Input

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // Virtual key codes for navigation
        private const int VK_ESCAPE = 0x1B;
        private const int VK_F8 = 0x77;
        private const int VK_UP = 0x26;
        private const int VK_DOWN = 0x28;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;
        private const int VK_RETURN = 0x0D;
        private const int VK_SPACE = 0x20;

        // Track previous key states to detect key-down transitions
        private static Dictionary<int, bool> previousKeyStates = new Dictionary<int, bool>();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint WS_EX_TOOLWINDOW = 0x00000080;  // Hidden from taskbar
        private const uint WS_POPUP = 0x80000000;
        private const int SW_SHOW = 5;

        private static IntPtr gameWindowHandle = IntPtr.Zero;
        private static IntPtr focusBlockerHandle = IntPtr.Zero;

        /// <summary>
        /// Initializes key states for all tracked keys based on current pressed state.
        /// Called when menu opens to prevent keys that opened the menu from triggering actions.
        /// </summary>
        private static void InitializeKeyStates()
        {
            previousKeyStates.Clear();
            int[] trackedKeys = { VK_ESCAPE, VK_F8, VK_UP, VK_DOWN, VK_LEFT, VK_RIGHT, VK_RETURN, VK_SPACE };
            foreach (int vKey in trackedKeys)
            {
                bool isPressed = (GetAsyncKeyState(vKey) & 0x8000) != 0;
                previousKeyStates[vKey] = isPressed;
            }
        }

        /// <summary>
        /// Checks if a key was just pressed this frame using Windows API.
        /// Works even when game window doesn't have focus.
        /// </summary>
        private static bool IsKeyDown(int vKey)
        {
            // GetAsyncKeyState returns short where high bit = currently pressed
            bool currentlyPressed = (GetAsyncKeyState(vKey) & 0x8000) != 0;

            // Get previous state (default false if not tracked)
            previousKeyStates.TryGetValue(vKey, out bool wasPressed);

            // Update state for next frame
            previousKeyStates[vKey] = currentlyPressed;

            // Key down = pressed now but wasn't pressed before
            return currentlyPressed && !wasPressed;
        }

        #endregion

        /// <summary>
        /// Initializes the mod menu with all menu items.
        /// Call this once during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            items = new List<MenuItem>
            {
                // Audio Feedback section
                new SectionHeader("Audio Feedback"),
                new ToggleItem("Wall Tones",
                    () => FFII_ScreenReaderMod.WallTonesEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleWallTones()),
                new ToggleItem("Footsteps",
                    () => FFII_ScreenReaderMod.FootstepsEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleFootsteps()),
                new ToggleItem("Audio Beacons",
                    () => FFII_ScreenReaderMod.AudioBeaconsEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleAudioBeacons()),

                // Volume Controls section
                new SectionHeader("Volume Controls"),
                new VolumeItem("Wall Bump Volume",
                    () => FFII_ScreenReaderMod.WallBumpVolume,
                    FFII_ScreenReaderMod.SetWallBumpVolume),
                new VolumeItem("Footstep Volume",
                    () => FFII_ScreenReaderMod.FootstepVolume,
                    FFII_ScreenReaderMod.SetFootstepVolume),
                new VolumeItem("Wall Tone Volume",
                    () => FFII_ScreenReaderMod.WallToneVolume,
                    FFII_ScreenReaderMod.SetWallToneVolume),
                new VolumeItem("Beacon Volume",
                    () => FFII_ScreenReaderMod.BeaconVolume,
                    FFII_ScreenReaderMod.SetBeaconVolume),

                // Navigation Filters section
                new SectionHeader("Navigation Filters"),
                new ToggleItem("Pathfinding Filter",
                    () => FFII_ScreenReaderMod.PathfindingFilterEnabled,
                    () => FFII_ScreenReaderMod.Instance?.TogglePathfindingFilter()),
                new ToggleItem("Map Exit Filter",
                    () => FFII_ScreenReaderMod.MapExitFilterEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleMapExitFilter()),

                // Battle Settings section
                new SectionHeader("Battle Settings"),
                new EnumItem("Enemy HP Display",
                    new[] { "Numbers", "Percentage", "Hidden" },
                    () => FFII_ScreenReaderMod.EnemyHPDisplay,
                    FFII_ScreenReaderMod.SetEnemyHPDisplay),

                // Close Menu action
                new ActionItem("Close Menu", Close)
            };

            MelonLogger.Msg("[ModMenu] Initialized with " + items.Count + " items");
        }

        /// <summary>
        /// Opens the mod menu.
        /// </summary>
        public static void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            currentIndex = 0;

            // Skip section header at index 0
            if (items != null && items.Count > 1 && items[0] is SectionHeader)
                currentIndex = 1;

            // Initialize key states to current pressed state to prevent keys that opened the menu from triggering actions
            InitializeKeyStates();

            SetGameInputEnabled(false);

            // Window title change announces "FFII_ModMenu" via screen reader focus
            // Just announce the first item after a short delay
            CoroutineManager.StartManaged(AnnounceFirstItemDelayed());
        }

        private static IEnumerator AnnounceFirstItemDelayed()
        {
            // Wait 2 frames for TTS to queue "Mod menu" before adding first item
            yield return null;
            yield return null;

            if (IsOpen) // Still open after delay
            {
                AnnounceCurrentItem(interrupt: false);
            }
        }

        /// <summary>
        /// Closes the mod menu.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            SetGameInputEnabled(true);
            // Focus returns to game window, screen reader announces the focus change
        }

        /// <summary>
        /// Handles input when the mod menu is open.
        /// Uses Windows GetAsyncKeyState API for input detection, which works
        /// even when the game window doesn't have focus.
        /// Returns true if input was consumed (menu is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;
            if (items == null || items.Count == 0) return false;

            // Escape or F8 to close
            if (IsKeyDown(VK_ESCAPE) || IsKeyDown(VK_F8))
            {
                Close();
                return true;
            }

            // Up arrow - navigate to previous item
            if (IsKeyDown(VK_UP))
            {
                NavigatePrevious();
                return true;
            }

            // Down arrow - navigate to next item
            if (IsKeyDown(VK_DOWN))
            {
                NavigateNext();
                return true;
            }

            // Left arrow - decrease value
            if (IsKeyDown(VK_LEFT))
            {
                AdjustCurrentItem(-1);
                return true;
            }

            // Right arrow - increase value
            if (IsKeyDown(VK_RIGHT))
            {
                AdjustCurrentItem(1);
                return true;
            }

            // Enter or Space - toggle/activate
            if (IsKeyDown(VK_RETURN) || IsKeyDown(VK_SPACE))
            {
                ToggleCurrentItem();
                return true;
            }

            return true; // Consume all input while menu is open
        }

        private static void NavigateNext()
        {
            int startIndex = currentIndex;
            do
            {
                currentIndex++;
                if (currentIndex >= items.Count)
                    currentIndex = 0;

                // Skip section headers
                if (!(items[currentIndex] is SectionHeader))
                    break;

            } while (currentIndex != startIndex);

            AnnounceCurrentItem();
        }

        private static void NavigatePrevious()
        {
            int startIndex = currentIndex;
            do
            {
                currentIndex--;
                if (currentIndex < 0)
                    currentIndex = items.Count - 1;

                // Skip section headers
                if (!(items[currentIndex] is SectionHeader))
                    break;

            } while (currentIndex != startIndex);

            AnnounceCurrentItem();
        }

        private static void AdjustCurrentItem(int delta)
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            if (item is SectionHeader) return;

            item.Adjust(delta);
            AnnounceCurrentItem();
        }

        private static void ToggleCurrentItem()
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            if (item is SectionHeader) return;

            item.Toggle();

            // For action items (like Close Menu), don't re-announce
            if (item is ActionItem) return;

            AnnounceCurrentItem();
        }

        private static void AnnounceCurrentItem(bool interrupt = true)
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            string value = item.GetValueString();

            string announcement;
            if (string.IsNullOrEmpty(value))
            {
                announcement = item.Name;
            }
            else
            {
                announcement = $"{item.Name}: {value}";
            }

            FFII_ScreenReaderMod.SpeakText(announcement, interrupt: interrupt);
        }

        private static void SetGameInputEnabled(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    // Save the game window handle before stealing focus
                    gameWindowHandle = GetForegroundWindow();
                    MelonLogger.Msg($"[ModMenu] Saved game window: 0x{gameWindowHandle:X}");

                    // Create an invisible window to steal focus
                    // Using "Static" class - always available in Windows
                    focusBlockerHandle = CreateWindowEx(
                        WS_EX_TOOLWINDOW,  // Hidden from taskbar
                        "Static",          // Built-in window class
                        "FFII_ModMenu",    // Window name (invisible)
                        WS_POPUP,          // No border, no title
                        0, 0, 1, 1,        // 1x1 pixel at origin
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                    if (focusBlockerHandle != IntPtr.Zero)
                    {
                        ShowWindow(focusBlockerHandle, SW_SHOW);
                        SetForegroundWindow(focusBlockerHandle);
                        MelonLogger.Msg($"[ModMenu] Focus blocker created: 0x{focusBlockerHandle:X}");
                    }
                    else
                    {
                        MelonLogger.Warning("[ModMenu] Failed to create focus blocker window");
                    }
                }
                else
                {
                    // Destroy the focus blocker
                    if (focusBlockerHandle != IntPtr.Zero)
                    {
                        DestroyWindow(focusBlockerHandle);
                        MelonLogger.Msg("[ModMenu] Focus blocker destroyed");
                        focusBlockerHandle = IntPtr.Zero;
                    }

                    // Return focus to the game
                    if (gameWindowHandle != IntPtr.Zero)
                    {
                        SetForegroundWindow(gameWindowHandle);
                        MelonLogger.Msg($"[ModMenu] Focus returned to game: 0x{gameWindowHandle:X}");
                        gameWindowHandle = IntPtr.Zero;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ModMenu] Error in focus control: {ex.Message}");
            }
        }
    }
}
