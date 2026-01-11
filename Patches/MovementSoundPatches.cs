using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using Il2CppLast.Management;
using FFII_ScreenReader.Utils;

namespace FFII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for playing sound effects during player movement (wall bumps).
    /// Ported from FF5/FF3 mod.
    /// </summary>
    [HarmonyPatch]
    public static class MovementSoundPatches
    {
        // Cooldown to prevent sound spam when holding a direction key against a wall
        private static float lastBumpTime = 0f;
        private static readonly float BUMP_COOLDOWN = 0.2f; // 200ms between bump sounds

        // Sound ID for wall bump - common collision/error sound
        private static readonly int BUMP_SOUND_ID = 4;

        private static bool hasLoggedPatchActive = false;

        /// <summary>
        /// Prefix patch to capture player position and check after a frame
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

                // Check if we're on cooldown
                float currentTime = Time.time;
                if (currentTime - lastBumpTime < BUMP_COOLDOWN)
                    return;

                // Access fieldPlayer directly - IL2CppInterop exposes protected fields
                if (__instance?.fieldPlayer?.transform == null)
                    return;

                // Store current position and start coroutine to check after a frame
                Vector3 positionBeforeMovement = __instance.fieldPlayer.transform.localPosition;
                CoroutineManager.StartManaged(CheckForWallBumpAfterFrame(__instance.fieldPlayer, positionBeforeMovement));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in MovementSoundPatches OnTouchPadCallback_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that waits one frame then checks if position changed
        /// </summary>
        private static IEnumerator CheckForWallBumpAfterFrame(FieldPlayer player, Vector3 positionBefore)
        {
            // Wait one frame for movement to be processed
            yield return null;

            try
            {
                // Check if player still exists
                if (player == null || player.transform == null)
                    yield break;

                // Get position after movement was processed
                Vector3 positionAfter = player.transform.localPosition;

                // Calculate distance moved
                float distanceMoved = Vector3.Distance(positionBefore, positionAfter);

                // If position didn't change (within small threshold), player hit a wall
                if (distanceMoved < 0.1f)
                {
                    MelonLogger.Msg($"[WallBump] Wall bump detected! Distance moved: {distanceMoved:F3}");
                    PlayBumpSound();
                    lastBumpTime = Time.time;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in CheckForWallBumpAfterFrame: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the axis input represents actual movement input
        /// </summary>
        private static bool HasMovementInput(Vector2 axis)
        {
            // Check if there's any significant input on either axis
            const float inputThreshold = 0.1f;
            return Mathf.Abs(axis.x) > inputThreshold || Mathf.Abs(axis.y) > inputThreshold;
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
