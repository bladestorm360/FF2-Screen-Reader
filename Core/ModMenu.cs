using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using FFII_ScreenReader.Patches;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;

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
            // Lambda so toggle descriptions can change with the current state.
            public Func<string> DescriptionGetter { get; protected set; } = () => "";
            public abstract string GetValueString();
            public abstract void Adjust(int delta);
            public abstract void Toggle();
        }

        private class ToggleItem : MenuItem
        {
            private readonly Func<bool> getter;
            private readonly Action toggle;

            public ToggleItem(string name, Func<bool> getter, Action toggle, Func<string> description = null)
            {
                Name = name;
                this.getter = getter;
                this.toggle = toggle;
                if (description != null) DescriptionGetter = description;
            }

            public override string GetValueString() => getter() ? T("On") : T("Off");
            public override void Adjust(int delta) => toggle();
            public override void Toggle() => toggle();
        }

        private class VolumeItem : MenuItem
        {
            private readonly Func<int> getter;
            private readonly Action<int> setter;

            public VolumeItem(string name, Func<int> getter, Action<int> setter, Func<string> description = null)
            {
                Name = name;
                this.getter = getter;
                this.setter = setter;
                if (description != null) DescriptionGetter = description;
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

            public EnumItem(string name, string[] options, Func<int> getter, Action<int> setter, Func<string> description = null)
            {
                Name = name;
                this.options = options;
                this.getter = getter;
                this.setter = setter;
                if (description != null) DescriptionGetter = description;
            }

            public override string GetValueString()
            {
                int index = getter();
                if (index >= 0 && index < options.Length)
                    return T(options[index]);
                return T("Unknown");
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

            public ActionItem(string name, Action action, Func<string> description = null)
            {
                Name = name;
                this.action = action;
                if (description != null) DescriptionGetter = description;
            }

            public override string GetValueString() => "";
            public override void Adjust(int delta) => action();
            public override void Toggle() => action();
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
                new SectionHeader(T("Audio Feedback")),
                new ToggleItem(T("Wall Tones"),
                    () => PreferencesManager.WallTonesEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleWallTones(),
                    () => PreferencesManager.WallTonesEnabled
                        ? T("On. Directional tones play as you approach walls.")
                        : T("Off. No directional wall feedback.")),
                new ToggleItem(T("Footsteps"),
                    () => PreferencesManager.FootstepsEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleFootsteps(),
                    () => PreferencesManager.FootstepsEnabled
                        ? T("On. A click plays for each tile of player movement.")
                        : T("Off. No per-tile movement sound.")),
                new ToggleItem(T("Audio Beacons"),
                    () => PreferencesManager.AudioBeaconsEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleAudioBeacons(),
                    () => PreferencesManager.AudioBeaconsEnabled
                        ? T("On. Audio beacon is the primary navigation aid; turn-by-turn pathfinding is disabled.")
                        : T("Off. Turn-by-turn pathfinding is used for navigation.")),

                // Volume Controls section
                new SectionHeader(T("Volume Controls")),
                new VolumeItem(T("Wall Bump Volume"),
                    () => PreferencesManager.WallBumpVolume,
                    PreferencesManager.SetWallBumpVolume,
                    () => T("Volume of wall bump sound effects, zero to one hundred percent.")),
                new VolumeItem(T("Footstep Volume"),
                    () => PreferencesManager.FootstepVolume,
                    PreferencesManager.SetFootstepVolume,
                    () => T("Volume of footstep sounds, zero to one hundred percent.")),
                new VolumeItem(T("Wall Tone Volume"),
                    () => PreferencesManager.WallToneVolume,
                    PreferencesManager.SetWallToneVolume,
                    () => T("Volume of directional wall proximity tones, zero to one hundred percent.")),
                new VolumeItem(T("Beacon Volume"),
                    () => PreferencesManager.BeaconVolume,
                    PreferencesManager.SetBeaconVolume,
                    () => T("Volume of audio beacon pings, zero to one hundred percent.")),

                // Navigation Filters section
                new SectionHeader(T("Navigation Filters")),
                new ToggleItem(T("Pathfinding Filter"),
                    () => PreferencesManager.PathfindingFilterEnabled,
                    () => FFII_ScreenReaderMod.Instance?.TogglePathfindingFilter(),
                    () => PreferencesManager.PathfindingFilterEnabled
                        ? T("On. Entity cycling shows only entities reachable via pathfinding.")
                        : T("Off. All entities appear when cycling, including unreachable ones.")),
                new ToggleItem(T("Map Exit Filter"),
                    () => PreferencesManager.MapExitFilterEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleMapExitFilter(),
                    () => PreferencesManager.MapExitFilterEnabled
                        ? T("On. Multiple exits leading to the same destination collapse to the closest one.")
                        : T("Off. All map exits appear in navigation.")),
                new ToggleItem(T("Layer Transition Filter"),
                    () => PreferencesManager.ToLayerFilterEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleToLayerFilter(),
                    () => PreferencesManager.ToLayerFilterEnabled
                        ? T("On. Layer transition entities are hidden from navigation.")
                        : T("Off. Layer transition entities appear in navigation.")),
                new ToggleItem(T("Stick Click Normalization"),
                    () => PreferencesManager.StickClickNormalization,
                    ToggleStickClickNormalization,
                    () => PreferencesManager.StickClickNormalization
                        ? T("On. L3 and R3 pass through to the game (auto-dash and encounter toggle). Mod functions move to mod mode.")
                        : T("Off. L3 toggles beacon navigation; R3 toggles pathfinding filter; game cannot see them.")),

                // Battle Settings section
                new SectionHeader(T("Battle Settings")),
                new EnumItem(T("Enemy HP Display"),
                    new[] { "Numbers", "Percentage", "Hidden" },
                    () => PreferencesManager.EnemyHPDisplay,
                    PreferencesManager.SetEnemyHPDisplay,
                    () => T("Controls how enemy HP appears in battle: numeric value, percentage of max, or hidden.")),
                new EnumItem(T("Multi-hit Damage"),
                    new[] { "Total only", "With hit count" },
                    () => PreferencesManager.DamageDisplay,
                    PreferencesManager.SetDamageDisplay,
                    () => T("On multi-hit physical attacks, optionally prepend the number of hits, e.g. '14x1552 damage'.")),

                // Announcements section
                new SectionHeader(T("Announcements")),
                new ToggleItem(T("Auto Detail"),
                    () => PreferencesManager.AutoDetailEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleAutoDetail(),
                    () => PreferencesManager.AutoDetailEnabled
                        ? T("On. Descriptions and stats announce automatically on focus for items, magic, equipment, and shops.")
                        : T("Off. Use the I key to read descriptions on demand.")),
                new ToggleItem(T("Beacon Destination Announcement"),
                    () => FFII_ScreenReaderMod.AnnounceOnBeaconRestartEnabled,
                    () => FFII_ScreenReaderMod.Instance?.ToggleAnnounceOnBeaconRestart(),
                    () => FFII_ScreenReaderMod.AnnounceOnBeaconRestartEnabled
                        ? T("On. Restarting the beacon also re-speaks the current destination.")
                        : T("Off. Restarting the beacon only re-pings, without speaking.")),
                new ToggleItem(T("Menu Position Announcements"),
                    () => PreferencesManager.MenuPositionAnnouncementsEnabled,
                    ToggleMenuPositionAnnouncements,
                    () => PreferencesManager.MenuPositionAnnouncementsEnabled
                        ? T("On. List entries announce their position, for example, 3 of 12.")
                        : T("Off. List entries do not announce position.")),

                // Close Menu action
                new ActionItem(T("Close Menu"), Close,
                    () => T("Closes the mod menu and returns to the game."))
            };

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

            // Announce that the menu opened (both F8 and the controller Start button reach here),
            // then the first item after a short delay. Game input suppressed via
            // ControllerRouter.SuppressGameInput + InputSystemManager patches (no window stealing).
            FFII_ScreenReaderMod.SpeakText(T("Mod menu"), interrupt: true);
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
            // Announce on every close path (keyboard Escape/F8, "Close Menu" item, controller B/Start).
            // Game input restored automatically — ControllerRouter.SuppressGameInput becomes false.
            FFII_ScreenReaderMod.SpeakText(T("Mod menu closed"), interrupt: true);
        }

        /// <summary>
        /// Handles keyboard input when the mod menu is open. Reads keys via GamepadManager
        /// (GetAsyncKeyState — hardware state); game input suppressed by InputSystemManager
        /// patches + Input.ResetInputAxes while open. No window focus stealing.
        /// Returns true if input was consumed (menu is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;
            if (items == null || items.Count == 0) return false;

            // Escape or F8 to close
            if (GamepadManager.IsKeyCodePressed(KeyCode.Escape) || GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                Close();
                return true;
            }

            // Up arrow - navigate to previous item
            if (GamepadManager.IsKeyCodePressed(KeyCode.UpArrow))
            {
                NavigatePrevious();
                return true;
            }

            // Down arrow - navigate to next item
            if (GamepadManager.IsKeyCodePressed(KeyCode.DownArrow))
            {
                NavigateNext();
                return true;
            }

            // Left arrow - decrease value
            if (GamepadManager.IsKeyCodePressed(KeyCode.LeftArrow))
            {
                AdjustCurrentItem(-1);
                return true;
            }

            // Right arrow - increase value
            if (GamepadManager.IsKeyCodePressed(KeyCode.RightArrow))
            {
                AdjustCurrentItem(1);
                return true;
            }

            // Enter or Space - toggle/activate
            if (GamepadManager.IsKeyCodePressed(KeyCode.Return) || GamepadManager.IsKeyCodePressed(KeyCode.Space))
            {
                ToggleCurrentItem();
                return true;
            }

            // I - read the context description for the current item
            if (GamepadManager.IsKeyCodePressed(KeyCode.I))
            {
                AnnounceCurrentItemDescription();
                return true;
            }

            return true; // Consume all input while menu is open
        }

        private static void AnnounceCurrentItemDescription()
        {
            if (items == null || currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            string desc = item.DescriptionGetter?.Invoke();
            FFII_ScreenReaderMod.SpeakText(
                string.IsNullOrWhiteSpace(desc) ? T("No description") : desc,
                interrupt: true);
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

        /// <summary>
        /// Toggles the StickClickNormalization preference and announces the new state.
        /// </summary>
        private static void ToggleStickClickNormalization()
        {
            bool newValue = !PreferencesManager.StickClickNormalization;
            PreferencesManager.SaveStickClickNormalization(newValue);
        }

        /// <summary>
        /// Toggles the MenuPositionAnnouncements preference. The re-announce of the current item
        /// (AnnounceCurrentItem) provides the audible feedback, so no separate speech here.
        /// </summary>
        private static void ToggleMenuPositionAnnouncements()
        {
            bool newValue = !PreferencesManager.MenuPositionAnnouncementsEnabled;
            PreferencesManager.SaveMenuPositionAnnouncements(newValue);
        }

        // --- Controller-accessible navigation hooks ---
        // The private methods above are tied to keyboard polling; these expose
        // the same operations so ControllerRouter can drive the menu via gamepad.
        public static void ControllerNavigatePrevious() => NavigatePrevious();
        public static void ControllerNavigateNext() => NavigateNext();
        public static void ControllerAdjustCurrentItem(int delta) => AdjustCurrentItem(delta);
        public static void ControllerToggleCurrentItem() => ToggleCurrentItem();

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

            var (index, count) = NavigablePosition();
            announcement = MenuPosition.Format(announcement, index, count);

            FFII_ScreenReaderMod.SpeakText(announcement, interrupt: interrupt);
        }

        /// <summary>
        /// Position of the current item among the navigable (non-header) items. Section headers are
        /// silently skipped during navigation, so the user hears "(N of total settings)" — not counting
        /// the invisible headers.
        /// </summary>
        private static (int index, int count) NavigablePosition()
        {
            int count = 0, index = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is SectionHeader) continue;
                if (i == currentIndex) index = count;
                count++;
            }
            return (index, count);
        }
    }
}
