using System;
using System.Collections.Generic;
using UnityEngine;
using FFII_ScreenReader.Core;
using FFII_ScreenReader.Utils;
using FieldEntity = Il2CppLast.Entity.Field.FieldEntity;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;

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
        /// Whether the underlying game entity is still alive and active in the scene.
        /// Used by the delta scan to prune entities deactivated by events (opened chests'
        /// interaction collider, NPCs despawned mid-cutscene).
        /// </summary>
        public virtual bool IsAlive
        {
            get
            {
                if (GameEntity == null) return false;
                try
                {
                    var fe = GameEntity as FieldEntity;
                    if (fe == null) return false;
                    var go = fe.gameObject;
                    if (go == null) return false;
                    return go.activeInHierarchy;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Reads the live position from the backing FieldEntity, or returns the fallback.
        /// </summary>
        protected Vector3 ReadLivePosition(Vector3 fallback)
        {
            try
            {
                var fe = GameEntity as FieldEntity;
                if (fe?.transform != null)
                    return fe.transform.localPosition;
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Reads the live isOpen state from a backing FieldTresureBox.
        /// Tries reflection on the private isOpen field, falls back to IL2CPP pointer
        /// access at offset 0x159 (offset confirmed via EntityScanner.GetTreasureBoxOpenedState).
        /// </summary>
        protected internal static bool ReadTreasureOpened(object gameEntity, bool fallback)
        {
            if (gameEntity == null) return fallback;
            try
            {
                var fe = gameEntity as FieldEntity;
                if (fe == null) return fallback;
                var treasureBox = fe.TryCast<FieldTresureBox>();
                if (treasureBox == null) return fallback;

                var isOpenField = treasureBox.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (isOpenField != null)
                {
                    var value = isOpenField.GetValue(treasureBox);
                    if (value != null) return (bool)value;
                }

                unsafe
                {
                    IntPtr ptr = treasureBox.Pointer;
                    if (ptr != IntPtr.Zero)
                        return *(bool*)(ptr + 0x159);
                }
            }
            catch { }
            return fallback;
        }

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
            string direction = DirectionHelper.GetCardinalDirection(playerPos, Position);
            return $"{GetDisplayName()} ({DirectionHelper.FormatSteps(distance)} {direction}) - {GetEntityTypeName()}";
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
        /// Whether this treasure chest has been opened. Reads live from the game entity
        /// so the delta scan reflects state changes without recreating the wrapper.
        /// </summary>
        public bool IsOpened => ReadTreasureOpened(GameEntity, isOpened);

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

        public override Vector3 Position => ReadLivePosition(position);
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

        /// <summary>
        /// The event type name (e.g., "Event", "ToLayer", "Warp Tile")
        /// </summary>
        public string EventTypeName => eventTypeName;

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

        public override Vector3 Position => ReadLivePosition(position);
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
