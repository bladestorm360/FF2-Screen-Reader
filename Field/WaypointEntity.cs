using System;
using UnityEngine;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Field
{
    /// <summary>
    /// Categories for waypoints to allow filtering
    /// </summary>
    public enum WaypointCategory
    {
        All = 0,           // Filter only - shows all waypoints
        Docks = 1,         // Ship/boat docking locations
        Landmarks = 2,     // Towns, dungeons, notable locations
        AirshipLandings = 3, // Airship landing zones
        Miscellaneous = 4  // Default category for new waypoints
    }

    /// <summary>
    /// Represents a user-defined waypoint for navigation.
    /// Standalone class - not part of the entity navigation system.
    /// Waypoints have their own separate category system.
    /// </summary>
    public class WaypointEntity
    {
        private readonly string waypointId;
        private readonly string waypointName;
        private readonly Vector3 position;
        private readonly WaypointCategory waypointCategory;
        private readonly string mapId;
        private readonly int layer;

        public string WaypointId => waypointId;
        public string WaypointName => waypointName;
        public WaypointCategory WaypointCategoryType => waypointCategory;
        public string MapId => mapId;

        public Vector3 Position => position;

        /// <summary>
        /// Unity gameObject.layer captured from the player when the waypoint was created, used for
        /// layer-aware pathfinding. -1 = unknown (legacy waypoints) → pathfinding falls back to the
        /// brute-force destination-Z search.
        /// </summary>
        public int Layer => layer;

        public string Name => waypointName;

        public WaypointEntity(string id, string name, Vector3 pos, string mapId, WaypointCategory category, int layer = -1)
        {
            this.waypointId = id;
            this.waypointName = name;
            this.position = pos;
            this.mapId = mapId;
            this.waypointCategory = category;
            this.layer = layer;
        }

        /// <summary>
        /// Gets a display-friendly name for the waypoint category
        /// </summary>
        public static string GetCategoryDisplayName(WaypointCategory category)
        {
            switch (category)
            {
                case WaypointCategory.Docks:
                    return "Dock";
                case WaypointCategory.Landmarks:
                    return "Landmark";
                case WaypointCategory.AirshipLandings:
                    return "Airship Landing";
                case WaypointCategory.Miscellaneous:
                    return "Waypoint";
                default:
                    return "Waypoint";
            }
        }

        /// <summary>
        /// Gets the category names for cycling announcements
        /// </summary>
        public static string[] GetCategoryNames()
        {
            return new string[] { "All", "Docks", "Landmarks", "Airship Landings", "Miscellaneous" };
        }

        /// <summary>
        /// Formats this waypoint for screen reader announcement
        /// </summary>
        public string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = DirectionHelper.GetCardinalDirection(playerPos, Position);
            string categoryName = GetCategoryDisplayName(waypointCategory);
            return $"{waypointName} ({categoryName}) ({DirectionHelper.FormatSteps(distance)} {direction})";
        }

    }
}
