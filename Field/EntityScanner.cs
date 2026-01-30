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
using PropertyTransportation = Il2CppLast.Map.PropertyTransportation;
using FieldEntity = Il2CppLast.Entity.Field.FieldEntity;
using FieldAirShip = Il2CppLast.Entity.Field.FieldAirShip;

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

        // Incremental scanning: map FieldEntity to its NavigableEntity conversion
        // This avoids re-converting the same entities every scan
        private Dictionary<FieldEntity, NavigableEntity> entityMap = new Dictionary<FieldEntity, NavigableEntity>();

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
        /// Returns positions of all MapExitEntity instances from the unfiltered entity list.
        /// Used by wall tone suppression to avoid false positives at map exits/doors/stairs.
        /// Reads from the unfiltered list so it works regardless of the active category filter.
        /// </summary>
        public List<Vector3> GetMapExitPositions()
        {
            var positions = new List<Vector3>();
            foreach (var entity in entities)
            {
                if (entity is MapExitEntity)
                    positions.Add(entity.Position);
            }
            return positions;
        }

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
        /// Scans the field for all navigable entities using incremental scanning.
        /// Only converts new entities, keeping existing conversions to improve performance.
        /// </summary>
        public void ScanEntities()
        {
            try
            {
                var fieldEntities = FieldNavigationHelper.GetAllFieldEntities();
                var currentSet = new HashSet<FieldEntity>(fieldEntities);

                // Remove entities that no longer exist
                var toRemove = entityMap.Keys.Where(k => !currentSet.Contains(k)).ToList();
                foreach (var key in toRemove)
                    entityMap.Remove(key);

                // Only process NEW entities (ones not already in the map)
                foreach (var fieldEntity in fieldEntities)
                {
                    if (!entityMap.ContainsKey(fieldEntity))
                    {
                        try
                        {
                            var navigable = ConvertToNavigableEntity(fieldEntity);
                            if (navigable != null)
                            {
                                entityMap[fieldEntity] = navigable;
                            }
                        }
                        catch { }  // Silently skip entities that fail to convert
                    }
                }

                // Update the entities list from the map
                entities = entityMap.Values.ToList();

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
        /// Forces a full rescan by clearing the entity cache.
        /// Use this when changing maps or when entities may have changed state.
        /// </summary>
        public void ForceRescan()
        {
            entityMap.Clear();
            loggedEntityTypes.Clear(); // Clear debug log tracking for fresh output
            ScanEntities();
        }

        /// <summary>
        /// Applies the current category filter.
        /// </summary>
        private void ApplyFilter()
        {
            if (currentCategory == EntityCategory.All)
            {
                filteredEntities = new List<NavigableEntity>(entities);
            }
            else
            {
                filteredEntities = entities.Where(e => e.Category == currentCategory).ToList();
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
            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                    return;
            }

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
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
        }

        // Debug: track logged entity types to avoid spam
        private static HashSet<string> loggedEntityTypes = new HashSet<string>();
        private static bool vehicleDebugEnabled = false;

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

            // Skip the player entity
            if (typeName.Contains("FieldPlayer") || goNameLower.Contains("player"))
                return null;

            // Check VehicleTypeMap first - this has the accurate type from Transportation.ModelList
            if (FieldNavigationHelper.VehicleTypeMap.TryGetValue(fieldEntity, out int vehicleType))
            {
                string vehicleName = GetVehicleNameFromType(vehicleType);
                return new VehicleEntity(fieldEntity, position, vehicleName, vehicleType);
            }

            // Check for FieldAirShip BEFORE the residentchara filter
            // Vehicles may have similar naming to resident characters but should be detected
            var airship = fieldEntity.TryCast<FieldAirShip>();
            if (airship != null)
            {
                string vehicleName = CleanObjectName(goName, "Airship");
                return new VehicleEntity(fieldEntity, position, vehicleName, 3); // 3 = Plane/Airship in TransportationType
            }

            // Check for vehicles via PropertyTransportation
            // Vehicles from Transportation.ModelList have this property type
            try
            {
                var property = fieldEntity.Property;
                if (property != null)
                {
                    var transportProperty = property.TryCast<PropertyTransportation>();
                    if (transportProperty != null)
                    {
                        // Determine vehicle type from name
                        string vehicleName = GetVehicleNameFromProperty(goName, typeName);
                        int vehicleTypeFromName = GetVehicleTypeFromName(vehicleName);
                        // Skip unknown vehicle types (Type 0)
                        if (vehicleTypeFromName == 0)
                            return null;
                        return new VehicleEntity(fieldEntity, position, vehicleName, vehicleTypeFromName);
                    }
                }
            }
            catch { }

            // Skip party members following the player (but not vehicles which were checked above)
            if (goNameLower.Contains("residentchara"))
            {
                return null;
            }

            // Skip inactive objects
            try
            {
                if (!fieldEntity.gameObject.activeInHierarchy)
                    return null;
            }
            catch { }

            // 1. Check for MoveArea FIRST - these are area boundaries, not map exits
            if (goNameLower.Contains("movearea"))
                return new EventEntity(fieldEntity, position, "Area Boundary", "AreaBoundary");

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
                return new MapExitEntity(fieldEntity, position, "Exit", destMapId, destName);
            }

            // 3. Check for treasure chest - first by type cast (most reliable), then by name
            var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
            if (treasureBox != null)
            {
                bool isOpened = GetTreasureBoxOpenedState(treasureBox);
                return new TreasureChestEntity(fieldEntity, position, "Treasure Chest", isOpened);
            }

            // Fallback: Check for treasure chest by name patterns
            if (typeName.Contains("Treasure") || goNameLower.Contains("treasure") ||
                goNameLower.Contains("chest") || goNameLower.Contains("box"))
            {
                string name = CleanObjectName(goName, "Treasure Chest");
                bool isOpened = CheckIfTreasureOpened(fieldEntity);
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // 4. Check for save point
            if (typeName.Contains("Save") || goNameLower.Contains("save"))
                return new SavePointEntity(fieldEntity, position, "Save Point");

            // 5. Check for transportation/vehicles (string-based fallback)
            if (typeName.Contains("Transport") || goNameLower.Contains("ship") ||
                goNameLower.Contains("canoe") || goNameLower.Contains("airship") ||
                goNameLower.Contains("chocobo"))
            {
                string vehicleName = CleanObjectName(goName, "Vehicle");
                return new VehicleEntity(fieldEntity, position, vehicleName, 0);
            }

            // 6. Check for SavePointEventEntity by type casting
            var savePointEvent = fieldEntity.TryCast<SavePointEventEntity>();
            if (savePointEvent != null)
                return new SavePointEntity(fieldEntity, position, "Save Point");

            // 7. Check for EventTriggerEntity by type casting
            var eventTrigger = fieldEntity.TryCast<EventTriggerEntity>();
            if (eventTrigger != null)
            {
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                    entityName = CleanObjectName(goName, "Event");
                return new EventEntity(fieldEntity, position, entityName, "Event");
            }

            // 8. Check for FieldNonPlayer (NPCs) by type casting - most reliable
            var fieldNonPlayer = fieldEntity.TryCast<FieldNonPlayer>();
            if (fieldNonPlayer != null)
            {
                string name = GetNpcDisplayName(fieldEntity, goName);
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // 9. Check for NPC by GameObject name "FieldNpc" (fallback)
            if (goNameLower.Contains("fieldnpc"))
            {
                string name = GetNpcDisplayName(fieldEntity, goName);
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // 10. Check for NPC/character by type name (fallback)
            if ((typeName.Contains("Chara") || typeName.Contains("Npc") || typeName.Contains("NPC"))
                && !typeName.Contains("Player") && !typeName.Contains("Resident"))
            {
                string name = GetNpcDisplayName(fieldEntity, goName);
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // Skip visual effects and non-interactive objects
            if (goNameLower.Contains("effect") || goNameLower.Contains("tileanim") ||
                goNameLower.Contains("scroll") || goNameLower.Contains("pointin") ||
                goNameLower.Contains("opentrigger"))
                return null;

            // 11. Check for interactive objects (generic fallback)
            var interactiveEntity = fieldEntity.TryCast<IInteractiveEntity>();
            if (interactiveEntity != null)
            {
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                    entityName = CleanObjectName(goName, "Interactive Object");
                return new EventEntity(fieldEntity, position, entityName, "Interactive");
            }

            // Include ALL remaining entities as generic events
            string fallbackName = GetEntityNameFromProperty(fieldEntity);
            if (string.IsNullOrEmpty(fallbackName))
                fallbackName = CleanObjectName(goName, typeName);
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
        /// Gets a display name for a vehicle based on TransportationType.
        /// </summary>
        private string GetVehicleNameFromType(int transportType)
        {
            // TransportationType enum values from dump.cs
            return transportType switch
            {
                2 => "Ship",
                3 => "Airship",
                6 => "Submarine",
                7 => "Airship",      // LowFlying variant
                8 => "Airship",      // SpecialPlane variant
                9 => "Yellow Chocobo",
                10 => "Black Chocobo",
                _ => "Vehicle"
            };
        }

        /// <summary>
        /// Gets a display name for a vehicle entity.
        /// </summary>
        private string GetVehicleNameFromProperty(string goName, string typeName)
        {
            string nameLower = goName.ToLower();

            // Check for specific vehicle keywords
            if (nameLower.Contains("airship") || typeName.Contains("AirShip"))
                return "Airship";
            if (nameLower.Contains("chocobo"))
                return "Chocobo";
            if (nameLower.Contains("canoe"))
                return "Canoe";
            if (nameLower.Contains("ship") || nameLower.Contains("boat"))
                return "Ship";

            // Default to cleaned name or generic
            string cleaned = CleanObjectName(goName, "");
            return string.IsNullOrEmpty(cleaned) ? "Vehicle" : cleaned;
        }

        /// <summary>
        /// Gets the TransportationType enum value from vehicle name.
        /// </summary>
        private int GetVehicleTypeFromName(string vehicleName)
        {
            string nameLower = vehicleName.ToLower();

            // TransportationType enum values from dump.cs
            // Ship=2, Plane=3, YellowChocobo=9, BlackChocobo=10
            if (nameLower.Contains("airship") || nameLower.Contains("plane"))
                return 3; // Plane/Airship
            if (nameLower.Contains("ship") || nameLower.Contains("boat"))
                return 2; // Ship
            if (nameLower.Contains("chocobo"))
                return 9; // YellowChocobo (default)

            return 0; // Unknown
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
                    return null;

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
                            return EntityTranslator.Translate(localizedName);
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
                        return EntityTranslator.Translate(formatted);
                    }
                }

                return EntityTranslator.Translate(name);
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
                // First try reflection
                var isOpenField = treasureBox.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (isOpenField != null)
                {
                    var value = isOpenField.GetValue(treasureBox);
                    if (value != null)
                        return (bool)value;
                }

                // Fallback: Try IL2CPP pointer offset access
                unsafe
                {
                    IntPtr ptr = treasureBox.Pointer;
                    if (ptr != IntPtr.Zero)
                    {
                        bool isOpen = *(bool*)(ptr + 0x159);
                        return isOpen;
                    }
                }
            }
            catch { }
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
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                    return (destMapId, destName);

                // Try to cast to PropertyGotoMap which has the actual MapId destination
                var gotoMapProperty = property.TryCast<PropertyGotoMap>();
                if (gotoMapProperty != null)
                {
                    destMapId = gotoMapProperty.MapId;
                    string assetName = gotoMapProperty.AssetName;

                    if (destMapId > 0)
                    {
                        string mapName = MapNameResolver.GetMapExitName(destMapId);
                        if (!string.IsNullOrEmpty(mapName))
                            destName = mapName;
                    }
                    else if (!string.IsNullOrEmpty(assetName))
                    {
                        destName = FormatAssetNameAsMapName(assetName);
                    }
                }
            }
            catch { }

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

                        // Try to get character/NPC name from Property
                        string[] nameProps = { "characterName", "npcName", "displayName", "Name", "name" };
                        foreach (var nameProp in nameProps)
                        {
                            var innerProp = propType.GetProperty(nameProp);
                            if (innerProp != null && innerProp.PropertyType == typeof(string))
                            {
                                string name = innerProp.GetValue(propertyObj) as string;
                                if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Clone"))
                                    return EntityTranslator.Translate(name);
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
                                                    return EntityTranslator.Translate(name);
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
            catch { }

            // Fallback to generic NPC (don't use GameObject name as it's just "FieldNpc(Clone)")
            return "NPC";
        }
    }
}
