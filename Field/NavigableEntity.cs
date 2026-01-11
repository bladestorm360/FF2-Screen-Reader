using System;
using System.Collections.Generic;
using UnityEngine;
using FFII_ScreenReader.Core;

namespace FFII_ScreenReader.Field
{
    /// <summary>
    /// Base class for all navigable entities on the field map.
    /// Provides common properties and behavior for entity navigation and pathfinding.
    /// </summary>
    public abstract class NavigableEntity
    {
        /// <summary>
        /// Reference to the underlying game entity (FieldEntity)
        /// </summary>
        public virtual object GameEntity { get; set; }

        /// <summary>
        /// Current position in world coordinates
        /// </summary>
        public abstract Vector3 Position { get; }

        /// <summary>
        /// Entity name (localized if available)
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Category for filtering purposes
        /// </summary>
        public abstract EntityCategory Category { get; }

        /// <summary>
        /// Priority for deduplication (lower = more important)
        /// </summary>
        public abstract int Priority { get; }

        /// <summary>
        /// Whether this entity blocks pathfinding movement
        /// </summary>
        public abstract bool BlocksPathing { get; }

        /// <summary>
        /// Whether this entity is currently interactive
        /// </summary>
        public virtual bool IsInteractive => true;

        /// <summary>
        /// Gets the display name for this entity (without distance/direction)
        /// </summary>
        protected abstract string GetDisplayName();

        /// <summary>
        /// Gets the entity type name for this entity (e.g., "Treasure Chest", "NPC")
        /// </summary>
        protected abstract string GetEntityTypeName();

        /// <summary>
        /// Formats this entity for screen reader announcement
        /// </summary>
        public virtual string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = GetDirection(playerPos, Position);
            return $"{GetDisplayName()} ({FormatSteps(distance)} {direction}) - {GetEntityTypeName()}";
        }

        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another
        /// </summary>
        protected string GetDirection(Vector3 from, Vector3 to)
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
        /// Helper to format distance in steps
        /// </summary>
        protected string FormatSteps(float distance)
        {
            float steps = distance / 16f;
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";
            return $"{steps:F1} {stepLabel}";
        }
    }

    /// <summary>
    /// Represents a treasure chest entity
    /// </summary>
    public class TreasureChestEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private bool isOpened;

        public TreasureChestEntity(object gameEntity, Vector3 pos, string entityName, bool opened)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            isOpened = opened;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// Whether this treasure chest has been opened
        /// </summary>
        public bool IsOpened => isOpened;

        public override EntityCategory Category => EntityCategory.Chests;
        public override int Priority => 3;
        public override bool BlocksPathing => true;

        /// <summary>
        /// Opened chests are not interactive
        /// </summary>
        public override bool IsInteractive => !IsOpened;

        protected override string GetDisplayName()
        {
            string status = IsOpened ? "Opened" : "Unopened";
            return $"{status} {Name}";
        }

        protected override string GetEntityTypeName()
        {
            return "Treasure Chest";
        }
    }

    /// <summary>
    /// Represents an NPC entity
    /// </summary>
    public class NPCEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private string assetName;
        private bool isShop;

        public NPCEntity(object gameEntity, Vector3 pos, string entityName, string asset = "", bool shop = false)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            assetName = asset;
            isShop = shop;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// Asset name used by the game
        /// </summary>
        public string AssetName => assetName;

        /// <summary>
        /// Whether this NPC is a shop
        /// </summary>
        public bool IsShop => isShop;

        public override EntityCategory Category => EntityCategory.NPCs;
        public override int Priority => 4;
        public override bool BlocksPathing => true;

        protected override string GetDisplayName()
        {
            var details = new List<string>();

            if (IsShop)
            {
                details.Add("shop");
            }

            string detailStr = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";
            return $"{Name}{detailStr}";
        }

        protected override string GetEntityTypeName()
        {
            return "NPC";
        }
    }

    /// <summary>
    /// Represents a map exit/transition
    /// </summary>
    public class MapExitEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private int destinationMapId;
        private string destinationName;

        public MapExitEntity(object gameEntity, Vector3 pos, string entityName, int destMapId, string destName)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            destinationMapId = destMapId;
            destinationName = destName;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// Destination map ID
        /// </summary>
        public int DestinationMapId => destinationMapId;

        /// <summary>
        /// Friendly name of destination map
        /// </summary>
        public string DestinationName => destinationName;

        public override EntityCategory Category => EntityCategory.MapExits;
        public override int Priority => 1;
        public override bool BlocksPathing => true;

        protected override string GetDisplayName()
        {
            return !string.IsNullOrEmpty(DestinationName)
                ? $"{Name} → {DestinationName}"
                : Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Map Exit";
        }
    }

    /// <summary>
    /// Represents a save point
    /// </summary>
    public class SavePointEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;

        public SavePointEntity(object gameEntity, Vector3 pos, string entityName)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        public override EntityCategory Category => EntityCategory.Events;
        public override int Priority => 2;
        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Save Point";
        }
    }

    /// <summary>
    /// Represents a generic event (teleport, switch event, random event, etc.)
    /// </summary>
    public class EventEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private string eventTypeName;

        public EventEntity(object gameEntity, Vector3 pos, string entityName, string typeName = "Event")
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            eventTypeName = typeName;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        public override EntityCategory Category => EntityCategory.Events;
        public override int Priority => 8;
        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return eventTypeName;
        }
    }

    /// <summary>
    /// Represents a vehicle (airship, canoe, etc.)
    /// </summary>
    public class VehicleEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private int transportationId;

        public VehicleEntity(object gameEntity, Vector3 pos, string entityName, int transportId)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            transportationId = transportId;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// Transportation type ID
        /// </summary>
        public int TransportationId => transportationId;

        public override EntityCategory Category => EntityCategory.Vehicles;
        public override int Priority => 10;
        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return GetVehicleName(TransportationId);
        }

        protected override string GetEntityTypeName()
        {
            return "Vehicle";
        }

        public static string GetVehicleName(int id)
        {
            // FF2-specific vehicle names - adjust based on game
            switch (id)
            {
                case 1: return "Player";
                case 2: return "Canoe";
                case 3: return "Ship";
                case 4: return "Airship";
                default: return $"Vehicle {id}";
            }
        }
    }
}
