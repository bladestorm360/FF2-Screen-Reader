using MelonLoader;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Menus;
using FFII_ScreenReader.Patches;
using FFII_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;
using FieldMap = Il2Cpp.FieldMap;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using FieldMapProvisionInformation = Il2CppLast.Map.FieldMapProvisionInformation;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

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

        // Entity scanning
        private const float ENTITY_SCAN_INTERVAL = 5f;
        private float lastEntityScanTime = 0f;

        private static readonly int CategoryCount = Enum.GetValues(typeof(EntityCategory)).Length;
        private EntityCategory currentCategory = EntityCategory.All;

        // Filter toggles
        private bool filterByPathfinding = false;
        private bool filterMapExits = false;

        // Preferences
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("FFII Screen Reader Mod loaded!");

            // Subscribe to scene load events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFII_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination");

            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize entity scanner
            entityScanner = new EntityScanner();

            // Apply Harmony patches
            TryManualPatching();
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

            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                LoggerInstance.Msg($"[Scene] Loaded: {scene.name}");
                GameObjectCache.Clear<FieldMap>();

                // Reset vehicle/movement state on scene transitions
                MovementSpeechPatches.ResetState();
                VehicleLandingPatches.ResetState();
                MoveStateMonitor.ResetState();

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
            yield return new UnityEngine.WaitForSeconds(0.5f);

            try
            {
                var fieldMap = UnityEngine.Object.FindObjectOfType<FieldMap>();
                if (fieldMap != null)
                {
                    GameObjectCache.Register(fieldMap);
                    LoggerInstance.Msg("[Cache] Cached FieldMap");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Cache] Error caching FieldMap: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            inputManager.Update();
        }

        #region Entity Navigation

        internal void AnnounceCurrentEntity()
        {
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
                var playerController = GameObjectCache.Get<FieldPlayerController>();

                // If not in cache, try to find it
                if (playerController == null)
                {
                    playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                    if (playerController != null)
                    {
                        GameObjectCache.Register(playerController);
                    }
                }

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
            try
            {
                // Rescan if enough time has passed
                if (Time.time - lastEntityScanTime > ENTITY_SCAN_INTERVAL)
                {
                    entityScanner.ScanEntities();
                    lastEntityScanTime = Time.time;
                }

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
            try
            {
                // Rescan if enough time has passed
                if (Time.time - lastEntityScanTime > ENTITY_SCAN_INTERVAL)
                {
                    entityScanner.ScanEntities();
                    lastEntityScanTime = Time.time;
                }

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

        private Vector3? GetPlayerPosition()
        {
            try
            {
                // Try to get FieldPlayerController - first from cache, then find it
                var playerController = GameObjectCache.Get<FieldPlayerController>();

                // If not in cache, try to find it
                if (playerController == null)
                {
                    playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                    if (playerController != null)
                    {
                        GameObjectCache.Register(playerController);
                        LoggerInstance.Msg("[Cache] Cached FieldPlayerController");
                    }
                }

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
            int nextCategory = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (EntityCategory)nextCategory;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
            int prevCategory = (int)currentCategory - 1;
            if (prevCategory < 0)
                prevCategory = CategoryCount - 1;

            currentCategory = (EntityCategory)prevCategory;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void ResetToAllCategory()
        {
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
                var playerController = GameObjectCache.Get<FieldPlayerController>();

                // If not in cache, try to find it
                if (playerController == null)
                {
                    playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                    if (playerController != null)
                    {
                        GameObjectCache.Register(playerController);
                    }
                }

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
        /// Clears all menu states except the specified one.
        /// Called by patches when a menu activates to ensure only one menu suppresses cursor at a time.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            if (exceptMenu != "Equip") EquipMenuState.ClearState();
            if (exceptMenu != "BattleCommand") BattleCommandState.ClearState();
            if (exceptMenu != "BattleTarget") BattleTargetPatches.SetTargetSelectionActive(false);
            if (exceptMenu != "Item") ItemMenuState.ClearState();
            if (exceptMenu != "Status") StatusMenuState.ResetState();
            if (exceptMenu != "Magic") MagicMenuState.ResetState();
            if (exceptMenu != "Config") ConfigMenuState.ResetState();
            if (exceptMenu != "Shop") ShopMenuTracker.ResetState();
            if (exceptMenu != "BattleItem") BattleItemMenuState.Reset();
            if (exceptMenu != "BattleMagic") BattleMagicMenuState.Reset();
            if (exceptMenu != "Keyword") KeywordMenuState.ClearState();
            if (exceptMenu != "Words") WordsMenuState.ClearState();
            if (exceptMenu != "Popup") PopupState.ClearState();
        }

        /// <summary>
        /// Clears all menu state flags.
        /// Called when returning to main menu level to ensure no stuck suppression flags.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            EquipMenuState.ClearState();
            BattleCommandState.ClearState();
            BattleTargetPatches.SetTargetSelectionActive(false);
            ItemMenuState.ClearState();
            StatusMenuState.ResetState();
            MagicMenuState.ResetState();
            ConfigMenuState.ResetState();
            ShopMenuTracker.ResetState();
            BattleItemMenuState.Reset();
            BattleMagicMenuState.Reset();
            KeywordMenuState.ClearState();
            WordsMenuState.ClearState();
            PopupState.ClearState();
        }

        /// <summary>
        /// Fast check if any menu state flag is currently set.
        /// Used to detect when we should check for main menu fallback reset.
        /// </summary>
        public static bool AnyMenuStateActive()
        {
            return EquipMenuState.IsActive ||
                   BattleCommandState.IsActive ||
                   BattleTargetPatches.IsTargetSelectionActive ||
                   ItemMenuState.IsActive ||
                   StatusMenuState.IsActive ||
                   MagicMenuState.IsActive ||
                   ConfigMenuState.IsActive ||
                   ShopMenuTracker.IsActive ||
                   BattleItemMenuState.IsActive ||
                   BattleMagicMenuState.IsActive ||
                   KeywordMenuState.IsActive ||
                   WordsMenuState.IsActive ||
                   PopupState.IsActive;
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
                if (PopupState.ShouldSuppress()) return;

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
    }
}
