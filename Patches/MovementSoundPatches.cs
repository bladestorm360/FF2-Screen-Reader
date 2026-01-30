using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using FFII_ScreenReader.Utils;
using FFII_ScreenReader.Core;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for playing sound effects during player movement (wall bumps, footsteps).
    /// Uses coroutine-based approach: captures position before movement, checks after 0.08s.
    /// Wall bumps use SoundPlayer.PlayWallBump() (procedural tone via waveOut API).
    /// Footsteps use SoundPlayer.PlayFootstep() (gated by IsFootstepsEnabled).
    /// </summary>
    [HarmonyPatch]
    public static class MovementSoundPatches
    {
        // Cooldown to prevent sound spam when holding a direction key against a wall
        private static float lastBumpTime = 0f;
        private static readonly float BUMP_COOLDOWN = 0.3f; // 300ms between bump sounds

        private static bool hasLoggedPatchActive = false;

        // Track consecutive failed moves to detect real walls vs false positives
        private static Vector3 lastCollisionPos = Vector3.zero;
        private static int samePositionCount = 0;
        private const int REQUIRED_CONSECUTIVE_HITS = 2; // Need 2+ hits at same spot
        private const float POSITION_TOLERANCE = 1.0f; // Positions within 1 unit considered "same"

        // Prevent multiple wall-check coroutines from stacking up
        private static bool wallCheckPending = false;

        // Audio feedback cooldowns
        private const float TILE_SIZE = 16f;
        private static float lastFootstepTime = 0f;
        private const float FOOTSTEP_COOLDOWN = 0.15f;

        // Tile position tracking for footsteps
        private static Vector2Int lastTilePosition = Vector2Int.zero;
        private static bool tileTrackingInitialized = false;

        /// <summary>
        /// Converts world position to tile coordinates.
        /// </summary>
        private static Vector2Int GetTilePosition(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / TILE_SIZE),
                Mathf.FloorToInt(worldPos.y / TILE_SIZE)
            );
        }

        /// <summary>
        /// Prefix patch to capture player position and check after a frame.
        /// </summary>
        [HarmonyPatch(typeof(FieldPlayerKeyController), nameof(FieldPlayerKeyController.OnTouchPadCallback))]
        [HarmonyPrefix]
        private static void OnTouchPadCallback_Prefix(FieldPlayerKeyController __instance, Vector2 axis)
        {
            try
            {
                // Log once to confirm patch is working
                if (!hasLoggedPatchActive)
                {
                    MelonLogger.Msg("[WallBump] Patch is active - OnTouchPadCallback intercepted");
                    hasLoggedPatchActive = true;
                }

                // Only check if there's actual movement input
                if (!HasMovementInput(axis))
                    return;

                // Access fieldPlayer directly - IL2CppInterop exposes protected fields
                if (__instance?.fieldPlayer?.transform == null)
                    return;

                // Only start a new coroutine if one isn't already pending
                if (!wallCheckPending)
                {
                    wallCheckPending = true;
                    Vector3 positionBeforeMovement = __instance.fieldPlayer.transform.localPosition;
                    CoroutineManager.StartManaged(CheckForWallBumpAfterFrame(__instance.fieldPlayer, positionBeforeMovement));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in MovementSoundPatches OnTouchPadCallback_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that waits for movement animation to complete then checks position.
        /// Movement takes ~0.067s per tile at 15 tiles/sec, so we wait 0.08s.
        /// </summary>
        private static IEnumerator CheckForWallBumpAfterFrame(FieldPlayer player, Vector3 positionBefore)
        {
            // Wait for movement animation to complete (movement takes ~0.067s per tile)
            yield return new WaitForSeconds(0.08f);

            try
            {
                // Check if player still exists
                if (player == null || player.transform == null)
                {
                    wallCheckPending = false;
                    yield break;
                }

                // Get position after movement was processed
                Vector3 positionAfter = player.transform.localPosition;

                // Calculate distance moved
                float distanceMoved = Vector3.Distance(positionBefore, positionAfter);

                // Check tile position change for footsteps
                Vector2Int currentTile = GetTilePosition(positionAfter);

                // Initialize tile tracking if needed
                if (!tileTrackingInitialized)
                {
                    lastTilePosition = currentTile;
                    tileTrackingInitialized = true;
                }

                // If position didn't change (within small threshold), player hit a wall
                if (distanceMoved < 0.1f)
                {
                    // Check if position is same as last collision
                    float distFromLast = Vector3.Distance(positionBefore, lastCollisionPos);

                    if (distFromLast < POSITION_TOLERANCE)
                    {
                        samePositionCount++;
                    }
                    else
                    {
                        // New position - reset counter
                        samePositionCount = 1;
                        lastCollisionPos = positionBefore;
                    }

                    // Only play sound after confirmed consecutive hits
                    if (samePositionCount < REQUIRED_CONSECUTIVE_HITS)
                    {
                        wallCheckPending = false;
                        yield break;
                    }

                    // Check cooldown INSIDE coroutine (after the wait)
                    float currentTime = Time.time;
                    if (currentTime - lastBumpTime < BUMP_COOLDOWN)
                    {
                        wallCheckPending = false;
                        yield break;
                    }

                    SoundPlayer.PlayWallBump();
                    lastBumpTime = currentTime;
                }
                else
                {
                    // Player successfully moved - reset collision counter
                    samePositionCount = 0;

                    if (currentTile != lastTilePosition)
                    {
                        // Tile changed - play footstep if enabled
                        lastTilePosition = currentTile;

                        if (FFII_ScreenReaderMod.Instance != null && FFII_ScreenReaderMod.Instance.IsFootstepsEnabled())
                        {
                            float currentTime = Time.time;
                            if (currentTime - lastFootstepTime >= FOOTSTEP_COOLDOWN)
                            {
                                SoundPlayer.PlayFootstep();
                                lastFootstepTime = currentTime;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in CheckForWallBumpAfterFrame: {ex.Message}");
            }
            finally
            {
                // Always reset pending flag so next coroutine can start
                wallCheckPending = false;
            }
        }

        /// <summary>
        /// Checks if the axis input represents actual movement input.
        /// </summary>
        private static bool HasMovementInput(Vector2 axis)
        {
            const float inputThreshold = 0.1f;
            return Mathf.Abs(axis.x) > inputThreshold || Mathf.Abs(axis.y) > inputThreshold;
        }

        /// <summary>
        /// Resets all static state. Called on map transitions to prevent stale
        /// collision/footstep data from the previous map.
        /// </summary>
        public static void ResetState()
        {
            lastBumpTime = 0f;
            lastCollisionPos = Vector3.zero;
            samePositionCount = 0;
            wallCheckPending = false;
            lastFootstepTime = 0f;
            lastTilePosition = Vector2Int.zero;
            tileTrackingInitialized = false;
        }
    }
}
