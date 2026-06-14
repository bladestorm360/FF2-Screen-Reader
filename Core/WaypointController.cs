using System;
using MelonLoader;
using UnityEngine;
using FFII_ScreenReader.Field;
using FFII_ScreenReader.Utils;
using static FFII_ScreenReader.Utils.ModTextTranslator;
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
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            NavigationTargetTracker.MarkWaypoint();

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFII_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            waypointNavigator.CycleNext();
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CyclePrevious()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            NavigationTargetTracker.MarkWaypoint();

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFII_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            waypointNavigator.CyclePrevious();
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CycleNextCategory()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            NavigationTargetTracker.MarkWaypoint();

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.CycleNextCategory(mapId);
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void CyclePreviousCategory()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            NavigationTargetTracker.MarkWaypoint();

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.CyclePreviousCategory(mapId);
            FFII_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void Pathfind()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            NavigationTargetTracker.MarkWaypoint();

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFII_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            // Beacon mode: re-ping toward this waypoint instead of turn-by-turn directions.
            if (PreferencesManager.AudioBeaconsEnabled)
            {
                mod.RestartBeacon();
                if (FFII_ScreenReaderMod.AnnounceOnBeaconRestartEnabled)
                    FFII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
                return;
            }

            try
            {
                var playerController = GameObjectCache.GetOrRefresh<FieldPlayerController>();
                if (playerController?.fieldPlayer == null || playerController.mapHandle == null)
                {
                    FFII_ScreenReaderMod.SpeakText(T("Unable to pathfind"));
                    return;
                }

                Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
                Vector3 targetPos = waypoint.Position;
                int? targetLayer = waypoint.Layer >= 0 ? waypoint.Layer : (int?)null;

                var pathInfo = FieldNavigationHelper.FindPathTo(
                    playerPos,
                    targetPos,
                    playerController.mapHandle,
                    playerController.fieldPlayer,
                    targetLayer
                );

                if (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
                {
                    FFII_ScreenReaderMod.SpeakText(pathInfo.Description);
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("No path to {0}"), waypoint.Name));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Waypoint] Pathfind error: {ex.Message}");
                FFII_ScreenReaderMod.SpeakText(T("Pathfinding failed"));
            }
        }

        public void Add()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            string mapId = mod.GetCurrentMapIdString();

            TextInputWindow.Open(T("Enter waypoint name"), "", (name) =>
            {
                try
                {
                    var playerController = GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null)
                    {
                        FFII_ScreenReaderMod.SpeakText(T("Unable to get player position"));
                        return;
                    }

                    Vector3 position = playerController.fieldPlayer.transform.localPosition;
                    int layer = playerController.fieldPlayer.gameObject.layer;
                    waypointManager.AddWaypoint(name, position, mapId, layer: layer);
                    waypointNavigator.RefreshList(mapId);

                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Waypoint added: {0}"), name));
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Waypoint] Add error: {ex.Message}");
                    FFII_ScreenReaderMod.SpeakText(T("Failed to add waypoint"));
                }
            });
        }

        public void Rename()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFII_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            TextInputWindow.Open(T("Enter new waypoint name"), waypoint.Name, (newName) =>
            {
                string mapId = mod.GetCurrentMapIdString();
                bool success = waypointManager.RenameWaypoint(waypoint.WaypointId, newName);

                if (success)
                {
                    waypointNavigator.RefreshList(mapId);
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Waypoint renamed to: {0}"), newName));
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText(T("Failed to rename waypoint"));
                }
            });
        }

        public void Delete()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFII_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            string waypointName = waypoint.Name;
            string waypointId = waypoint.WaypointId;

            ConfirmationDialog.Open(string.Format(T("Delete waypoint {0}?"), waypointName), () =>
            {
                string mapId = mod.GetCurrentMapIdString();
                bool success = waypointManager.RemoveWaypoint(waypointId);

                if (success)
                {
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();
                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Waypoint deleted: {0}"), waypointName));
                }
                else
                {
                    FFII_ScreenReaderMod.SpeakText(T("Failed to delete waypoint"));
                }
            });
        }

        public void ClearAll()
        {
            if (!mod.EnsureFieldContext(speakIfMissing: false))
                return;

            string mapId = mod.GetCurrentMapIdString();
            int count = waypointManager.GetWaypointCountForMap(mapId);

            if (count == 0)
            {
                FFII_ScreenReaderMod.SpeakText(T("No waypoints to clear on this map"));
                return;
            }

            string plural = count == 1 ? T("waypoint") : T("waypoints");

            ConfirmationDialog.Open(
                string.Format(T("Clear all {0} {1} from this map?"), count, plural),
                onYes: () =>
                {
                    int cleared = waypointManager.ClearMapWaypoints(mapId);
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();

                    FFII_ScreenReaderMod.SpeakText(string.Format(T("Cleared {0} {1}"), cleared, plural));
                },
                onNo: () => { });
        }
    }
}
