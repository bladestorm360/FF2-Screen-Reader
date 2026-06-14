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
        private static MelonPreferences_Entry<int> prefWallBumpVolume;
        private static MelonPreferences_Entry<int> prefFootstepVolume;
        private static MelonPreferences_Entry<int> prefWallToneVolume;
        private static MelonPreferences_Entry<int> prefBeaconVolume;
        private static MelonPreferences_Entry<int> prefEnemyHPDisplay;

        // Volume properties (0-100, default 50)
        public static int WallBumpVolume => prefWallBumpVolume?.Value ?? 50;
        public static int FootstepVolume => prefFootstepVolume?.Value ?? 50;
        public static int WallToneVolume => prefWallToneVolume?.Value ?? 50;
        public static int BeaconVolume => prefBeaconVolume?.Value ?? 50;

        // Enemy HP display mode (0=Numbers, 1=Percentage, 2=Hidden)
        public static int EnemyHPDisplay => prefEnemyHPDisplay?.Value ?? 0;

        // Toggle states
        public static bool WallTonesEnabled => prefWallTones?.Value ?? false;
        public static bool FootstepsEnabled => prefFootsteps?.Value ?? false;
        public static bool AudioBeaconsEnabled => prefAudioBeacons?.Value ?? false;
        public static bool PathfindingFilterEnabled => prefPathfindingFilter?.Value ?? false;
        public static bool MapExitFilterEnabled => prefMapExitFilter?.Value ?? false;
        public static bool ToLayerFilterEnabled => prefToLayerFilter?.Value ?? false;
        public static bool StickClickNormalization => prefStickClickNormalization?.Value ?? false;
        public static bool AutoDetailEnabled => prefAutoDetail?.Value ?? false;
        public static bool AnnounceOnBeaconRestartEnabled => prefAnnounceOnBeaconRestart?.Value ?? false;

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
            prefAutoDetail = prefsCategory.CreateEntry<bool>("AutoDetail", false, "Auto Detail", "Announce descriptions/stats on focus for items, magic, equipment, and shops (off = terse; use I/U keys on demand)");
            prefAnnounceOnBeaconRestart = prefsCategory.CreateEntry<bool>("AnnounceOnBeaconRestart", false, "Beacon Destination Announcement", "Re-speak the current destination when the beacon is restarted");
            prefWallBumpVolume = prefsCategory.CreateEntry<int>("WallBumpVolume", 50, "Wall Bump Volume", "Volume for wall bump sounds (0-100)");
            prefFootstepVolume = prefsCategory.CreateEntry<int>("FootstepVolume", 50, "Footstep Volume", "Volume for footstep sounds (0-100)");
            prefWallToneVolume = prefsCategory.CreateEntry<int>("WallToneVolume", 50, "Wall Tone Volume", "Volume for wall proximity tones (0-100)");
            prefBeaconVolume = prefsCategory.CreateEntry<int>("BeaconVolume", 50, "Beacon Volume", "Volume for audio beacon pings (0-100)");
            prefEnemyHPDisplay = prefsCategory.CreateEntry<int>("EnemyHPDisplay", 0, "Enemy HP Display", "0=Numbers, 1=Percentage, 2=Hidden");
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

        internal static void SaveToggle(string prefName, bool value)
        {
            MelonPreferences_Entry<bool> pref = prefName switch
            {
                "WallTones" => prefWallTones,
                "Footsteps" => prefFootsteps,
                "AudioBeacons" => prefAudioBeacons,
                "PathfindingFilter" => prefPathfindingFilter,
                "MapExitFilter" => prefMapExitFilter,
                "ToLayerFilter" => prefToLayerFilter,
                "StickClickNormalization" => prefStickClickNormalization,
                "AutoDetail" => prefAutoDetail,
                "AnnounceOnBeaconRestart" => prefAnnounceOnBeaconRestart,
                _ => null
            };
            if (pref != null)
            {
                pref.Value = value;
                prefsCategory?.SaveToFile(false);
            }
        }
    }
}
