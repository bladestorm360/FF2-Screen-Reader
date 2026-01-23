using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using Il2CppLast.Management;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for playing sound effects when player hits a wall/obstacle.
    /// Uses FieldController.OnPlayerHitCollider which fires on collision events (event-driven, not polling).
    /// </summary>
    [HarmonyPatch]
    public static class MovementSoundPatches
    {
        // Sound ID for wall bump - common collision/error sound
        private static readonly int BUMP_SOUND_ID = 4;

        // Cooldown to prevent sound spam when holding direction against a wall
        private static float lastBumpTime = 0f;
        private const float BUMP_COOLDOWN = 0.3f; // 300ms

        private static bool hasLoggedPatchActive = false;

        // Track positions to detect real wall collisions vs false positives
        private static Vector3 lastCollisionPos = Vector3.zero;
        private static int samePositionCount = 0;
        private const int REQUIRED_CONSECUTIVE_HITS = 2; // Need 2+ hits at same spot to confirm wall
        private const float POSITION_TOLERANCE = 1.0f; // Positions within 1 unit considered "same"

        /// <summary>
        /// Fires when the player collides with an obstacle.
        /// Event-driven approach - only called when collision actually occurs.
        ///
        /// FF2-specific: The game fires this event even during successful movement (false positives).
        /// Real wall collisions are identified by consecutive hits at the same position.
        /// </summary>
        [HarmonyPatch(typeof(FieldController), nameof(FieldController.OnPlayerHitCollider))]
        [HarmonyPostfix]
        private static void OnPlayerHitCollider_Postfix(FieldPlayer playerEntity)
        {
            try
            {
                // Log once to confirm patch is working
                if (!hasLoggedPatchActive)
                {
                    MelonLogger.Msg("[WallBump] Event-driven patch active - consecutive hit detection enabled");
                    hasLoggedPatchActive = true;
                }

                if (playerEntity == null)
                    return;

                // Get current position
                Vector3 currentPos = playerEntity.transform.position;

                // Check if position is same as last collision (within tolerance)
                float distFromLast = Vector3.Distance(currentPos, lastCollisionPos);

                if (distFromLast < POSITION_TOLERANCE)
                {
                    // Same position - increment consecutive hit counter
                    samePositionCount++;
                }
                else
                {
                    // Different position - reset counter, this is likely a false positive
                    samePositionCount = 1;
                    lastCollisionPos = currentPos;
                    return; // Don't play sound for single hit at new position
                }

                // Only consider playing sound after confirmed consecutive hits
                if (samePositionCount < REQUIRED_CONSECUTIVE_HITS)
                    return;

                // Apply cooldown to prevent sound spam while held against wall
                float currentTime = Time.time;
                if (currentTime - lastBumpTime < BUMP_COOLDOWN)
                    return;

                lastBumpTime = currentTime;
                PlayBumpSound();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in OnPlayerHitCollider_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays the wall bump sound effect
        /// </summary>
        private static void PlayBumpSound()
        {
            try
            {
                var audioManager = AudioManager.Instance;
                if (audioManager != null)
                {
                    audioManager.PlaySe(BUMP_SOUND_ID);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error playing bump sound: {ex.Message}");
            }
        }
    }
}
