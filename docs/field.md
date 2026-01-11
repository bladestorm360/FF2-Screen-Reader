# Field Navigation Implementation

## Overview

Field navigation allows players to scan for and navigate to entities (NPCs, chests, exits, etc.) on the map using hotkeys. The system provides:
- Entity scanning and listing
- Directional announcements (N/S/E/W with distance)
- Category filtering
- Optional A* pathfinding for reachable entities

## Key Classes from dump.cs

### Field Scene
```csharp
// Line 261103 - Main field scene
public class FieldMap : SubSceneBase, ISceneStateProcess<SubSceneManagerMainGame.State>, IMenuAccessor

// Line 314473 - Field controller (full interface)
public class FieldController : IMapAccessor, IFieldResource, IMapEncounter, IEntityAccessor, IFilterAccessor, IPlayerAccessor
```

### Player Controller
```csharp
// Line 322060 - Base player controller
public class FieldPlayerController : MonoBehaviour, IInputDeviceListener

// Line 300841 - Player entity
public class FieldPlayer : FieldCharaEntity, IPadAccessor

// Variants:
// - FieldPlayerKeyController (Line 322212) - Keyboard input
// - FieldPlayerKeyAirshipController (Line 322148) - Airship keyboard
// - FieldPlayerTouchVectorMoveController (Line 322597) - Touch input
```

### Field Entities
```csharp
// Line 299700 - Base entity class
public class FieldEntity : Entities, IFieldEntityObject

// Line 257554 - Entity constants
public class FieldEntityConstants

// Line 298880 - Save point entity
public class SavePointEventEntity : EventTriggerEntity
```

### Map Data
```csharp
// Line 258117 - Route/path searching
public class MapRouteSearcher

// Line 258304 - Pathfinding exception
public class MapRouteSearchException : Exception

// Line 253432 - Collision data
public class MapCollisionGroupData : MapCollisionGroupBase
```

### Map Info
```csharp
// From ff3 reference - likely similar
FieldMapProvisionInformation.Instance.CurrentMapId

// Get map name via MessageManager
MessageManager.Instance.GetMessage($"map_name_{mapId:D4}")
```

## Entity Categories

Reference: `ff3-screen-reader/Core/FFIII_ScreenReaderMod.cs`

```csharp
public enum EntityCategory
{
    All = 0,
    Chests = 1,      // Treasure chests/boxes
    NPCs = 2,        // Characters to talk to
    MapExits = 3,    // Doors, stairs, exits
    Events = 4,      // Interactive objects
    Vehicles = 5     // Ships, canoes, airship (FF2 has canoe + ship)
}
```

## Entity Detection

Reference: `ff3-screen-reader/Field/EntityScanner.cs`

### Getting All Entities
```csharp
// Via FieldNavigationHelper.GetAllFieldEntities()
// Uses FieldController's entity list or FindObjectsOfType<FieldEntity>

var fieldEntities = FieldNavigationHelper.GetAllFieldEntities();
```

### Entity Type Detection
Detect entity type by class name and GameObject name:

```csharp
string typeName = fieldEntity.GetType().Name;
string goName = fieldEntity.gameObject.name.ToLower();

// Treasure chests
if (typeName.Contains("Treasure") || goName.Contains("chest"))
    return EntityCategory.Chests;

// NPCs
if (typeName.Contains("Chara") && !typeName.Contains("Player"))
    return EntityCategory.NPCs;

// Map exits
if (typeName.Contains("MapChange") || goName.Contains("exit") || goName.Contains("door"))
    return EntityCategory.MapExits;

// Save points
if (typeName.Contains("Save") || goName.Contains("save"))
    return EntityCategory.Events;

// Vehicles
if (goName.Contains("ship") || goName.Contains("canoe"))
    return EntityCategory.Vehicles;
```

## Directional Announcements

Reference: `ff3-screen-reader/Field/NavigableEntity.cs`

```csharp
public string FormatDescription(Vector3 playerPos)
{
    Vector3 diff = Position - playerPos;

    // Calculate cardinal direction
    string direction = GetCardinalDirection(diff);

    // Calculate distance in steps (tiles)
    int steps = (int)Math.Round(Vector3.Distance(Position, playerPos));

    return $"{Name}, {direction}, {steps} steps";
}

private string GetCardinalDirection(Vector3 diff)
{
    if (Math.Abs(diff.x) > Math.Abs(diff.y))
        return diff.x > 0 ? "East" : "West";
    else
        return diff.y > 0 ? "North" : "South";
}
```

## Pathfinding Integration

Reference: `ff3-screen-reader/Field/FieldNavigationHelper.cs`

```csharp
// Uses MapRouteSearcher for A* pathfinding
public static PathInfo FindPathTo(Vector3 from, Vector3 to, MapHandle mapHandle, FieldPlayer player)
{
    // Get map collision data
    // Run A* search
    // Return path description: "North 3, East 2, North 1"
}
```

### Pathfinding Filter
Optional filter to only show entities the player can reach:

```csharp
public class PathfindingFilter : IEntityFilter
{
    public bool IsEnabled { get; set; }

    public bool PassesFilter(NavigableEntity entity, FilterContext context)
    {
        if (!IsEnabled) return true;

        var path = FieldNavigationHelper.FindPathTo(playerPos, entity.Position, ...);
        return path.Success;
    }
}
```

## Implementation Steps

### 1. Port Core Navigation Classes
```
Field/
  NavigableEntity.cs     - Entity representation with position/category
  EntityScanner.cs       - Scans map for entities
  FieldNavigationHelper.cs - Pathfinding integration
  FilterContext.cs       - Filter state
Core/Filters/
  IEntityFilter.cs       - Filter interface
  CategoryFilter.cs      - Category filtering
  PathfindingFilter.cs   - Reachability filtering
```

### 2. Entity Type Classes
```csharp
// From ff3-screen-reader/Field/NavigableEntity.cs
public class TreasureChestEntity : NavigableEntity { ... }
public class NPCEntity : NavigableEntity { ... }
public class MapExitEntity : NavigableEntity { ... }
public class SavePointEntity : NavigableEntity { ... }
public class VehicleEntity : NavigableEntity { ... }
public class EventEntity : NavigableEntity { ... }
```

### 3. Hotkey Actions
```csharp
// In InputManager
void RegisterFieldHotkeys()
{
    // Scan/announce current entity
    RegisterKey(KeyCode.E, AnnounceCurrentEntity);

    // Cycle through entities
    RegisterKey(KeyCode.PageDown, CycleNext);
    RegisterKey(KeyCode.PageUp, CyclePrevious);

    // Cycle categories
    RegisterKey(KeyCode.Tab, CycleNextCategory);
    RegisterKey(KeyCode.LeftShift + KeyCode.Tab, CyclePreviousCategory);

    // Toggle pathfinding filter
    RegisterKey(KeyCode.P, TogglePathfindingFilter);

    // Announce map name
    RegisterKey(KeyCode.M, AnnounceCurrentMap);

    // Announce party status
    RegisterKey(KeyCode.S, AnnouncePartyStatus);
}
```

### 4. Map Transition Detection
```csharp
// Subscribe to scene load events
SceneManager.sceneLoaded += OnSceneLoaded;

private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    // Clear entity cache
    GameObjectCache.Clear<FieldMap>();

    // Delay scan for scene initialization
    CoroutineManager.StartManaged(DelayedInitialScan());
}
```

## FF2-Specific Entities

FF2 has some unique interactive elements:
- **Canoe**: Water vehicle for rivers
- **Ship**: Ocean vessel
- **Key doors**: Doors requiring specific keys
- **Secret passages**: Hidden paths (may need special detection)
