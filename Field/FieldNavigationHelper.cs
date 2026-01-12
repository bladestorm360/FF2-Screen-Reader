using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using FFII_ScreenReader.Utils;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using FieldMap = Il2Cpp.FieldMap;
using MapRouteSearcher = Il2Cpp.MapRouteSearcher;

namespace FFII_ScreenReader.Field
{
    /// <summary>
    /// Result of a pathfinding operation
    /// </summary>
    public class PathInfo
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int StepCount { get; set; }
        public string Description { get; set; }
        public List<Vector3> WorldPath { get; set; }

        public PathInfo()
        {
            Success = false;
            WorldPath = new List<Vector3>();
        }
    }

    /// <summary>
    /// Helper for field navigation and pathfinding.
    /// Uses the game's native pathfinding system.
    /// </summary>
    public static class FieldNavigationHelper
    {
        /// <summary>
        /// Gets all field entities in the current map.
        /// </summary>
        public static List<FieldEntity> GetAllFieldEntities()
        {
            var results = new List<FieldEntity>();

            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController == null)
                    return results;

                var entityList = fieldMap.fieldController.entityList;
                if (entityList != null)
                {
                    foreach (var fieldEntity in entityList)
                    {
                        if (fieldEntity != null)
                        {
                            results.Add(fieldEntity);
                        }
                    }
                }

                // Also check for transportation entities
                if (fieldMap.fieldController.transportation != null)
                {
                    try
                    {
                        var transportationEntities = fieldMap.fieldController.transportation.NeedInteractiveList();
                        if (transportationEntities != null)
                        {
                            foreach (var interactiveEntity in transportationEntities)
                            {
                                if (interactiveEntity == null) continue;

                                var fieldEntity = interactiveEntity.TryCast<FieldEntity>();
                                if (fieldEntity != null && !results.Contains(fieldEntity))
                                {
                                    results.Add(fieldEntity);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting field entities: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Gets walkable directions from the current position.
        /// </summary>
        public static string GetWalkableDirections(FieldPlayer player, IMapAccessor mapHandle)
        {
            if (player == null || mapHandle == null)
                return "Cannot check directions";

            Vector3 currentPos = player.transform.position;
            float stepSize = 16f;

            var directions = new List<string>();

            Vector3 northPos = currentPos + new Vector3(0, stepSize, 0);
            if (CheckPositionWalkable(player, northPos))
                directions.Add("North");

            Vector3 southPos = currentPos + new Vector3(0, -stepSize, 0);
            if (CheckPositionWalkable(player, southPos))
                directions.Add("South");

            Vector3 eastPos = currentPos + new Vector3(stepSize, 0, 0);
            if (CheckPositionWalkable(player, eastPos))
                directions.Add("East");

            Vector3 westPos = currentPos + new Vector3(-stepSize, 0, 0);
            if (CheckPositionWalkable(player, westPos))
                directions.Add("West");

            if (directions.Count == 0)
                return "STUCK - No walkable directions!";

            return string.Join(", ", directions);
        }

        private static bool CheckPositionWalkable(FieldPlayer player, Vector3 position)
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController != null)
                {
                    return fieldMap.fieldController.IsCanMoveToDestPosition(player, ref position);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds a path from the player position to the target position.
        /// Uses the game's MapRouteSearcher for collision-aware pathfinding.
        /// </summary>
        public static PathInfo FindPathTo(Vector3 playerWorldPos, Vector3 targetWorldPos, IMapAccessor mapHandle, FieldPlayer player = null)
        {
            var pathInfo = new PathInfo { Success = false };

            if (mapHandle == null)
            {
                pathInfo.ErrorMessage = "Map handle not available";
                MelonLogger.Msg("[Pathfinding] FAIL: Map handle is null");
                return pathInfo;
            }

            try
            {
                int mapWidth = mapHandle.GetCollisionLayerWidth();
                int mapHeight = mapHandle.GetCollisionLayerHeight();

                // Log map info
                MelonLogger.Msg($"[Pathfinding] Map dimensions: {mapWidth}x{mapHeight}");
                MelonLogger.Msg($"[Pathfinding] Player world pos: ({playerWorldPos.x:F1}, {playerWorldPos.y:F1}, {playerWorldPos.z:F1})");
                MelonLogger.Msg($"[Pathfinding] Target world pos: ({targetWorldPos.x:F1}, {targetWorldPos.y:F1}, {targetWorldPos.z:F1})");

                // Convert world coordinates to cell coordinates
                Vector3 startCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + playerWorldPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - playerWorldPos.y * 0.0625f),
                    0
                );

                Vector3 destCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + targetWorldPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - targetWorldPos.y * 0.0625f),
                    0
                );

                MelonLogger.Msg($"[Pathfinding] Start cell: ({startCell.x:F0}, {startCell.y:F0}, {startCell.z:F0})");
                MelonLogger.Msg($"[Pathfinding] Dest cell (initial): ({destCell.x:F0}, {destCell.y:F0}, {destCell.z:F0})");

                // Validate cell bounds
                bool startInBounds = startCell.x >= 0 && startCell.x < mapWidth && startCell.y >= 0 && startCell.y < mapHeight;
                bool destInBounds = destCell.x >= 0 && destCell.x < mapWidth && destCell.y >= 0 && destCell.y < mapHeight;
                MelonLogger.Msg($"[Pathfinding] Bounds check: start={startInBounds}, dest={destInBounds}");

                if (!startInBounds || !destInBounds)
                {
                    pathInfo.ErrorMessage = "Cells out of bounds";
                    MelonLogger.Msg("[Pathfinding] FAIL: Start or dest cell out of bounds");
                    return pathInfo;
                }

                if (player != null)
                {
                    int playerLayer = player.gameObject.layer;
                    float layerZ = playerLayer - 9;
                    startCell.z = layerZ;
                    MelonLogger.Msg($"[Pathfinding] Player layer: {playerLayer}, Z offset: {layerZ}");
                    MelonLogger.Msg($"[Pathfinding] Start cell (with Z): ({startCell.x:F0}, {startCell.y:F0}, {startCell.z:F0})");
                }

                Il2CppSystem.Collections.Generic.List<Vector3> pathPoints = null;

                if (player != null)
                {
                    bool playerCollisionState = player._IsOnCollision_k__BackingField;
                    MelonLogger.Msg($"[Pathfinding] Player collision state: {playerCollisionState}");

                    // Try pathfinding with different destination layers until one succeeds
                    for (int tryDestZ = 2; tryDestZ >= 0; tryDestZ--)
                    {
                        destCell.z = tryDestZ;
                        pathPoints = MapRouteSearcher.Search(mapHandle, startCell, destCell, playerCollisionState);

                        int pointCount = pathPoints?.Count ?? 0;
                        MelonLogger.Msg($"[Pathfinding] Try Z={tryDestZ}: Search returned {pointCount} points");

                        if (pathPoints != null && pathPoints.Count > 0)
                        {
                            MelonLogger.Msg($"[Pathfinding] SUCCESS at Z={tryDestZ}");
                            break;
                        }
                    }

                    // If direct path failed, try adjacent tiles
                    if (pathPoints == null || pathPoints.Count == 0)
                    {
                        MelonLogger.Msg("[Pathfinding] Direct path failed, trying adjacent tiles...");

                        Vector3[] adjacentOffsets = new Vector3[] {
                            new Vector3(0, 16, 0),    // north
                            new Vector3(16, 0, 0),    // east
                            new Vector3(0, -16, 0),   // south
                            new Vector3(-16, 0, 0),   // west
                            new Vector3(16, 16, 0),   // northeast
                            new Vector3(16, -16, 0),  // southeast
                            new Vector3(-16, -16, 0), // southwest
                            new Vector3(-16, 16, 0)   // northwest
                        };

                        string[] dirNames = { "N", "E", "S", "W", "NE", "SE", "SW", "NW" };
                        int dirIndex = 0;

                        foreach (var offset in adjacentOffsets)
                        {
                            Vector3 adjacentTargetWorld = targetWorldPos + offset;

                            Vector3 adjacentDestCell = new Vector3(
                                Mathf.FloorToInt(mapWidth * 0.5f + adjacentTargetWorld.x * 0.0625f),
                                Mathf.FloorToInt(mapHeight * 0.5f - adjacentTargetWorld.y * 0.0625f),
                                0
                            );

                            for (int tryDestZ = 2; tryDestZ >= 0; tryDestZ--)
                            {
                                adjacentDestCell.z = tryDestZ;
                                pathPoints = MapRouteSearcher.Search(mapHandle, startCell, adjacentDestCell, playerCollisionState);

                                if (pathPoints != null && pathPoints.Count > 0)
                                {
                                    MelonLogger.Msg($"[Pathfinding] SUCCESS via adjacent {dirNames[dirIndex]} at Z={tryDestZ}");
                                    break;
                                }
                            }

                            if (pathPoints != null && pathPoints.Count > 0)
                                break;

                            dirIndex++;
                        }
                    }
                }
                else
                {
                    MelonLogger.Msg("[Pathfinding] No player reference, using SearchSimple");
                    pathPoints = MapRouteSearcher.SearchSimple(mapHandle, startCell, destCell);
                    int pointCount = pathPoints?.Count ?? 0;
                    MelonLogger.Msg($"[Pathfinding] SearchSimple returned {pointCount} points");
                }

                if (pathPoints == null || pathPoints.Count == 0)
                {
                    pathInfo.ErrorMessage = "No path found";
                    MelonLogger.Msg("[Pathfinding] FAIL: No path found after all attempts");
                    return pathInfo;
                }

                pathInfo.WorldPath = new List<Vector3>();
                for (int i = 0; i < pathPoints.Count; i++)
                {
                    pathInfo.WorldPath.Add(pathPoints[i]);
                }

                pathInfo.Success = true;
                pathInfo.StepCount = pathPoints.Count > 0 ? pathPoints.Count - 1 : 0;
                pathInfo.Description = DescribePath(pathInfo.WorldPath);

                MelonLogger.Msg($"[Pathfinding] SUCCESS: {pathInfo.StepCount} steps - {pathInfo.Description}");
                return pathInfo;
            }
            catch (Exception ex)
            {
                pathInfo.ErrorMessage = $"Pathfinding error: {ex.Message}";
                MelonLogger.Warning($"[Pathfinding] EXCEPTION: {ex.Message}");
                return pathInfo;
            }
        }

        /// <summary>
        /// Creates a human-readable description of a path.
        /// </summary>
        private static string DescribePath(List<Vector3> worldPath)
        {
            if (worldPath == null || worldPath.Count < 2)
                return "No movement needed";

            var segments = new List<string>();
            Vector3 currentDir = Vector3.zero;
            int stepCount = 0;

            for (int i = 1; i < worldPath.Count; i++)
            {
                Vector3 dir = worldPath[i] - worldPath[i - 1];
                dir.Normalize();

                if (Vector3.Distance(dir, currentDir) < 0.1f)
                {
                    stepCount++;
                }
                else
                {
                    if (stepCount > 0)
                    {
                        string dirName = GetCardinalDirectionName(currentDir);
                        segments.Add($"{dirName} {stepCount}");
                    }

                    currentDir = dir;
                    stepCount = 1;
                }
            }

            if (stepCount > 0)
            {
                string dirName = GetCardinalDirectionName(currentDir);
                segments.Add($"{dirName} {stepCount}");
            }

            return string.Join(", ", segments);
        }

        /// <summary>
        /// Gets the cardinal direction name from a direction vector.
        /// </summary>
        private static string GetCardinalDirectionName(Vector3 dir)
        {
            if (Mathf.Abs(dir.x) > 0.4f && Mathf.Abs(dir.y) > 0.4f)
            {
                if (dir.y > 0 && dir.x > 0) return "Northeast";
                if (dir.y > 0 && dir.x < 0) return "Northwest";
                if (dir.y < 0 && dir.x > 0) return "Southeast";
                if (dir.y < 0 && dir.x < 0) return "Southwest";
            }

            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
            {
                return dir.y > 0 ? "North" : "South";
            }
            else if (Mathf.Abs(dir.x) > 0.1f)
            {
                return dir.x > 0 ? "East" : "West";
            }

            return "Unknown";
        }

        /// <summary>
        /// Gets the cardinal/intercardinal direction from source to target.
        /// </summary>
        public static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions
            if (angle >= 337.5 || angle < 22.5) return "North";
            else if (angle >= 22.5 && angle < 67.5) return "Northeast";
            else if (angle >= 67.5 && angle < 112.5) return "East";
            else if (angle >= 112.5 && angle < 157.5) return "Southeast";
            else if (angle >= 157.5 && angle < 202.5) return "South";
            else if (angle >= 202.5 && angle < 247.5) return "Southwest";
            else if (angle >= 247.5 && angle < 292.5) return "West";
            else if (angle >= 292.5 && angle < 337.5) return "Northwest";
            else return "Unknown";
        }

        /// <summary>
        /// Calculates the distance between two positions in game units.
        /// </summary>
        public static float GetDistance(Vector3 from, Vector3 to)
        {
            return Vector3.Distance(from, to);
        }

        /// <summary>
        /// Converts game distance units to steps.
        /// One step = 16 game units.
        /// </summary>
        public static float DistanceToSteps(float distance)
        {
            return distance / 16f;
        }

        /// <summary>
        /// Gets a simple path description with direction and step count.
        /// </summary>
        public static string GetSimplePathDescription(Vector3 from, Vector3 to)
        {
            float distance = GetDistance(from, to);
            string direction = GetDirection(from, to);
            float steps = DistanceToSteps(distance);
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";

            return $"{steps:F0} {stepLabel} {direction}";
        }
    }
}
