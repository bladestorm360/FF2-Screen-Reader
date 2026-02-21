using System;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Field;
using FFII_ScreenReader.Utils;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

namespace FFII_ScreenReader.Core
{
    /// <summary>
    /// Handles waypoint operations: cycling, pathfinding, add/rename/remove.
    /// Extracted from FFII_ScreenReaderMod to reduce file size.
    /// </summary>
    internal class WaypointController
    {
        private readonly FFII_ScreenReaderMod mod;
        private readonly WaypointManager waypointManager;
        private readonly WaypointNavigator waypointNavigator;

        public WaypointController(FFII_ScreenReaderMod mod, WaypointManager manager, WaypointNavigator navigator)
        {
            this.mod = mod;
            this.waypointManager = manager;
            this.waypointNavigator = navigator;
        }

        public void CycleNext()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFII_ScreenReaderMod.SpeakText("No waypoints on this map");
                return;
            }

            waypointNavigator.CycleNext();
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CyclePrevious()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFII_ScreenReaderMod.SpeakText("No waypoints on this map");
                return;
            }

            waypointNavigator.CyclePrevious();
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CycleNextCategory()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.CycleNextCategory(mapId);
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void CyclePreviousCategory()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.CyclePreviousCategory(mapId);
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void Pathfind()
        {
            if (!mod.EnsureFieldContext())
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFII_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            try
            {
                var playerController = GameObjectCache.GetOrRefresh<FieldPlayerController>();
                if (playerController?.fieldPlayer == null || playerController.mapHandle == null)
                {
                    FFII_ScreenReaderMod.SpeakText("Unable to pathfind");
                    return;
                }

                Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
                Vector3 targetPos = waypoint.Position;

                var pathInfo = FieldNavigationHelper.FindPathTo(
                    playerPos,
                    targetPos,
                    playerController.mapHandle,
                    playerController.fieldPlayer
                );

                if (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
                {
                    FFII_ScreenReaderMod.SpeakText(pathInfo.Description);
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText($"No path to {waypoint.Name}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Waypoint] Pathfind error: {ex.Message}");
                FFII_ScreenReaderMod.SpeakText("Pathfinding failed");
            }
        }

        public void Add()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();

            TextInputWindow.Open("Enter waypoint name", "", (name) =>
            {
                try
                {
                    var playerController = GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null)
                    {
                        FFII_ScreenReaderMod.SpeakText("Unable to get player position");
                        return;
                    }

                    Vector3 position = playerController.fieldPlayer.transform.localPosition;
                    waypointManager.AddWaypoint(name, position, mapId);
                    waypointNavigator.RefreshList(mapId);

                    FFII_ScreenReaderMod.SpeakText($"Waypoint added: {name}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Waypoint] Add error: {ex.Message}");
                    FFII_ScreenReaderMod.SpeakText("Failed to add waypoint");
                }
            });
        }

        public void Rename()
        {
            if (!mod.EnsureFieldContext())
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFII_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            TextInputWindow.Open("Enter new waypoint name", waypoint.Name, (newName) =>
            {
                string mapId = mod.GetCurrentMapIdString();
                bool success = waypointManager.RenameWaypoint(waypoint.WaypointId, newName);

                if (success)
                {
                    waypointNavigator.RefreshList(mapId);
                    FFII_ScreenReaderMod.SpeakText($"Waypoint renamed to: {newName}");
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText("Failed to rename waypoint");
                }
            });
        }

        public void Delete()
        {
            if (!mod.EnsureFieldContext())
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFII_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            string waypointName = waypoint.Name;
            string waypointId = waypoint.WaypointId;

            ConfirmationDialog.Open($"Delete waypoint {waypointName}?", () =>
            {
                string mapId = mod.GetCurrentMapIdString();
                bool success = waypointManager.RemoveWaypoint(waypointId);

                if (success)
                {
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();
                    FFII_ScreenReaderMod.SpeakText($"Waypoint deleted: {waypointName}");
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText("Failed to delete waypoint");
                }
            });
        }

        public void ClearAll()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            int count = waypointManager.GetWaypointCountForMap(mapId);

            if (count == 0)
            {
                FFII_ScreenReaderMod.SpeakText("No waypoints to clear on this map");
                return;
            }

            string plural = count == 1 ? "waypoint" : "waypoints";

            // First confirmation (silent Yes - proceeds directly to second prompt without "Yes" announcement)
            ConfirmationDialog.Open($"Clear all {count} {plural} from this map?", () =>
            {
                // Second confirmation (normal with speech)
                ConfirmationDialog.Open("Are you sure?", () =>
                {
                    int cleared = waypointManager.ClearMapWaypoints(mapId);
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();

                    string clearedPlural = cleared == 1 ? "waypoint" : "waypoints";
                    FFII_ScreenReaderMod.SpeakText($"Cleared {cleared} {clearedPlural}");
                });
            }, silentYes: true);
        }
    }
}
