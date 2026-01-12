using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MelonLoader;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Core.Filters;
using FFII_ScreenReader.Utils;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Management;
using FieldMap = Il2Cpp.FieldMap;
using FieldNonPlayer = Il2CppLast.Entity.Field.FieldNonPlayer;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;
using EventTriggerEntity = Il2CppLast.Entity.Field.EventTriggerEntity;
using SavePointEventEntity = Il2CppLast.Entity.Field.SavePointEventEntity;
using PropertyEntity = Il2CppLast.Map.PropertyEntity;
using PropertyGotoMap = Il2CppLast.Map.PropertyGotoMap;

namespace FFII_ScreenReader.Field
{
    /// <summary>
    /// Scans the field map for navigable entities and maintains a list of them.
    /// </summary>
    public class EntityScanner
    {
        private List<NavigableEntity> entities = new List<NavigableEntity>();
        private int currentIndex = 0;
        private EntityCategory currentCategory = EntityCategory.All;
        private List<NavigableEntity> filteredEntities = new List<NavigableEntity>();
        private PathfindingFilter pathfindingFilter = new PathfindingFilter();

        // Track selected entity by identifier to maintain focus across re-sorts
        private Vector3? selectedEntityPosition = null;
        private EntityCategory? selectedEntityCategory = null;
        private string selectedEntityName = null;

        /// <summary>
        /// Whether to filter entities by pathfinding accessibility.
        /// When enabled, only entities with a valid path from the player are shown.
        /// </summary>
        public bool FilterByPathfinding
        {
            get => pathfindingFilter.IsEnabled;
            set => pathfindingFilter.IsEnabled = value;
        }

        /// <summary>
        /// Current list of entities (filtered by category)
        /// </summary>
        public List<NavigableEntity> Entities => filteredEntities;

        /// <summary>
        /// Current entity index
        /// </summary>
        public int CurrentIndex
        {
            get => currentIndex;
            set
            {
                currentIndex = value;
                SaveSelectedEntityIdentifier();
            }
        }

        /// <summary>
        /// Saves the current entity's identifier for focus restoration after re-sorting.
        /// </summary>
        private void SaveSelectedEntityIdentifier()
        {
            var entity = CurrentEntity;
            if (entity != null)
            {
                selectedEntityPosition = entity.Position;
                selectedEntityCategory = entity.Category;
                selectedEntityName = entity.Name;
            }
        }

        /// <summary>
        /// Clears the saved entity identifier (used when explicitly resetting selection).
        /// </summary>
        public void ClearSelectedEntityIdentifier()
        {
            selectedEntityPosition = null;
            selectedEntityCategory = null;
            selectedEntityName = null;
        }

