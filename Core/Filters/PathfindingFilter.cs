using FFII_ScreenReader.Field;
using UnityEngine;
using Il2CppLast.Entity.Field;

namespace FFII_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters entities by whether they have a valid path from the player.
    /// This is an expensive filter as it runs pathfinding for each entity.
    /// </summary>
    public class PathfindingFilter : IEntityFilter
    {
        private bool isEnabled = false;

        /// <summary>
        /// Whether this filter is enabled.
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (value != isEnabled)
                {
                    isEnabled = value;
                    if (value)
                        OnEnabled();
                    else
                        OnDisabled();
                }
            }
        }

        /// <summary>
        /// Display name for this filter.
        /// </summary>
        public string Name => "Pathfinding Filter";

        /// <summary>
        /// Pathfinding filter runs at cycle time since paths change as player/entities move.
        /// </summary>
        public FilterTiming Timing => FilterTiming.OnCycle;

        /// <summary>
        /// Checks if an entity has a valid path from the player.
        /// </summary>
        public bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            if (!IsEntityValid(entity))
                return false;

            if (context.PlayerController == null)
                return false;

            // Use localPosition for pathfinding
            Vector3 playerPos = context.PlayerPosition;
            Vector3 targetPos = entity.Position;

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                context.MapHandle,
                context.FieldPlayer
            );

            return pathInfo.Success;
        }

        /// <summary>
        /// Called when filter is enabled.
        /// </summary>
        public void OnEnabled()
        {
            // No initialization needed
        }

        /// <summary>
        /// Called when filter is disabled.
        /// </summary>
        public void OnDisabled()
        {
            // No cleanup needed
        }

        /// <summary>
        /// Validates that a NavigableEntity is still active and accessible.
        /// </summary>
        private bool IsEntityValid(NavigableEntity entity)
        {
            if (entity?.GameEntity == null)
                return false;

            try
            {
                // Cast to FieldEntity to access Unity properties
                var fieldEntity = entity.GameEntity as FieldEntity;
                if (fieldEntity == null)
                    return false;

                // Check if the GameObject is still active in the hierarchy
                if (fieldEntity.gameObject == null || !fieldEntity.gameObject.activeInHierarchy)
                    return false;

                // Check if the transform is still valid
                if (fieldEntity.transform == null)
                    return false;

                return true;
            }
            catch
            {
                // Entity has been destroyed or is otherwise invalid
                return false;
            }
        }
    }
}
