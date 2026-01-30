using MelonLoader;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Patches;
using FFII_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;
using FieldMap = Il2Cpp.FieldMap;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using FieldMapProvisionInformation = Il2CppLast.Map.FieldMapProvisionInformation;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

// Menu controller types for transition patches
using KeyInputItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using KeyInputEquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using KeyInputStatusWindowController = Il2CppLast.UI.KeyInput.StatusWindowController;
using KeyInputAbilityWindowController = Il2CppSerial.FF2.UI.KeyInput.AbilityWindowController;
using KeyInputConfigController = Il2CppLast.UI.KeyInput.ConfigController;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;
using KeyInputSecretWordController = Il2CppLast.UI.KeyInput.SecretWordController;
using KeyInputWordsWindowController = Il2CppLast.UI.KeyInput.WordsWindowController;
using CommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;

[assembly: MelonInfo(typeof(FFII_ScreenReader.Core.FFII_ScreenReaderMod), "FFII Screen Reader", "1.0.0", "Author")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY II")]

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Entity category for filtering navigation targets
    /// </summary>
    public enum EntityCategory
    {
        All = 0,
        Chests = 1,
        NPCs = 2,
        MapExits = 3,
        Events = 4,
        Vehicles = 5
    }

    /// <summary>
    /// Main mod class for FFII Screen Reader.
    /// Provides screen reader accessibility support for Final Fantasy II Pixel Remaster.
    /// </summary>
    public class FFII_ScreenReaderMod : MelonMod
    {
        private static TolkWrapper tolk;
        private InputManager inputManager;
        private EntityScanner entityScanner;

        /// <summary>
        /// Singleton instance for access from patches.
        /// </summary>
        public static FFII_ScreenReaderMod Instance { get; private set; }

        private static readonly int CategoryCount = Enum.GetValues(typeof(EntityCategory)).Length;
        private EntityCategory currentCategory = EntityCategory.All;

        // Filter toggles
        private bool filterByPathfinding = false;
        private bool filterMapExits = false;

        // Audio feedback toggles
        private bool enableWallTones = false;
        private bool enableFootsteps = false;
        private bool enableAudioBeacons = false;

        // Coroutine-based audio loops (replace per-frame polling)
        private IEnumerator wallToneCoroutine = null;
        private IEnumerator beaconCoroutine = null;
        private const float BEACON_INTERVAL = 2.0f;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;

        // Map transition suppression for wall tones
        private int wallToneMapId = -1;
        private float wallToneSuppressedUntil = 0f;

        // Reusable direction list buffer to avoid per-cycle allocations
        private static readonly List<SoundPlayer.Direction> wallDirectionsBuffer = new List<SoundPlayer.Direction>(4);

        // Preferences
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;
        private static MelonPreferences_Entry<bool> prefWallTones;
        private static MelonPreferences_Entry<bool> prefFootsteps;
        private static MelonPreferences_Entry<bool> prefAudioBeacons;

        // Volume controls (0-100, default 50 = current volume)
        private static MelonPreferences_Entry<int> prefWallBumpVolume;
        private static MelonPreferences_Entry<int> prefFootstepVolume;
        private static MelonPreferences_Entry<int> prefWallToneVolume;
        private static MelonPreferences_Entry<int> prefBeaconVolume;

        // Enemy HP display mode (0=Numbers, 1=Percentage, 2=Hidden)
        private static MelonPreferences_Entry<int> prefEnemyHPDisplay;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("FFII Screen Reader Mod loaded!");

            // Subscribe to scene load events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFII_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination");
            prefWallTones = prefsCategory.CreateEntry<bool>("WallTones", false, "Wall Tones", "Play directional tones when approaching walls");
            prefFootsteps = prefsCategory.CreateEntry<bool>("Footsteps", false, "Footsteps", "Play click sound on each tile movement");
            prefAudioBeacons = prefsCategory.CreateEntry<bool>("AudioBeacons", false, "Audio Beacons", "Play ping toward selected entity");

            // Volume controls (0-100, default 50)
            prefWallBumpVolume = prefsCategory.CreateEntry<int>("WallBumpVolume", 50, "Wall Bump Volume", "Volume for wall bump sounds (0-100)");
            prefFootstepVolume = prefsCategory.CreateEntry<int>("FootstepVolume", 50, "Footstep Volume", "Volume for footstep sounds (0-100)");
            prefWallToneVolume = prefsCategory.CreateEntry<int>("WallToneVolume", 50, "Wall Tone Volume", "Volume for wall proximity tones (0-100)");
            prefBeaconVolume = prefsCategory.CreateEntry<int>("BeaconVolume", 50, "Beacon Volume", "Volume for audio beacon pings (0-100)");

            // Enemy HP display mode
            prefEnemyHPDisplay = prefsCategory.CreateEntry<int>("EnemyHPDisplay", 0, "Enemy HP Display", "0=Numbers, 1=Percentage, 2=Hidden");

            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;
            enableWallTones = prefWallTones.Value;
            enableFootsteps = prefFootsteps.Value;
            enableAudioBeacons = prefAudioBeacons.Value;

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize external sound player for distinct audio feedback
            SoundPlayer.Initialize();

            // Initialize entity name translator (Japanese → English)
            EntityTranslator.Initialize();

            // Initialize input manager with event-driven input handling
            inputManager = new InputManager(this);
            inputManager.Initialize();

            // Initialize entity scanner
            entityScanner = new EntityScanner();

            // Apply Harmony patches
            TryManualPatching();

            // Initialize mod menu
            ModMenu.Initialize();

            // NOTE: Audio loops (wall tones, beacons) are NOT started here.
            // They start from DelayedInitialScan() after scene loads, or from user toggle.
            // Starting them before scene load causes lag (null checks run repeatedly).
        }

        /// <summary>
        /// Attempts to manually apply Harmony patches with detailed error logging.
        /// </summary>
        private void TryManualPatching()
        {
            LoggerInstance.Msg("Attempting manual Harmony patching...");

            var harmony = new HarmonyLib.Harmony("com.ffii.screenreader.manual");

            // Patch cursor navigation methods (menus and battle)
            TryPatchCursorNavigation(harmony);

            // Patch dialogue methods via MessageWindowManager
            MessageWindowPatches.ApplyPatches(harmony);

            // Patch scroll/fade messages for intro text
            ScrollMessagePatches.ApplyPatches(harmony);

            // Patch new game character naming screen
            NewGameNamingPatches.ApplyPatches(harmony);

            // Patch battle system
            BattleCommandPatches.ApplyPatches(harmony);
            BattleMessagePatches.ApplyPatches(harmony);
            BattleResultPatches.ApplyPatches(harmony);

            // Patch equipment menu
            EquipMenuPatches.ApplyPatches(harmony);

            // Patch item menu
            ItemMenuPatches.ApplyPatches(harmony);

            // Patch status menu
            StatusMenuPatches.ApplyPatches(harmony);

            // Patch status details (arrow key navigation)
            StatusDetailsPatches.ApplyPatches(harmony);

            // Patch magic menu
            MagicMenuPatches.ApplyPatches(harmony);

            // Patch config menu
            ConfigMenuPatches.ApplyPatches(harmony);

            // Patch shop menus
            ShopPatches.ApplyPatches(harmony);

            // Patch battle item menu
            BattleItemPatchesApplier.ApplyPatches(harmony);

            // Patch battle magic menu
            BattleMagicPatchesApplier.ApplyPatches(harmony);

            // Patch vehicle/movement state changes
            MovementSpeechPatches.ApplyPatches(harmony);

            // Patch vehicle landing announcements
            VehicleLandingPatches.ApplyPatches(harmony);

            // Patch keyword system (NPC dialogue and Words menu)
            KeywordPatches.ApplyPatches(harmony);

            // Patch popup dialogs (Yes/No confirmations)
            PopupPatches.ApplyPatches(harmony);

            // Patch save/load confirmation popups
            SaveLoadPatches.ApplyPatches(harmony);

            // Patch battle pause menu
            BattlePausePatches.ApplyPatches(harmony);

            // Apply transition patches to clear menu states when menus close
            MenuTransitionPatches.ApplyPatches(harmony);

            // Patch game state transitions (map changes, battle exit) - event-driven, no polling
            GameStatePatches.ApplyPatches(harmony);

            // Map transition fade detection (suppress wall tones during screen fades)
            MapTransitionPatches.ApplyPatches(harmony);

            // Patch walk/run toggle (F1 key) for accurate state tracking
            DashFlagPatches.ApplyPatches(harmony);

            // Patch entity interactions for event-driven entity refresh
            TryPatchEntityInteractions(harmony);
        }

        /// <summary>
        /// Patches entity interaction methods for event-driven entity scanner refresh.
        /// Triggers rescan when treasure chests are opened or dialogue ends.
        /// </summary>
        private void TryPatchEntityInteractions(HarmonyLib.Harmony harmony)
        {
            try
            {
                LoggerInstance.Msg("[EntityRefresh] Applying entity interaction patches...");

                // Patch FieldTresureBox.Open() - triggers entity refresh when chest is opened
                Type treasureBoxType = typeof(Il2CppLast.Entity.Field.FieldTresureBox);
                var openMethod = treasureBoxType.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance);
                var openPostfix = typeof(ManualPatches).GetMethod("TreasureBox_Open_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (openMethod != null && openPostfix != null)
                {
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                    LoggerInstance.Msg("[EntityRefresh] Patched FieldTresureBox.Open for entity refresh");
                }
                else
                {
                    LoggerInstance.Warning($"[EntityRefresh] FieldTresureBox.Open not patched. Method: {openMethod != null}, Postfix: {openPostfix != null}");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[EntityRefresh] Error patching entity interactions: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches cursor navigation methods for menu reading.
        /// </summary>
        private void TryPatchCursorNavigation(HarmonyLib.Harmony harmony)
        {
            try
            {
                LoggerInstance.Msg("Searching for Cursor type...");

                Type cursorType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.FullName == "Il2CppLast.UI.Cursor")
                            {
                                LoggerInstance.Msg($"Found Cursor type: {type.FullName}");
                                cursorType = type;
                                break;
                            }
                        }
                        if (cursorType != null) break;
                    }
                    catch { }
                }

                if (cursorType == null)
                {
                    LoggerInstance.Warning("Cursor type not found");
                    return;
                }

                var nextIndexPostfix = typeof(ManualPatches).GetMethod("CursorNavigation_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (nextIndexPostfix == null)
                {
                    LoggerInstance.Error("Could not find postfix method");
                    return;
                }

                var nextIndexMethod = cursorType.GetMethod("NextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (nextIndexMethod != null)
                {
                    harmony.Patch(nextIndexMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched NextIndex");
                }

                var prevIndexMethod = cursorType.GetMethod("PrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (prevIndexMethod != null)
                {
                    harmony.Patch(prevIndexMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched PrevIndex");
                }

                var skipNextMethod = cursorType.GetMethod("SkipNextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipNextMethod != null)
                {
                    harmony.Patch(skipNextMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched SkipNextIndex");
                }

                var skipPrevMethod = cursorType.GetMethod("SkipPrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipPrevMethod != null)
                {
                    harmony.Patch(skipPrevMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched SkipPrevIndex");
                }

                LoggerInstance.Msg("Cursor navigation patches applied successfully");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching cursor navigation: {ex.Message}");
            }
        }

        public override void OnDeinitializeMelon()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Stop audio loops
            StopWallToneLoop();
            StopBeaconLoop();

            // Shutdown sound player (closes waveOut handles, frees unmanaged memory)
            SoundPlayer.Shutdown();

            // Dispose input manager (unsubscribes from events)
            inputManager?.Dispose();

            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                LoggerInstance.Msg($"[Scene] Loaded: {scene.name}");

                // Clear all cached GameObjects from the previous scene
                GameObjectCache.ClearAll();

                // Stop audio loops during scene transition and suppress wall tones briefly
                StopWallToneLoop();
                StopBeaconLoop();
                wallToneSuppressedUntil = Time.time + 1.0f;

                // Reset movement state for new map
                MovementSoundPatches.ResetState();

                // Reset landing zone state on scene transitions
                // Note: Do NOT reset MovementSpeechPatches here - it breaks state tracking on game load
                VehicleLandingPatches.ResetState();

                // Reset location message tracker for new scene
                LocationMessageTracker.Reset();

                // Clear all battle state flags when leaving battle (any scene transition from battle)
                // This ensures flags are cleared on flee, defeat, or any other non-victory battle exit
                if (IsInBattle)
                {
                    LoggerInstance.Msg("[Scene] Clearing battle state on scene transition");
                    ClearBattleActive();
                    BattleCommandState.ClearState();
                    BattleTargetPatches.SetTargetSelectionActive(false);
                    BattleCommandPatches.ResetTurnState();
                    BattleCommandPatches.ResetCommandCursorState();
                    BattleMagicMenuState.Reset();
                    BattleItemMenuState.Reset();
                    BattleMessagePatches.ResetState();
                }

                // Delay entity scan for scene initialization
                CoroutineManager.StartManaged(DelayedInitialScan());
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Scene] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator DelayedInitialScan()
        {
            // Wait for scene to fully initialize and entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            try
            {
                var fieldMap = UnityEngine.Object.FindObjectOfType<FieldMap>();
                if (fieldMap != null)
                {
                    GameObjectCache.Register(fieldMap);
                    LoggerInstance.Msg("[Cache] Cached FieldMap");

                    // Also cache FieldPlayerController for entity navigation
                    var playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                    if (playerController != null)
                    {
                        GameObjectCache.Register(playerController);
                        LoggerInstance.Msg("[Cache] Cached FieldPlayerController");
                    }

                    // Initial entity scan for this map
                    entityScanner.ScanEntities();
                    LoggerInstance.Msg($"[Cache] Initial scan found {entityScanner.Entities.Count} entities");
                }

                // Restart audio loops after scene has settled
                if (enableWallTones) StartWallToneLoop();
                if (enableAudioBeacons) StartBeaconLoop();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Cache] Error in DelayedInitialScan: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            // Check for mod hotkey input using optimized Input System approach
            // Uses wasPressedThisFrame with early exit on anyKey check - minimal overhead
            inputManager?.CheckInput();
        }

        /// <summary>
        /// Forces an entity rescan. Called from GameStatePatches on map transitions.
        /// </summary>
        public void ForceEntityRescan()
        {
            entityScanner?.ForceRescan();
        }

        #region Entity Navigation

        /// <summary>
        /// Checks if player is on an active field map.
        /// Returns true if on valid map (ready for entity navigation), false otherwise.
        /// Prevents entity navigation on title screen, menus, loading screens.
        /// </summary>
        internal bool EnsureFieldContext()
        {
            // Check if FieldMap exists and is active
            var fieldMap = GameObjectCache.Get<FieldMap>();
            if (fieldMap == null || !fieldMap.gameObject.activeInHierarchy)
            {
                SpeakText("Not on map");
                return false;
            }

            // Check if player controller exists
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not on map");
                return false;
            }

            return true;
        }

        internal void AnnounceCurrentEntity()
        {
            if (!EnsureFieldContext())
                return;

            try
            {
                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText("No entities found");
                    return;
                }

                var playerPos = GetPlayerPosition();
                if (!playerPos.HasValue)
                {
                    SpeakText(entity.Name);
                    return;
                }

                // Get pathfinding info using dedicated method (like FF3)
                string pathDescription = GetPathToEntity(entity, playerPos.Value);

                string announcement;
                if (!string.IsNullOrEmpty(pathDescription))
                {
                    // Only announce directions, not entity name
                    announcement = pathDescription;
                }
                else
                {
                    announcement = entity.FormatDescription(playerPos.Value);
                }

                SpeakText(announcement);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error announcing entity: {ex.Message}");
                SpeakText("Error getting entity info");
            }
        }

        /// <summary>
        /// Gets pathfinding directions to an entity.
        /// Uses FieldPlayerController for map handle and player access (like FF3/FF5).
        /// </summary>
        private string GetPathToEntity(NavigableEntity entity, Vector3 playerPos)
        {
            try
            {
                // Use FieldPlayerController directly - it has direct access to mapHandle and fieldPlayer
                var playerController = GameObjectCache.GetOrRefresh<FieldPlayerController>();

                if (playerController?.fieldPlayer == null || playerController.mapHandle == null)
                {
                    return null;
                }

                // Get target position using localPosition
                Vector3 targetPos = entity.Position;

                // Get path using the controller's mapHandle and fieldPlayer
                var pathInfo = FieldNavigationHelper.FindPathTo(
                    playerPos,
                    targetPos,
                    playerController.mapHandle,
                    playerController.fieldPlayer
                );

                if (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
                {
                    return pathInfo.Description;
                }
            }
            catch { }

            return null;
        }

        internal void CycleNext()
        {
            if (!EnsureFieldContext())
                return;

            try
            {
                // Lazy scan if entity list is empty - event-driven hooks handle state updates
                RefreshEntitiesIfNeeded();

                entityScanner.CurrentCategory = currentCategory;
                entityScanner.FilterByPathfinding = filterByPathfinding;
                entityScanner.NextEntity();

                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText("No entities found");
                    return;
                }

                var playerPos = GetPlayerPosition();
                if (playerPos.HasValue)
                {
                    string description = entity.FormatDescription(playerPos.Value);
                    int index = entityScanner.CurrentIndex + 1;
                    int total = entityScanner.Entities.Count;
                    SpeakText($"{description}, {index} of {total}");
                }
                else
                {
                    SpeakText(entity.Name);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error cycling next: {ex.Message}");
                SpeakText("Error cycling entities");
            }
        }

        internal void CyclePrevious()
        {
            if (!EnsureFieldContext())
                return;

            try
            {
                // Lazy scan if entity list is empty - event-driven hooks handle state updates
                RefreshEntitiesIfNeeded();

                entityScanner.CurrentCategory = currentCategory;
                entityScanner.FilterByPathfinding = filterByPathfinding;
                entityScanner.PreviousEntity();

                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText("No entities found");
                    return;
                }

                var playerPos = GetPlayerPosition();
                if (playerPos.HasValue)
                {
                    string description = entity.FormatDescription(playerPos.Value);
                    int index = entityScanner.CurrentIndex + 1;
                    int total = entityScanner.Entities.Count;
                    SpeakText($"{description}, {index} of {total}");
                }
                else
                {
                    SpeakText(entity.Name);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error cycling previous: {ex.Message}");
                SpeakText("Error cycling entities");
            }
        }

        internal void AnnounceEntityOnly()
        {
            try
            {
                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText("No entity selected");
                    return;
                }

                var playerPos = GetPlayerPosition();
                if (playerPos.HasValue)
                {
                    SpeakText(entity.FormatDescription(playerPos.Value));
                }
                else
                {
                    SpeakText(entity.Name);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error announcing entity: {ex.Message}");
                SpeakText("No entity selected");
            }
        }

        /// <summary>
        /// Only scans entities if the list is empty.
        /// Event-driven hooks (treasure chest open, dialogue end) handle state updates.
        /// </summary>
        private void RefreshEntitiesIfNeeded()
        {
            if (entityScanner.Entities.Count == 0)
            {
                entityScanner.ScanEntities();
            }
        }

        /// <summary>
        /// Schedules an entity refresh after a 1-frame delay.
        /// Called by interaction hooks (treasure chest, dialogue end) to update entity states.
        /// The delay ensures the game state has fully updated before rescanning.
        /// </summary>
        internal void ScheduleEntityRefresh()
        {
            CoroutineManager.StartManaged(EntityRefreshCoroutine());
        }

        private IEnumerator EntityRefreshCoroutine()
        {
            // Wait one frame for game state to fully update
            yield return null;

            // Rescan entities to pick up state changes (e.g., chest opened)
            entityScanner.ScanEntities();
            LoggerInstance.Msg("[EntityRefresh] Rescanned entities after interaction");
        }

        private Vector3? GetPlayerPosition()
        {
            try
            {
                // Try to get FieldPlayerController - first from cache, then find it
                var playerController = GameObjectCache.GetOrRefresh<FieldPlayerController>();

                if (playerController?.fieldPlayer != null)
                {
                    return playerController.fieldPlayer.transform.localPosition;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"GetPlayerPosition error: {ex.Message}");
            }
            return null;
        }

        internal void CycleNextCategory()
        {
            if (!EnsureFieldContext())
                return;

            int nextCategory = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (EntityCategory)nextCategory;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
            if (!EnsureFieldContext())
                return;

            int prevCategory = (int)currentCategory - 1;
            if (prevCategory < 0)
                prevCategory = CategoryCount - 1;

            currentCategory = (EntityCategory)prevCategory;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void ResetToAllCategory()
        {
            if (!EnsureFieldContext())
                return;

            if (currentCategory == EntityCategory.All)
            {
                SpeakText("Already in All category");
                return;
            }

            currentCategory = EntityCategory.All;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void TogglePathfindingFilter()
        {
            filterByPathfinding = !filterByPathfinding;
            entityScanner.FilterByPathfinding = filterByPathfinding;
            prefPathfindingFilter.Value = filterByPathfinding;
            prefsCategory.SaveToFile(false);

            string status = filterByPathfinding ? "on" : "off";
            SpeakText($"Pathfinding filter {status}");
        }

        internal void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;
            prefMapExitFilter.Value = filterMapExits;
            prefsCategory.SaveToFile(false);

            string status = filterMapExits ? "on" : "off";
            SpeakText($"Map exit filter {status}");
        }

        internal void ToggleWallTones()
        {
            enableWallTones = !enableWallTones;

            if (enableWallTones)
                StartWallToneLoop();
            else
                StopWallToneLoop();

            prefWallTones.Value = enableWallTones;
            prefsCategory.SaveToFile(false);

            string status = enableWallTones ? "on" : "off";
            SpeakText($"Wall tones {status}");
        }

        internal void ToggleFootsteps()
        {
            enableFootsteps = !enableFootsteps;

            prefFootsteps.Value = enableFootsteps;
            prefsCategory.SaveToFile(false);

            string status = enableFootsteps ? "on" : "off";
            SpeakText($"Footsteps {status}");
        }

        internal void ToggleAudioBeacons()
        {
            enableAudioBeacons = !enableAudioBeacons;

            if (enableAudioBeacons)
                StartBeaconLoop();
            else
                StopBeaconLoop();

            prefAudioBeacons.Value = enableAudioBeacons;
            prefsCategory.SaveToFile(false);

            string status = enableAudioBeacons ? "on" : "off";
            SpeakText($"Audio beacons {status}");
        }

        // Accessors for audio feedback state (used by MovementSoundPatches)
        internal bool IsWallTonesEnabled() => enableWallTones;
        internal bool IsFootstepsEnabled() => enableFootsteps;
        internal bool IsAudioBeaconsEnabled() => enableAudioBeacons;

        // Public static accessors for volume settings (used by SoundPlayer and ModMenu)
        public static int WallBumpVolume => prefWallBumpVolume?.Value ?? 50;
        public static int FootstepVolume => prefFootstepVolume?.Value ?? 50;
        public static int WallToneVolume => prefWallToneVolume?.Value ?? 50;
        public static int BeaconVolume => prefBeaconVolume?.Value ?? 50;

        // Public static accessor for enemy HP display mode (used by battle patches and ModMenu)
        public static int EnemyHPDisplay => prefEnemyHPDisplay?.Value ?? 0;

        // Public static accessors for filter settings (used by ModMenu)
        public static bool PathfindingFilterEnabled => Instance?.filterByPathfinding ?? false;
        public static bool MapExitFilterEnabled => Instance?.filterMapExits ?? false;
        public static bool WallTonesEnabled => Instance?.enableWallTones ?? false;
        public static bool FootstepsEnabled => Instance?.enableFootsteps ?? false;
        public static bool AudioBeaconsEnabled => Instance?.enableAudioBeacons ?? false;

        // Public static setters for ModMenu
        public static void SetWallBumpVolume(int value)
        {
            if (prefWallBumpVolume != null)
            {
                prefWallBumpVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetFootstepVolume(int value)
        {
            if (prefFootstepVolume != null)
            {
                prefFootstepVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetWallToneVolume(int value)
        {
            if (prefWallToneVolume != null)
            {
                prefWallToneVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetBeaconVolume(int value)
        {
            if (prefBeaconVolume != null)
            {
                prefBeaconVolume.Value = Math.Clamp(value, 0, 100);
                prefsCategory?.SaveToFile(false);
            }
        }

        public static void SetEnemyHPDisplay(int value)
        {
            if (prefEnemyHPDisplay != null)
            {
                prefEnemyHPDisplay.Value = Math.Clamp(value, 0, 2);
                prefsCategory?.SaveToFile(false);
            }
        }

        private void AnnounceCategoryChange()
        {
            string categoryName = GetCategoryName(currentCategory);
            SpeakText($"Category: {categoryName}");
        }

        public static string GetCategoryName(EntityCategory category)
        {
            switch (category)
            {
                case EntityCategory.All: return "All";
                case EntityCategory.Chests: return "Treasure Chests";
                case EntityCategory.NPCs: return "NPCs";
                case EntityCategory.MapExits: return "Map Exits";
                case EntityCategory.Events: return "Events";
                case EntityCategory.Vehicles: return "Vehicles";
                default: return "Unknown";
            }
        }

        #endregion

        #region Audio Loop Management

        private void StartWallToneLoop()
        {
            if (wallToneCoroutine != null) return;
            wallToneCoroutine = WallToneLoop();
            CoroutineManager.StartManaged(wallToneCoroutine);
        }

        private void StopWallToneLoop()
        {
            if (wallToneCoroutine != null)
            {
                try { MelonCoroutines.Stop(wallToneCoroutine); }
                catch { }
                wallToneCoroutine = null;
            }
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        private void StartBeaconLoop()
        {
            if (beaconCoroutine != null) return;
            beaconCoroutine = BeaconLoop();
            CoroutineManager.StartManaged(beaconCoroutine);
        }

        private void StopBeaconLoop()
        {
            if (beaconCoroutine != null)
            {
                try { MelonCoroutines.Stop(beaconCoroutine); }
                catch { }
                beaconCoroutine = null;
            }
        }

        private IEnumerator WallToneLoop()
        {
            var waitInterval = new WaitForSeconds(WALL_TONE_LOOP_INTERVAL);

            while (enableWallTones)
            {
                yield return waitInterval;

                // Check flag immediately after resuming - exit if toggled off during yield
                if (!enableWallTones)
                    break;

                try
                {
                    float currentTime = Time.time;

                    // Detect sub-map transitions and suppress tones briefly
                    int currentMapId = GetCurrentMapId();
                    if (currentMapId > 0 && wallToneMapId > 0 && currentMapId != wallToneMapId)
                    {
                        wallToneSuppressedUntil = currentTime + 1.0f;
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                    }
                    if (currentMapId > 0)
                        wallToneMapId = currentMapId;

                    if (currentTime < wallToneSuppressedUntil)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    if (MapTransitionPatches.IsScreenFading)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var player = GetFieldPlayer();
                    if (player == null)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var walls = FieldNavigationHelper.GetNearbyWallsWithDistance(player);
                    var mapExitPositions = entityScanner?.GetMapExitPositions();
                    Vector3 playerPos = player.transform.localPosition;

                    // Reuse static buffer to avoid per-cycle allocations
                    wallDirectionsBuffer.Clear();

                    if (walls.NorthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(0, 16, 0), mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.North);

                    if (walls.SouthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(0, -16, 0), mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.South);

                    if (walls.EastDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(16, 0, 0), mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.East);

                    if (walls.WestDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(-16, 0, 0), mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.West);

                    // Pass buffer directly (IList<Direction>) - no ToArray() allocation
                    SoundPlayer.PlayWallTonesLooped(wallDirectionsBuffer);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[WallTones] Error: {ex.Message}");
                }
            }

            // Cleanup when loop exits (ensures audio stops when toggled off)
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        private IEnumerator BeaconLoop()
        {
            var waitInterval = new WaitForSeconds(BEACON_INTERVAL);

            while (true)
            {
                yield return waitInterval;

                try
                {
                    var entity = entityScanner?.CurrentEntity;
                    if (entity == null) continue;

                    var playerController = GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null) continue;

                    Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
                    Vector3 entityPos = entity.Position;

                    float distance = Vector3.Distance(playerPos, entityPos);
                    float maxDist = 500f;
                    float volumeScale = Mathf.Clamp(1f - (distance / maxDist), 0.15f, 0.60f);

                    float deltaX = entityPos.x - playerPos.x;
                    float pan = Mathf.Clamp(deltaX / 100f, -1f, 1f) * 0.5f + 0.5f;

                    bool isSouth = entityPos.y < playerPos.y - 8f;

                    SoundPlayer.PlayBeacon(isSouth, pan, volumeScale);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Beacon] Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the current map ID from UserDataManager (FF2-specific).
        /// Returns -1 if unable to retrieve.
        /// </summary>
        private int GetCurrentMapId()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    return userDataManager.CurrentMapId;
                }
            }
            catch { }
            return -1;
        }

        #endregion

        #region Teleport

        internal void TeleportInDirection(Vector2 offset)
        {
            try
            {
                var player = GetFieldPlayer();
                if (player == null)
                {
                    LoggerInstance.Msg("[Teleport] No field player found");
                    SpeakText("Not on field map");
                    return;
                }

                // Get the currently selected entity
                var entity = entityScanner?.CurrentEntity;
                if (entity == null)
                {
                    LoggerInstance.Msg("[Teleport] No entity selected");
                    SpeakText("No entity selected");
                    return;
                }

                // Calculate target position: entity position + offset
                Vector3 entityPos = entity.Position;
                Vector3 playerPos = player.transform.localPosition;
                Vector3 targetPos = entityPos + new Vector3(offset.x, offset.y, 0);

                LoggerInstance.Msg($"[Teleport] Entity: {entity.Name} at ({entityPos.x}, {entityPos.y}, {entityPos.z})");
                LoggerInstance.Msg($"[Teleport] Player was at ({playerPos.x}, {playerPos.y}, {playerPos.z})");
                LoggerInstance.Msg($"[Teleport] Offset: ({offset.x}, {offset.y})");
                LoggerInstance.Msg($"[Teleport] Target: ({targetPos.x}, {targetPos.y}, {targetPos.z})");

                // Teleport player to target position
                player.transform.localPosition = targetPos;

                // Announce with direction relative to entity and entity name
                string direction = GetDirectionFromOffset(offset);
                string entityName = entity.Name;
                SpeakText($"Teleported to {direction} of {entityName}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error teleporting: {ex.Message}");
                SpeakText("Teleport failed");
            }
        }

        private string GetDirectionFromOffset(Vector2 offset)
        {
            if (Math.Abs(offset.x) > Math.Abs(offset.y))
            {
                return offset.x > 0 ? "east" : "west";
            }
            else
            {
                return offset.y > 0 ? "north" : "south";
            }
        }

        /// <summary>
        /// Gets the FieldPlayer from the FieldPlayerController.
        /// Uses direct IL2CPP property access (not reflection, which doesn't work on IL2CPP types).
        /// </summary>
        private Il2CppLast.Entity.Field.FieldPlayer GetFieldPlayer()
        {
            try
            {
                // Use FieldPlayerController directly - same pattern as FF3
                var playerController = GameObjectCache.GetOrRefresh<FieldPlayerController>();

                if (playerController?.fieldPlayer != null)
                {
                    return playerController.fieldPlayer;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting field player: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Status Announcements

        internal void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    int gil = userDataManager.OwendGil;
                    SpeakText($"{gil} Gil");
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting gil: {ex.Message}");
            }
            SpeakText("Gil not available");
        }

        internal void AnnounceCurrentMap()
        {
            try
            {
                string mapName = MapNameResolver.GetCurrentMapName();
                SpeakText(mapName);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting map name: {ex.Message}");
                SpeakText("Map name not available");
            }
        }

        internal void AnnounceCharacterStatus()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    SpeakText("Character data not available");
                    return;
                }

                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null || partyList.Count == 0)
                {
                    SpeakText("No party members");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                foreach (var charData in partyList)
                {
                    try
                    {
                        if (charData != null)
                        {
                            string name = charData.Name;
                            var param = charData.Parameter;
                            if (param != null)
                            {
                                int currentHp = param.CurrentHP;
                                int maxHp = param.ConfirmedMaxHp();
                                int currentMp = param.CurrentMP;
                                int maxMp = param.ConfirmedMaxMp();

                                sb.AppendLine($"{name}: HP {currentHp}/{maxHp}, MP {currentMp}/{maxMp}");
                            }
                        }
                    }
                    catch { }
                }

                string status = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(status))
                {
                    SpeakText(status);
                }
                else
                {
                    SpeakText("No character status available");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting character status: {ex.Message}");
                SpeakText("Character status not available");
            }
        }

        #endregion

        #region Menu State Management

        /// <summary>
        /// Clears all menu state flags including battle state.
        /// Called when returning to main menu level to ensure no stuck suppression flags.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            // Clear battle state
            ClearBattleActive();
            BattleCommandPatches.ResetTurnState();
            BattleCommandPatches.ResetCommandCursorState();
            BattleMessagePatches.ResetState();

            // Clear all menu flags via centralized registry
            MenuStateRegistry.ResetAll();

            // Also reset local state in state classes that track additional data
            // (These methods also call MenuStateRegistry.Reset internally but handle local cleanup)
            MagicMenuState.ResetState();       // Has multiple sub-flags
            ShopMenuTracker.ClearState();      // Has item tracking data
            BattleTargetPatches.ResetState();  // Has index tracking
        }

        /// <summary>
        /// Explicit flag tracking if we're in active battle.
        /// Set by BattleCommandPatches.SetCommandData_Postfix when a turn starts.
        /// Cleared by BattleResultPatches.Show_Postfix when battle ends.
        /// </summary>
        public static bool IsInBattle { get; set; } = false;

        /// <summary>
        /// Check if we're in any battle UI context.
        /// Uses explicit flag rather than FindObjectOfType to avoid false positives.
        /// </summary>
        public static bool IsInBattleUIContext()
        {
            return IsInBattle;
        }

        /// <summary>
        /// Called when battle starts (first turn command).
        /// </summary>
        public static void SetBattleActive()
        {
            IsInBattle = true;
        }

        /// <summary>
        /// Called when battle ends (result screen shows).
        /// </summary>
        public static void ClearBattleActive()
        {
            IsInBattle = false;
        }

        #endregion

        /// <summary>
        /// Speak text through the screen reader.
        /// Thread-safe: TolkWrapper uses locking to prevent concurrent native calls.
        /// </summary>
        public static void SpeakText(string text, bool interrupt = true)
        {
            tolk?.Speak(text, interrupt);
        }
    }

    /// <summary>
    /// Manual patch methods for Harmony.
    /// </summary>
    public static class ManualPatches
    {
        /// <summary>
        /// Postfix for cursor navigation methods.
        /// Uses the Active State Pattern to check if specialized patches handle announcements.
        /// </summary>
        public static void CursorNavigation_Postfix(object __instance)
        {
            try
            {
                var cursor = __instance as GameCursor;
                if (cursor == null)
                {
                    MelonLogger.Warning("Cursor is null in postfix");
                    return;
                }

                // Build cursor path for pause menu detection
                string cursorPath = "";
                try
                {
                    var t = cursor.transform;
                    cursorPath = t?.name ?? "null";
                    if (t?.parent != null) cursorPath = t.parent.name + "/" + cursorPath;
                    if (t?.parent?.parent != null) cursorPath = t.parent.parent.name + "/" + cursorPath;
                }
                catch { cursorPath = "error"; }

                // === BATTLE PAUSE MENU SPECIAL CASE ===
                // Must be checked BEFORE battle suppression because battle states would suppress it.
                // Cursor path contains "curosr_parent" (game typo) when in pause menu.
                if (cursorPath.Contains("curosr_parent"))
                {
                    MelonLogger.Msg("[CursorNav] Battle pause menu detected - reading directly");
                    CoroutineManager.StartManaged(
                        MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false)
                    );
                    return;
                }

                // === BATTLE UI CONTEXT CHECK ===
                // If any battle UI is active, suppress MenuTextDiscovery entirely.
                // Battle menus have their own specialized patches.
                if (FFII_ScreenReaderMod.IsInBattleUIContext())
                {
                    return;
                }

                // === ACTIVE STATE CHECKS ===
                // Each ShouldSuppress() validates its controller is still active.
                // If controller is gone, it auto-clears and returns false (preventing stuck flags).

                // Equipment menu (slot and item list) - needs stat comparison
                if (EquipMenuState.ShouldSuppress()) return;

                // Battle command menu - handled by SetCursor patch
                if (BattleCommandState.ShouldSuppress()) return;

                // Battle target selection - handled by SelectContent patches
                if (BattleTargetPatches.ShouldSuppress()) return;

                // Item menu - item list and target selection
                if (ItemMenuState.ShouldSuppress()) return;

                // Status menu - character selection
                if (StatusMenuState.ShouldSuppress()) return;

                // Magic menu - spell selection
                if (MagicMenuState.ShouldSuppress()) return;

                // Config menu - config options
                if (ConfigMenuState.ShouldSuppress()) return;

                // Shop menus - buy/sell lists
                if (ShopMenuTracker.ShouldSuppress()) return;

                // Battle item menu
                if (BattleItemMenuState.ShouldSuppress()) return;

                // Battle magic menu
                if (BattleMagicMenuState.ShouldSuppress()) return;

                // Keyword dialogue menu (Ask/Remember/Item)
                if (KeywordMenuState.ShouldSuppress()) return;

                // Words menu (main menu keyword browser)
                if (WordsMenuState.ShouldSuppress()) return;

                // Popup dialogs (Yes/No confirmations)
                // Route all popup button reading through PopupPatches.ReadCurrentButton
                if (PopupState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(cursor);
                    return;
                }

                // === DEFAULT: Read via MenuTextDiscovery ===
                // Start coroutine to read cursor text after one frame
                // The delay allows the UI to update before we read the text
                CoroutineManager.StartManaged(
                    MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false)
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CursorNavigation_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for FieldTresureBox.Open - triggers entity refresh when chest is opened.
        /// Updates the entity scanner to reflect the chest's new opened state.
        /// </summary>
        public static void TreasureBox_Open_Postfix()
        {
            MelonLogger.Msg("[TreasureBox] Chest opened, scheduling entity refresh");
            FFII_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }

    /// <summary>
    /// Transition patches that clear menu states when menus close.
    /// Patches SetActive(bool) methods to clear state flags when isActive becomes false.
    /// </summary>
    public static class MenuTransitionPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched) return;

            try
            {
                MelonLogger.Msg("[Transitions] Applying menu transition patches...");

                // ItemWindowController.SetActive
                TryPatchSetActive<KeyInputItemWindowController>(harmony, nameof(ItemWindowController_SetActive_Postfix));

                // EquipmentWindowController.SetActive
                TryPatchSetActive<KeyInputEquipmentWindowController>(harmony, nameof(EquipmentWindowController_SetActive_Postfix));

                // StatusWindowController.SetActive
                TryPatchSetActive<KeyInputStatusWindowController>(harmony, nameof(StatusWindowController_SetActive_Postfix));

                // AbilityWindowController.SetActive (FF2 magic menu)
                TryPatchSetActive<KeyInputAbilityWindowController>(harmony, nameof(AbilityWindowController_SetActive_Postfix));

                // ConfigController.SetActive
                TryPatchSetActive<KeyInputConfigController>(harmony, nameof(ConfigController_SetActive_Postfix));

                // ShopController.SetActive
                TryPatchSetActive<KeyInputShopController>(harmony, nameof(ShopController_SetActive_Postfix));

                // SecretWordController.SetActive (keyword dialogue)
                TryPatchSetActive<KeyInputSecretWordController>(harmony, nameof(SecretWordController_SetActive_Postfix));

                // WordsWindowController.SetActive (main menu words browser)
                TryPatchSetActive<KeyInputWordsWindowController>(harmony, nameof(WordsWindowController_SetActive_Postfix));

                isPatched = true;
                MelonLogger.Msg("[Transitions] Menu transition patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Transitions] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchSetActive<T>(HarmonyLib.Harmony harmony, string postfixMethodName)
        {
            try
            {
                Type controllerType = typeof(T);
                var setActiveMethod = controllerType.GetMethod("SetActive", new Type[] { typeof(bool) });

                if (setActiveMethod != null)
                {
                    var postfix = typeof(MenuTransitionPatches).GetMethod(postfixMethodName, BindingFlags.Public | BindingFlags.Static);
                    if (postfix != null)
                    {
                        harmony.Patch(setActiveMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg($"[Transitions] Patched {controllerType.Name}.SetActive");
                    }
                }
                else
                {
                    MelonLogger.Warning($"[Transitions] {controllerType.Name}.SetActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Transitions] Error patching {typeof(T).Name}: {ex.Message}");
            }
        }


        // === Transition Postfixes ===

        public static void ItemWindowController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                ItemMenuState.ClearState();
            }
        }

        public static void EquipmentWindowController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                EquipMenuState.ClearState();
            }
        }

        public static void StatusWindowController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                StatusMenuState.ResetState();
            }
        }

        public static void AbilityWindowController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                MagicMenuState.ResetState();
            }
        }

        public static void ConfigController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                ConfigMenuState.ResetState();
            }
        }

        public static void ShopController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                ShopMenuTracker.ClearState();
            }
        }

        public static void SecretWordController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                KeywordMenuState.ClearState();
            }
        }

        public static void WordsWindowController_SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                WordsMenuState.ClearState();
            }
        }

    }
}
