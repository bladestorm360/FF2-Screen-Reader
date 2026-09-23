using System;
using MelonLoader;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Manages all mod preferences (toggles, volumes, display modes).
    /// Extracted from FFII_ScreenReaderMod to reduce file size.
    /// </summary>
    public static class PreferencesManager
    {
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;
        private static MelonPreferences_Entry<bool> prefToLayerFilter;
        private static MelonPreferences_Entry<bool> prefWallTones;
        private static MelonPreferences_Entry<bool> prefFootsteps;
        private static MelonPreferences_Entry<bool> prefAudioBeacons;
        private static MelonPreferences_Entry<bool> prefStickClickNormalization;
        private static MelonPreferences_Entry<bool> prefAutoDetail;
        private static MelonPreferences_Entry<bool> prefAnnounceOnBeaconRestart;
        private static MelonPreferences_Entry<bool> prefMenuPositionAnnouncements;
        private static MelonPreferences_Entry<int> prefWallBumpVolume;
        private static MelonPreferences_Entry<int> prefFootstepVolume;
        private static MelonPreferences_Entry<int> prefWallToneVolume;
        private static MelonPreferences_Entry<int> prefBeaconVolume;
        private static MelonPreferences_Entry<int> prefEnemyHPDisplay;
        private static MelonPreferences_Entry<int> prefDamageDisplay;

        // Volume properties (0-100, default 50)
        public static int WallBumpVolume => prefWallBumpVolume?.Value ?? 50;
        public static int FootstepVolume => prefFootstepVolume?.Value ?? 50;
        public static int WallToneVolume => prefWallToneVolume?.Value ?? 50;
        public static int BeaconVolume => prefBeaconVolume?.Value ?? 50;

        // Enemy HP display mode (0=Numbers, 1=Percentage, 2=Hidden)
        public static int EnemyHPDisplay => prefEnemyHPDisplay?.Value ?? 0;

        // Multi-hit damage display (0=Total only, 1=With hit count "14x1552 damage")
        public static int DamageDisplay => prefDamageDisplay?.Value ?? 1;

        // Toggle states
        public static bool WallTonesEnabled => prefWallTones?.Value ?? false;
        public static bool FootstepsEnabled => prefFootsteps?.Value ?? false;
        public static bool AudioBeaconsEnabled => prefAudioBeacons?.Value ?? false;
        public static bool PathfindingFilterEnabled => prefPathfindingFilter?.Value ?? false;
        public static bool MapExitFilterEnabled => prefMapExitFilter?.Value ?? false;
        public static bool ToLayerFilterEnabled => prefToLayerFilter?.Value ?? false;
        public static bool StickClickNormalization => prefStickClickNormalization?.Value ?? false;
        public static bool AutoDetailEnabled => prefAutoDetail?.Value ?? true;
        public static bool AnnounceOnBeaconRestartEnabled => prefAnnounceOnBeaconRestart?.Value ?? false;
        public static bool MenuPositionAnnouncementsEnabled => prefMenuPositionAnnouncements?.Value ?? true;

        public static void Initialize()
        {
            prefsCategory = MelonPreferences.CreateCategory("FFII_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination");
            prefToLayerFilter = prefsCategory.CreateEntry<bool>("ToLayerFilter", false, "Layer Transition Filter", "Hide layer transition entities from navigation list");
            prefWallTones = prefsCategory.CreateEntry<bool>("WallTones", false, "Wall Tones", "Play directional tones when approaching walls");
            prefFootsteps = prefsCategory.CreateEntry<bool>("Footsteps", false, "Footsteps", "Play click sound on each tile movement");
            prefAudioBeacons = prefsCategory.CreateEntry<bool>("AudioBeacons", false, "Audio Beacons", "Play ping toward selected entity");
            prefStickClickNormalization = prefsCategory.CreateEntry<bool>("StickClickNormalization", false, "Stick Click Normalization", "When enabled, L3/R3 fall through to game; mod toggles move to mod mode");
            // Stored as "AutoDetailOnFocus", not "AutoDetail": Auto Detail now gates the battle and list
            // descriptions that used to be spoken unconditionally, and MelonPreferences had already
            // written the old off-by-default "AutoDetail" into every install. A new entry gives
            // everyone FF1's default (on) once; turning it off afterwards sticks as usual.
            prefAutoDetail = prefsCategory.CreateEntry<bool>("AutoDetailOnFocus", true, "Auto Detail", "Announce descriptions/stats on focus for items, magic, equipment, and shops (off = terse; use the I key on demand)");
            prefAnnounceOnBeaconRestart = prefsCategory.CreateEntry<bool>("AnnounceOnBeaconRestart", false, "Beacon Destination Announcement", "Re-speak the current destination when the beacon is restarted");
            prefMenuPositionAnnouncements = prefsCategory.CreateEntry<bool>("MenuPositionAnnouncements", true, "Menu Position Announcements", "Append position (N of M) to list-item announcements in menus");
            prefWallBumpVolume = prefsCategory.CreateEntry<int>("WallBumpVolume", 50, "Wall Bump Volume", "Volume for wall bump sounds (0-100)");
            prefFootstepVolume = prefsCategory.CreateEntry<int>("FootstepVolume", 50, "Footstep Volume", "Volume for footstep sounds (0-100)");
            prefWallToneVolume = prefsCategory.CreateEntry<int>("WallToneVolume", 50, "Wall Tone Volume", "Volume for wall proximity tones (0-100)");
            prefBeaconVolume = prefsCategory.CreateEntry<int>("BeaconVolume", 50, "Beacon Volume", "Volume for audio beacon pings (0-100)");
            prefEnemyHPDisplay = prefsCategory.CreateEntry<int>("EnemyHPDisplay", 0, "Enemy HP Display", "0=Numbers, 1=Percentage, 2=Hidden");
            // Stored as "MultiHitDamage" (default: with hit count) rather than the old off-by-default
            // "DamageDisplay", which MelonPreferences had already written into every install, so the
            // hit count is announced once after updating; choosing "Total only" afterwards sticks.
            prefDamageDisplay = prefsCategory.CreateEntry<int>("MultiHitDamage", 1, "Multi-hit Damage", "0=Total only, 1=With hit count (e.g. 14x1552 damage)");
        }

        private static void SetIntPreference(MelonPreferences_Entry<int> pref, int value, int min, int max)
        {
            if (pref != null)
            {
                pref.Value = Math.Clamp(value, min, max);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetWallBumpVolume(int value) => SetIntPreference(prefWallBumpVolume, value, 0, 100);
        public static void SetFootstepVolume(int value) => SetIntPreference(prefFootstepVolume, value, 0, 100);
        public static void SetWallToneVolume(int value) => SetIntPreference(prefWallToneVolume, value, 0, 100);
        public static void SetBeaconVolume(int value) => SetIntPreference(prefBeaconVolume, value, 0, 100);
        public static void SetEnemyHPDisplay(int value) => SetIntPreference(prefEnemyHPDisplay, value, 0, 2);
        public static void SetDamageDisplay(int value) => SetIntPreference(prefDamageDisplay, value, 0, 1);

        private static void SetBoolPreference(MelonPreferences_Entry<bool> pref, bool value)
        {
            if (pref != null)
            {
                pref.Value = value;
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SaveWallTones(bool value) => SetBoolPreference(prefWallTones, value);
        public static void SaveFootsteps(bool value) => SetBoolPreference(prefFootsteps, value);
        public static void SaveAudioBeacons(bool value) => SetBoolPreference(prefAudioBeacons, value);
        public static void SavePathfindingFilter(bool value) => SetBoolPreference(prefPathfindingFilter, value);
        public static void SaveMapExitFilter(bool value) => SetBoolPreference(prefMapExitFilter, value);
        public static void SaveToLayerFilter(bool value) => SetBoolPreference(prefToLayerFilter, value);
        public static void SaveStickClickNormalization(bool value) => SetBoolPreference(prefStickClickNormalization, value);
        // FF2-specific preferences (no FF3 equivalent)
        public static void SaveAutoDetail(bool value) => SetBoolPreference(prefAutoDetail, value);
        public static void SaveAnnounceOnBeaconRestart(bool value) => SetBoolPreference(prefAnnounceOnBeaconRestart, value);
        public static void SaveMenuPositionAnnouncements(bool value) => SetBoolPreference(prefMenuPositionAnnouncements, value);
    }
}
