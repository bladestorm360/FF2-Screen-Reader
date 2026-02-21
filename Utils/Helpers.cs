using System;
using System.Collections.Generic;
using UnityEngine;

// Type aliases for IL2CPP types
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized player position retrieval.
    /// </summary>
    public static class PlayerPositionHelper
    {
        /// <summary>
        /// Gets the player's world position (transform.position).
        /// Used by EntityNavigator, EntityCache, GroupEntity for distance sorting.
        /// </summary>
        public static Vector3 GetWorldPosition()
        {
            var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer?.transform == null)
                return Vector3.zero;

            return playerController.fieldPlayer.transform.position;
        }

        /// <summary>
        /// Gets the player's local position (transform.localPosition).
        /// Used by WaypointNavigator for waypoint coordinate space.
        /// </summary>
        public static Vector3 GetLocalPosition()
        {
            var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer?.transform == null)
                return Vector3.zero;

            return playerController.fieldPlayer.transform.localPosition;
        }
    }

    /// <summary>
    /// Centralized utility for character-related lookups.
    /// </summary>
    public static class CharacterUtility
    {
        /// <summary>
        /// Gets the character's row position (Front Row / Back Row) from Corps data.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                    return null;

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null && corps.CharacterId == characterId)
                    {
                        return corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                    }
                }
            }
            catch { }

            return null;
        }
    }

    /// <summary>
    /// Cardinal/intercardinal direction and distance formatting utilities.
    /// </summary>
    public static class DirectionHelper
    {
        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another.
        /// </summary>
        public static string GetCardinalDirection(Vector3 from, Vector3 to)
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
        /// Formats distance in steps (1 step = 16 units).
        /// </summary>
        public static string FormatSteps(float distance)
        {
            float steps = distance / 16f;
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";
            return $"{steps:F1} {stepLabel}";
        }
    }

    /// <summary>
    /// Shared sorting utilities for entity and waypoint lists.
    /// </summary>
    public static class CollectionHelper
    {
        /// <summary>
        /// Returns a new list sorted by distance from a reference position.
        /// </summary>
        public static List<T> SortByDistance<T>(List<T> items, Vector3 referencePos, Func<T, Vector3> getPosition)
        {
            var withDistances = new List<(T item, float distance)>(items.Count);
            foreach (var item in items)
            {
                withDistances.Add((item, Vector3.Distance(getPosition(item), referencePos)));
            }

            withDistances.Sort((a, b) => a.distance.CompareTo(b.distance));

            var result = new List<T>(withDistances.Count);
            foreach (var entry in withDistances)
            {
                result.Add(entry.item);
            }
            return result;
        }

        /// <summary>
        /// Sorts a list in-place by distance from a reference position.
        /// Returns the new index of the preserveItem, or -1 if not found.
        /// </summary>
        public static int SortByDistanceInPlace<T>(List<T> items, Vector3 referencePos, Func<T, Vector3> getPosition, T preserveItem)
        {
            var withDistances = new List<(T item, float distance)>(items.Count);
            foreach (var item in items)
            {
                withDistances.Add((item, Vector3.Distance(getPosition(item), referencePos)));
            }

            withDistances.Sort((a, b) => a.distance.CompareTo(b.distance));

            int preservedIndex = -1;
            for (int i = 0; i < withDistances.Count; i++)
            {
                items[i] = withDistances[i].item;
                if (preserveItem != null && EqualityComparer<T>.Default.Equals(withDistances[i].item, preserveItem))
                {
                    preservedIndex = i;
                }
            }

            return preservedIndex;
        }
    }
}