        /// <summary>
        /// Finds the index of an entity matching the saved identifier.
        /// Returns -1 if not found.
        /// </summary>
        private int FindEntityByIdentifier()
        {
            if (!selectedEntityPosition.HasValue || !selectedEntityCategory.HasValue)
                return -1;

            for (int i = 0; i < filteredEntities.Count; i++)
            {
                var entity = filteredEntities[i];
                // Match by position (with small tolerance) and category
                if (entity.Category == selectedEntityCategory.Value &&
                    Vector3.Distance(entity.Position, selectedEntityPosition.Value) < 0.5f)
                {
                    return i;
                }
            }

            // Fallback: try matching by name if position changed slightly
            if (!string.IsNullOrEmpty(selectedEntityName))
            {
                for (int i = 0; i < filteredEntities.Count; i++)
                {
                    var entity = filteredEntities[i];
                    if (entity.Category == selectedEntityCategory.Value &&
                        entity.Name == selectedEntityName)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Current category filter
        /// </summary>
        public EntityCategory CurrentCategory
        {
            get => currentCategory;
            set
            {
                if (currentCategory != value)
                {
                    currentCategory = value;
                    ClearSelectedEntityIdentifier(); // Clear since we're changing category
                    ApplyFilter();
                    currentIndex = 0;
                }
            }
        }

        /// <summary>
        /// Currently selected entity
        /// </summary>
        public NavigableEntity CurrentEntity
        {
            get
            {
                if (filteredEntities.Count == 0 || currentIndex < 0 || currentIndex >= filteredEntities.Count)
                    return null;
                return filteredEntities[currentIndex];
            }
        }

        /// <summary>
        /// Scans the field for all navigable entities.
        /// </summary>
        public void ScanEntities()
        {
            entities.Clear();

            try
            {
                // Log current map ID and explore FieldMap properties
                try
                {
                    var mapInfo = Il2CppLast.Map.FieldMapProvisionInformation.Instance;
                    var fieldMap = GameObjectCache.Get<FieldMap>();

                    if (mapInfo != null)
                    {
                        int currentMapId = mapInfo.CurrentMapId;
                        MelonLogger.Msg($"[EntityScanner] Current map ID: {currentMapId}");
                    }

                    // Explore FieldMap for map name data
                    if (fieldMap != null)
                    {
                        var fieldMapType = fieldMap.GetType();
                        var props = fieldMapType.GetProperties();
                        MelonLogger.Msg($"[EntityScanner] FieldMap properties: {string.Join(", ", props.Select(p => p.Name))}");

                        // Try to find name-related properties
                        foreach (var prop in props)
                        {
                            string propLower = prop.Name.ToLower();
                            if (propLower.Contains("name") || propLower.Contains("area") ||
                                propLower.Contains("data") || propLower.Contains("info"))
                            {
                                try
                                {
                                    var value = prop.GetValue(fieldMap);
                                    MelonLogger.Msg($"[EntityScanner] FieldMap.{prop.Name} = '{value}'");
                                }
                                catch { }
                            }
                        }

                        // Check if fieldController has map data
                        if (fieldMap.fieldController != null)
                        {
                            var controller = fieldMap.fieldController;
                            var controllerType = controller.GetType();

                            // Get mapTitleMessageId - this is likely the key for map name
                            var mapTitleMsgIdProp = controllerType.GetProperty("mapTitleMessageId");
                            if (mapTitleMsgIdProp != null)
                            {
                                var msgId = mapTitleMsgIdProp.GetValue(controller);
                                MelonLogger.Msg($"[EntityScanner] FieldController.mapTitleMessageId = '{msgId}'");

                                // Try to resolve it via MessageManager
                                if (msgId != null)
                                {
                                    var messageManager = MessageManager.Instance;
                                    string mapName = messageManager?.GetMessage(msgId.ToString(), true);
                                    MelonLogger.Msg($"[EntityScanner] Resolved mapTitleMessageId -> '{mapName}'");
                                }
                            }

                            // Get currentAreaId
                            var areaIdProp = controllerType.GetProperty("currentAreaId");
                            if (areaIdProp != null)
                            {
                                var areaId = areaIdProp.GetValue(controller);
                                MelonLogger.Msg($"[EntityScanner] FieldController.currentAreaId = '{areaId}'");
                            }

                            // Get nextMap for destination info
                            var nextMapProp = controllerType.GetProperty("nextMap");
                            if (nextMapProp != null)
                            {
                                var nextMap = nextMapProp.GetValue(controller);
                                if (nextMap != null)
                                {
                                    MelonLogger.Msg($"[EntityScanner] FieldController.nextMap = '{nextMap}', type={nextMap.GetType().Name}");
                                    // Log nextMap properties
                                    var nextMapType = nextMap.GetType();
                                    var nextMapProps = nextMapType.GetProperties();
                                    MelonLogger.Msg($"[EntityScanner] nextMap properties: {string.Join(", ", nextMapProps.Select(p => p.Name))}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[EntityScanner] Could not get map info: {ex.Message}");
                }

                var fieldEntities = FieldNavigationHelper.GetAllFieldEntities();
                MelonLogger.Msg($"[EntityScanner] Found {fieldEntities.Count} field entities");

                foreach (var fieldEntity in fieldEntities)
                {
                    try
                    {
                        var navigable = ConvertToNavigableEntity(fieldEntity);
                        if (navigable != null)
                        {
                            entities.Add(navigable);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error converting entity: {ex.Message}");
                    }
                }

                MelonLogger.Msg($"[EntityScanner] Converted {entities.Count} navigable entities");

                // Re-apply filter after scanning
                ApplyFilter();

                // Reset index if out of bounds
                if (currentIndex >= filteredEntities.Count)
                    currentIndex = 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityScanner] Error scanning entities: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the current category filter.
        /// </summary>
        private void ApplyFilter()
        {
            MelonLogger.Msg($"[EntityScanner] ApplyFilter: category={currentCategory}, entities.Count={entities.Count}");

            if (currentCategory == EntityCategory.All)
            {
                filteredEntities = new List<NavigableEntity>(entities);
            }
            else
            {
                filteredEntities = entities.Where(e => e.Category == currentCategory).ToList();
            }

            MelonLogger.Msg($"[EntityScanner] After filter: filteredEntities.Count={filteredEntities.Count}");

            // Log all filtered entities
            for (int i = 0; i < filteredEntities.Count; i++)
            {
                var e = filteredEntities[i];
                MelonLogger.Msg($"[EntityScanner]   [{i}] {e.Category}: '{e.Name}'");
            }

            // Sort by distance from player
            var playerPos = GetPlayerPosition();
            if (playerPos.HasValue)
            {
                filteredEntities = filteredEntities.OrderBy(e => Vector3.Distance(e.Position, playerPos.Value)).ToList();
            }

            // Just clamp index to valid bounds - don't try to restore previous selection
            // This prevents getting "stuck" on one entity
            if (currentIndex >= filteredEntities.Count)
            {
                currentIndex = 0;
            }
        }

        /// <summary>
        /// Gets the current player position.
        /// </summary>
        private Vector3? GetPlayerPosition()
        {
            try
            {
                // Use FieldPlayerController
                var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                {
                    // Use localPosition for pathfinding
                    return playerController.fieldPlayer.transform.localPosition;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the FieldPlayer from the FieldController using reflection.
        /// The player field is private in the main game's FieldController.
        /// </summary>
        private FieldPlayer GetFieldPlayer()
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController == null)
                    return null;

                // Access private 'player' field using reflection
                var fieldType = fieldMap.fieldController.GetType();
                var playerField = fieldType.GetField("player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (playerField != null)
                {
                    return playerField.GetValue(fieldMap.fieldController) as FieldPlayer;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Moves to the next entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void NextEntity()
        {
            MelonLogger.Msg($"[EntityScanner] NextEntity called. Filter enabled: {pathfindingFilter.IsEnabled}, Category: {currentCategory}");

            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                {
                    MelonLogger.Msg("[EntityScanner] No entities after scan");
                    return;
                }
            }

            MelonLogger.Msg($"[EntityScanner] filteredEntities.Count = {filteredEntities.Count}, currentIndex = {currentIndex}");

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
                var entity = filteredEntities[currentIndex];
                MelonLogger.Msg($"[EntityScanner] Filter OFF - selected '{entity.Name}' at index {currentIndex}");
                SaveSelectedEntityIdentifier();
                return;
            }

            // With pathfinding filter, find next entity with valid path
            var context = new FilterContext();
            int attempts = 0;
            int startIndex = currentIndex;

            while (attempts < filteredEntities.Count)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
                attempts++;

                var entity = filteredEntities[currentIndex];
                if (pathfindingFilter.PassesFilter(entity, context))
                {
                    SaveSelectedEntityIdentifier();
                    return; // Found a valid entity
                }
            }

            // No reachable entities found, stay at original position
            currentIndex = startIndex;
            MelonLogger.Msg("[EntityScanner] No reachable entities found with pathfinding filter");
        }

        /// <summary>
        /// Moves to the previous entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void PreviousEntity()
        {
            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                    return;
            }

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex - 1 + filteredEntities.Count) % filteredEntities.Count;
                SaveSelectedEntityIdentifier();
                return;
            }

            // With pathfinding filter, find previous entity with valid path
            var context = new FilterContext();
            int attempts = 0;
            int startIndex = currentIndex;

            while (attempts < filteredEntities.Count)
            {
                currentIndex = (currentIndex - 1 + filteredEntities.Count) % filteredEntities.Count;
                attempts++;

                var entity = filteredEntities[currentIndex];
                if (pathfindingFilter.PassesFilter(entity, context))
                {
                    SaveSelectedEntityIdentifier();
                    return; // Found a valid entity
                }
            }

            // No reachable entities found, stay at original position
            currentIndex = startIndex;
            MelonLogger.Msg("[EntityScanner] No reachable entities found with pathfinding filter");
        }

        /// <summary>
        /// Converts a FieldEntity to a NavigableEntity.
        /// Order matters - check specific types before generic ones.
        /// </summary>
        private NavigableEntity ConvertToNavigableEntity(FieldEntity fieldEntity)
        {
            if (fieldEntity == null)
                return null;

            // Use localPosition for pathfinding compatibility
            Vector3 position = fieldEntity.transform.localPosition;
            string typeName = fieldEntity.GetType().Name;
            string goName = "";

            try
            {
                goName = fieldEntity.gameObject.name ?? "";
            }
            catch { }

            string goNameLower = goName.ToLower();

            // Log EVERY entity for debugging - show what we're working with
            MelonLogger.Msg($"[Entity] Processing: type={typeName}, name={goName}, pos=({position.x:F0},{position.y:F0},{position.z:F0})");

            // Skip the player entity
            if (typeName.Contains("FieldPlayer") || goNameLower.Contains("player"))
            {
                MelonLogger.Msg($"[Entity] SKIP (player): {typeName} / {goName}");
                return null;
            }

            // Skip party members following the player
            if (goNameLower.Contains("residentchara"))
            {
                MelonLogger.Msg($"[Entity] SKIP (party member): {typeName} / {goName}");
                return null;
            }

            // Skip inactive objects
            try
            {
                if (!fieldEntity.gameObject.activeInHierarchy)
                {
                    MelonLogger.Msg($"[Entity] SKIP (inactive): {typeName} / {goName}");
                    return null;
                }
            }
            catch { }

            // 1. Check for MoveArea FIRST - these are area boundaries, not map exits
            // MoveArea entities are trigger zones within a map, categorized as Events
            if (goNameLower.Contains("movearea"))
            {
                MelonLogger.Msg($"[Entity] -> Area Boundary: {goName}");
                return new EventEntity(fieldEntity, position, "Area Boundary", "AreaBoundary");
            }

            // 2. Check for map exit/door (GotoMap and other exit types)
            if (typeName.Contains("MapChange") || typeName.Contains("Door") ||
                typeName.Contains("Exit") || typeName.Contains("Gate") ||
                typeName.Contains("Warp") || typeName.Contains("Transfer") ||
                goNameLower.Contains("gotomap") ||
                goNameLower.Contains("exit") || goNameLower.Contains("door") ||
                goNameLower.Contains("entrance") || goNameLower.Contains("gate") ||
                goNameLower.Contains("stairs") || goNameLower.Contains("ladder") ||
                goNameLower.Contains("warp") || goNameLower.Contains("transfer"))
            {
                var (destMapId, destName) = GetMapExitDestination(fieldEntity);
                MelonLogger.Msg($"[Entity] -> MapExit: {goName}, destId={destMapId}, destName={destName}");
                return new MapExitEntity(fieldEntity, position, "Exit", destMapId, destName);
            }

            // 3. Check for treasure chest - first by type cast (most reliable), then by name
            var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
            if (treasureBox != null)
            {
                bool isOpened = GetTreasureBoxOpenedState(treasureBox);
                string name = "Treasure Chest";
                MelonLogger.Msg($"[Entity] -> Chest (TryCast): {goName}, opened={isOpened}");
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // Fallback: Check for treasure chest by name patterns
            if (typeName.Contains("Treasure") || goNameLower.Contains("treasure") ||
                goNameLower.Contains("chest") || goNameLower.Contains("box"))
            {
                string name = CleanObjectName(goName, "Treasure Chest");
                bool isOpened = CheckIfTreasureOpened(fieldEntity);
                MelonLogger.Msg($"[Entity] -> Chest (name): {name}, opened={isOpened}");
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // 4. Check for save point
            if (typeName.Contains("Save") || goNameLower.Contains("save"))
            {
                MelonLogger.Msg($"[Entity] -> SavePoint");
                return new SavePointEntity(fieldEntity, position, "Save Point");
            }

            // 5. Check for transportation/vehicles
            if (typeName.Contains("Transport") || goNameLower.Contains("ship") ||
                goNameLower.Contains("canoe") || goNameLower.Contains("airship") ||
                goNameLower.Contains("chocobo"))
            {
                string vehicleName = CleanObjectName(goName, "Vehicle");
                MelonLogger.Msg($"[Entity] -> Vehicle: {vehicleName}");
                return new VehicleEntity(fieldEntity, position, vehicleName, 0);
            }

            // 6. Check for SavePointEventEntity by type casting
            var savePointEvent = fieldEntity.TryCast<SavePointEventEntity>();
            if (savePointEvent != null)
            {
                MelonLogger.Msg($"[Entity] -> SavePoint (TryCast): {goName}");
                return new SavePointEntity(fieldEntity, position, "Save Point");
            }

            // 7. Check for EventTriggerEntity by type casting
            var eventTrigger = fieldEntity.TryCast<EventTriggerEntity>();
            if (eventTrigger != null)
            {
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                {
                    entityName = CleanObjectName(goName, "Event");
                }
                MelonLogger.Msg($"[Entity] -> Event (TryCast): {goName} -> '{entityName}'");
                return new EventEntity(fieldEntity, position, entityName, "Event");
            }

            // 8. Check for FieldNonPlayer (NPCs) by type casting - most reliable
            var fieldNonPlayer = fieldEntity.TryCast<FieldNonPlayer>();
            if (fieldNonPlayer != null)
            {
                string name = GetNpcDisplayName(fieldEntity, goName);
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                MelonLogger.Msg($"[Entity] -> NPC (FieldNonPlayer): {name}, shop={isShop}");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // 7. Check for NPC by GameObject name "FieldNpc" (fallback)
            if (goNameLower.Contains("fieldnpc"))
            {
                string name = GetNpcDisplayName(fieldEntity, goName);
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                MelonLogger.Msg($"[Entity] -> NPC (name): {name}, shop={isShop}");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // 8. Check for NPC/character by type name (fallback)
            if ((typeName.Contains("Chara") || typeName.Contains("Npc") || typeName.Contains("NPC"))
                && !typeName.Contains("Player") && !typeName.Contains("Resident"))
            {
                string name = GetNpcDisplayName(fieldEntity, goName);
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                MelonLogger.Msg($"[Entity] -> NPC (type): {name}, shop={isShop}");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // Skip visual effects and non-interactive objects
            // Note: Do NOT skip "mapobject" - these are interactive objects like springs
            // Note: Do NOT skip generic "trigger" - these may be valid EventTriggerEntity objects
            // Skip "opentrigger" specifically - these are door activation zones (we have GotoMap exits)
            if (goNameLower.Contains("effect") || goNameLower.Contains("tileanim") ||
                goNameLower.Contains("scroll") || goNameLower.Contains("pointin") ||
                goNameLower.Contains("opentrigger"))
            {
                MelonLogger.Msg($"[Entity] SKIP (visual/trigger): {typeName} / {goName}");
                return null;
            }

            // 11. Check for interactive objects (generic fallback)
            var interactiveEntity = fieldEntity.TryCast<IInteractiveEntity>();
            if (interactiveEntity != null)
            {
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                {
                    entityName = CleanObjectName(goName, "Interactive Object");
                }
                MelonLogger.Msg($"[Entity] -> Interactive (TryCast): {goName} -> '{entityName}'");
                return new EventEntity(fieldEntity, position, entityName, "Interactive");
            }

            // Include ALL remaining entities as generic events
            // This ensures nothing is filtered out - we can refine the list later
            string fallbackName = GetEntityNameFromProperty(fieldEntity);
            if (string.IsNullOrEmpty(fallbackName))
            {
                fallbackName = CleanObjectName(goName, typeName);
            }
            MelonLogger.Msg($"[Entity] -> Generic (fallback): {typeName} / {goName} -> '{fallbackName}'");
            return new EventEntity(fieldEntity, position, fallbackName, "Generic");
        }

        /// <summary>
        /// Cleans up an object name for display.
        /// </summary>
        private string CleanObjectName(string name, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return defaultName;

            // Remove common suffixes
            name = name.Replace("(Clone)", "").Trim();

            // If name is just numbers or very short, use default
            if (name.Length < 2 || name.All(c => char.IsDigit(c) || c == '_'))
                return defaultName;

            // If name starts with underscore, use default
            if (name.StartsWith("_"))
                return defaultName;

            return name;
        }

        /// <summary>
        /// Gets the entity name from PropertyEntity.Name for event/interactive entities.
        /// Falls back to trying to resolve via MessageManager if name looks like a message ID.
        /// </summary>
        private string GetEntityNameFromProperty(FieldEntity fieldEntity)
        {
            try
            {
                // Access Property directly on the IL2CPP object
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                {
                    return null;
                }

                // Get the Name property
                string name = property.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                MelonLogger.Msg($"[EntityScanner] PropertyEntity.Name = '{name}'");

                // Check if name looks like a message ID (e.g., starts with "mes_" or similar patterns)
                if (name.StartsWith("mes_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("sys_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("field_", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to resolve via MessageManager
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(name, false);
                        if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != name)
                        {
                            MelonLogger.Msg($"[EntityScanner] Resolved '{name}' -> '{localizedName}'");
                            return localizedName;
                        }
                    }
                }

                // If name looks like a readable name (not a code), use it directly
                if (!name.Contains("_") && !name.All(c => char.IsLower(c)))
                {
                    return name;
                }

                // Try to format underscore-separated names into readable form
                // e.g., "recovery_spring" -> "Recovery Spring"
                if (name.Contains("_"))
                {
                    string formatted = FormatAssetNameAsReadable(name);
                    if (!string.IsNullOrEmpty(formatted))
                    {
                        return formatted;
                    }
                }

                return name;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityScanner] Error getting entity name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Formats an underscore-separated name into a readable form.
        /// e.g., "recovery_spring" -> "Recovery Spring"
        /// </summary>
        private string FormatAssetNameAsReadable(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            // Replace underscores with spaces and title case
            string[] parts = name.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
                }
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Gets the opened state from a FieldTresureBox entity.
        /// Uses IL2CPP pointer offset to access the private isOpen field at 0x159.
        /// </summary>
        private bool GetTreasureBoxOpenedState(FieldTresureBox treasureBox)
        {
            try
            {
                // FieldTresureBox has private "isOpen" field at offset 0x159 (from dump.cs:301492)
                // First try reflection
                var isOpenField = treasureBox.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (isOpenField != null)
                {
                    var value = isOpenField.GetValue(treasureBox);
                    if (value != null)
                    {
                        MelonLogger.Msg($"[TreasureBox] Got isOpen via reflection: {value}");
                        return (bool)value;
                    }
                }

                // Fallback: Try IL2CPP pointer offset access
                // isOpen is at offset 0x159 in FieldTresureBox
                unsafe
                {
                    IntPtr ptr = treasureBox.Pointer;
                    if (ptr != IntPtr.Zero)
                    {
                        bool isOpen = *(bool*)(ptr + 0x159);
                        MelonLogger.Msg($"[TreasureBox] Got isOpen via pointer offset: {isOpen}");
                        return isOpen;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TreasureBox] Error getting isOpen: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Checks if a treasure entity has been opened (fallback for non-FieldTresureBox entities).
        /// </summary>
        private bool CheckIfTreasureOpened(FieldEntity fieldEntity)
        {
            try
            {
                // Try to check if it has an "isOpened" or "isOpen" property
                var prop = fieldEntity.GetType().GetProperty("isOpened") ??
                           fieldEntity.GetType().GetProperty("isOpen");
                if (prop != null)
                {
                    return (bool)prop.GetValue(fieldEntity);
                }

                // Try field access
                var field = fieldEntity.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return (bool)field.GetValue(fieldEntity);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Gets the destination map ID and resolved name for a map exit.
        /// Uses PropertyGotoMap.MapId for the actual destination (NOT TmeId which is the source map).
        /// Falls back to "Exit" if destination cannot be resolved.
        /// </summary>
        private (int mapId, string mapName) GetMapExitDestination(FieldEntity fieldEntity)
        {
            int destMapId = -1;
            string destName = "Exit";

            try
            {
                // Access the Property directly on the IL2CPP object
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                {
                    MelonLogger.Msg("[MapExit] Property is null");
                    return (destMapId, destName);
                }

                // Log the actual property type for debugging
                string actualType = property.GetType().FullName;
                MelonLogger.Msg($"[MapExit] Property type: {actualType}");

                // Try to cast to PropertyGotoMap which has the actual MapId destination
                var gotoMapProperty = property.TryCast<PropertyGotoMap>();
                if (gotoMapProperty != null)
                {
                    destMapId = gotoMapProperty.MapId;
                    string assetName = gotoMapProperty.AssetName;
                    MelonLogger.Msg($"[MapExit] PropertyGotoMap - MapId={destMapId}, AssetName={assetName}");

                    if (destMapId > 0)
                    {
                        string mapName = MapNameResolver.GetMapExitName(destMapId);
                        if (!string.IsNullOrEmpty(mapName))
                        {
                            destName = mapName;
                            MelonLogger.Msg($"[MapExit] Resolved map {destMapId} -> '{mapName}'");
                        }
                        else
                        {
                            // Could not resolve map name, just say "Exit"
                            MelonLogger.Msg($"[MapExit] Could not resolve map {destMapId}, using 'Exit'");
                        }
                    }
                    else if (!string.IsNullOrEmpty(assetName))
                    {
                        // MapId is 0 but we have AssetName, format it as readable name
                        destName = FormatAssetNameAsMapName(assetName);
                        MelonLogger.Msg($"[MapExit] Using AssetName: {assetName} -> '{destName}'");
                    }
                }
                else
                {
                    // Not a PropertyGotoMap - could be another exit type
                    MelonLogger.Msg("[MapExit] Property is not PropertyGotoMap, using generic 'Exit'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapExit] Error getting destination: {ex.Message}");
            }

            return (destMapId, destName);
        }

        /// <summary>
        /// Formats an asset name into a readable map name.
        /// e.g., "altair_1f" -> "Altair 1F"
        /// </summary>
        private string FormatAssetNameAsMapName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return "Exit";

            // Replace underscores with spaces and title case
            string[] parts = assetName.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    // Check if it's a floor indicator (1f, 2f, b1, etc.)
                    if (parts[i].Length <= 3 && (parts[i].EndsWith("f") || parts[i].StartsWith("b")))
                    {
                        parts[i] = parts[i].ToUpper();
                    }
                    else
                    {
                        // Title case
                        parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
                    }
                }
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Gets the display name for an NPC entity.
        /// Tries to access Property object for NPC data, falls back to generic name.
        /// </summary>
        private string GetNpcDisplayName(FieldEntity fieldEntity, string gameObjectName)
        {
            try
            {
                var entityType = fieldEntity.GetType();

                // Try to access the Property object which contains NPC configuration
                var propertyProp = entityType.GetProperty("Property");
                if (propertyProp != null)
                {
                    var propertyObj = propertyProp.GetValue(fieldEntity);
                    if (propertyObj != null)
                    {
                        var propType = propertyObj.GetType();

                        // Log Property type and its members for debugging
                        var propMembers = propType.GetProperties();
                        MelonLogger.Msg($"[NPC] Property type: {propType.Name}, members: {string.Join(", ", propMembers.Select(p => p.Name))}");

                        // Try to get character/NPC name from Property
                        string[] nameProps = { "characterName", "npcName", "displayName", "Name", "name" };
                        foreach (var nameProp in nameProps)
                        {
                            var innerProp = propType.GetProperty(nameProp);
                            if (innerProp != null && innerProp.PropertyType == typeof(string))
                            {
                                string name = innerProp.GetValue(propertyObj) as string;
                                if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Clone"))
                                {
                                    MelonLogger.Msg($"[NPC] Found name in Property.{nameProp}: {name}");
                                    return name;
                                }
                            }
                        }

                        // Try to get character ID from Property and resolve via MessageManager
                        string[] idProps = { "characterId", "charaId", "npcId", "id" };
                        foreach (var idPropName in idProps)
                        {
                            var idProp = propType.GetProperty(idPropName);
                            if (idProp != null)
                            {
                                try
                                {
                                    int charId = Convert.ToInt32(idProp.GetValue(propertyObj));
                                    if (charId > 0)
                                    {
                                        var messageManager = MessageManager.Instance;
                                        if (messageManager != null)
                                        {
                                            string[] keyFormats = { $"chara_name_{charId:D4}", $"npc_name_{charId:D4}", $"character_{charId:D4}" };
                                            foreach (var keyFormat in keyFormats)
                                            {
                                                string name = messageManager.GetMessage(keyFormat);
                                                if (!string.IsNullOrEmpty(name))
                                                {
                                                    MelonLogger.Msg($"[NPC] Resolved name from Property.{idPropName}={charId}: {name}");
                                                    return name;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[NPC] Error getting display name: {ex.Message}");
            }

            // Fallback to generic NPC (don't use GameObject name as it's just "FieldNpc(Clone)")
            return "NPC";
        }
    }
}
