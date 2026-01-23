using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using FFII_ScreenReader.Utils;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

namespace FFII_ScreenReader.Field
{
    /// <summary>
    /// Shared context for filter evaluation containing player and map information.
    /// Passed to filters to avoid repeated lookups.
    /// Auto-populates from the current game state when constructed.
    /// Uses FieldPlayerController for direct access to mapHandle and fieldPlayer.
    /// </summary>
    public class FilterContext
    {
        /// <summary>
        /// Reference to the FieldPlayerController (provides mapHandle and fieldPlayer).
        /// </summary>
        public FieldPlayerController PlayerController { get; set; }

        /// <summary>
        /// Reference to the field player entity.
        /// </summary>
        public FieldPlayer FieldPlayer { get; set; }

        /// <summary>
        /// Handle to the current map for pathfinding.
        /// </summary>
        public IMapAccessor MapHandle { get; set; }

        /// <summary>
        /// Current player position (uses localPosition for pathfinding).
        /// </summary>
        public Vector3 PlayerPosition { get; set; }

        /// <summary>
        /// Default constructor that auto-populates from current game state.
        /// Uses FieldPlayerController for direct access to mapHandle and fieldPlayer.
        /// </summary>
        public FilterContext()
        {
            // Use FieldPlayerController - first try cache, then find it
            PlayerController = GameObjectCache.GetOrRefresh<FieldPlayerController>();

            if (PlayerController == null)
            {
                PlayerPosition = Vector3.zero;
                return;
            }

            // Get fieldPlayer and mapHandle directly from controller
            FieldPlayer = PlayerController.fieldPlayer;
            MapHandle = PlayerController.mapHandle;

            if (FieldPlayer?.transform != null)
            {
                // Use localPosition for pathfinding
                PlayerPosition = FieldPlayer.transform.localPosition;
            }
            else
            {
                PlayerPosition = Vector3.zero;
            }
        }
    }
}
