using Il2CppLast.Management;
using Il2CppLast.Data.Master;
using MelonLoader;
using Map = Il2CppLast.Data.Master.Map;
using Area = Il2CppLast.Data.Master.Area;

namespace FFII_ScreenReader.Field
{
    /// <summary>
    /// Resolves map IDs to human-readable localized area names.
    /// Uses the game's MasterManager and MessageManager to convert map IDs
    /// to localized display names (e.g., "Altair 1F").
    /// Ported from FF3 screen reader.
    /// </summary>
    public static class MapNameResolver
    {
        /// <summary>
        /// Gets the name of the current map the player is on.
        /// </summary>
        /// <returns>Localized map name, or "Unknown" if unable to determine</returns>
        public static string GetCurrentMapName()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return "Unknown";

                int currentMapId = userDataManager.CurrentMapId;
                string resolvedName = TryResolveMapNameById(currentMapId);

                if (!string.IsNullOrEmpty(resolvedName))
                    return resolvedName;

                return $"Map {currentMapId}";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[MapNameResolver] Error getting current map name: {ex.Message}");
                return "Unknown";
            }
        }

        /// <summary>
        /// Gets a human-readable name for a map exit destination by its TmeId (map ID).
        /// </summary>
        /// <param name="mapId">The destination map ID from PropertyEntity.TmeId</param>
        /// <returns>Localized map name, or formatted map ID if resolution fails</returns>
        public static string GetMapExitName(int mapId)
        {
            if (mapId <= 0)
                return "";

            // Try to resolve using the MapId with Map and Area master data
            string resolvedName = TryResolveMapNameById(mapId);
            if (!string.IsNullOrEmpty(resolvedName))
                return resolvedName;

            // Fallback: Just show the map ID
            return $"Map {mapId}";
        }

        /// <summary>
        /// Attempts to resolve a map ID to a localized area name using Map and Area master data.
        /// </summary>
        /// <param name="mapId">The map ID</param>
        /// <returns>Localized area name with floor/title, or null if resolution fails</returns>
        public static string TryResolveMapNameById(int mapId)
        {
            try
            {
                // Get MasterManager instance
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                {
                    return null;
                }

                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                {
                    return null;
                }

                // Get the Map master data (contains AreaId and MapTitle)
                var mapList = masterManager.GetList<Map>();
                if (mapList == null || !mapList.ContainsKey(mapId))
                {
                    return null;
                }

                var map = mapList[mapId];
                if (map == null)
                {
                    return null;
                }

                // Get the area ID from the map
                int areaId = map.AreaId;

                // Get the Area master data
                var areaList = masterManager.GetList<Area>();
                if (areaList == null || !areaList.ContainsKey(areaId))
                {
                    return null;
                }

                var area = areaList[areaId];
                if (area == null)
                {
                    return null;
                }

                // Get localized area name (e.g., "Altair", "Fynn Castle")
                string areaNameKey = area.AreaName;
                string areaName = null;
                if (!string.IsNullOrEmpty(areaNameKey))
                {
                    areaName = messageManager.GetMessage(areaNameKey, false);
                }

                // Get localized map title (e.g., "1F", "B1")
                string mapTitleKey = map.MapTitle;
                string mapTitle = null;
                if (!string.IsNullOrEmpty(mapTitleKey) && mapTitleKey != "None")
                {
                    mapTitle = messageManager.GetMessage(mapTitleKey, false);
                }
                else
                {
                    // Use Floor field directly if MapTitle is not set
                    int floor = map.Floor;
                    if (floor > 0)
                    {
                        mapTitle = $"{floor}F";
                    }
                    else if (floor < 0)
                    {
                        mapTitle = $"B{-floor}";
                    }
                }

                // Combine area name and map title
                if (!string.IsNullOrEmpty(areaName) && !string.IsNullOrEmpty(mapTitle))
                {
                    return $"{areaName} {mapTitle}";
                }
                else if (!string.IsNullOrEmpty(areaName))
                {
                    return areaName;
                }

                return null;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[MapNameResolver] Error resolving map ID {mapId}: {ex.Message}");
                return null;
            }
        }
    }
}
