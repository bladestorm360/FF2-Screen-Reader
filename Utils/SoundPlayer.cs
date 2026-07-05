using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// High-level sound playback facade.
    /// Delegates output/mixing to AudioEngine (SDL3) and tone synthesis to ToneGenerator.
    /// </summary>
    public static class SoundPlayer
    {
        #region Pre-cached Sounds

        private static byte[] wallBumpWav;
        private static byte[] footstepWav;

        // Sustain wall tones (one per direction, for looping). Generated at REFERENCE
        // amplitude (BASE_VOLUME × direction multiplier, pan baked in). User volume and
        // clipping headroom are applied at play time via per-stream gain; SDL sums the
        // active direction streams (no manual pre-mix).
        private static byte[] wallToneNorthSustain;
        private static byte[] wallToneSouthSustain;
        private static byte[] wallToneEastSustain;
        private static byte[] wallToneWestSustain;

        #endregion

        // Track current wall tone directions as a bitmask to detect newly-on / newly-off
        // directions across the ~100ms audio loop ticks.
        private static int currentWallDirectionsMask = 0;
        private static int lastWallToneVolume = 50;

        /// <summary>
        /// Cardinal direction enum for wall tones.
        /// </summary>
        public enum Direction
        {
            North,
            South,
            East,
            West
        }

        /// <summary>
        /// Initializes the audio engine and pre-generates all cached tones.
        /// Call this once during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            AudioEngine.Initialize();

            // Wall bump: deep thud with soft attack
            wallBumpWav = ToneGenerator.MonoToStereo(
                ToneGenerator.GenerateThudTone(
                    SoundConstants.WallBump.FREQUENCY,
                    SoundConstants.WallBump.DURATION_MS,
                    SoundConstants.WallBump.VOLUME));

            // Footstep: light click
            footstepWav = ToneGenerator.MonoToStereo(
                ToneGenerator.GenerateClickTone(
                    SoundConstants.Footstep.FREQUENCY,
                    SoundConstants.Footstep.DURATION_MS,
                    SoundConstants.Footstep.VOLUME));

            float bv = SoundConstants.WallToneVolumeMultipliers.BASE_VOLUME;
            int sustain = SoundConstants.WallToneTiming.SUSTAIN_DURATION_MS;

            // Sustain tones (no decay, cycle-aligned for seamless looping) at reference amplitude.
            wallToneNorthSustain = ToneGenerator.GenerateStereoTone(SoundConstants.WallToneFrequencies.NORTH, sustain, bv * SoundConstants.WallToneVolumeMultipliers.NORTH, SoundConstants.WallTonePan.NORTH, sustain: true);
            wallToneSouthSustain = ToneGenerator.GenerateStereoTone(SoundConstants.WallToneFrequencies.SOUTH, sustain, bv * SoundConstants.WallToneVolumeMultipliers.SOUTH, SoundConstants.WallTonePan.SOUTH, sustain: true);
            wallToneEastSustain  = ToneGenerator.GenerateStereoTone(SoundConstants.WallToneFrequencies.EAST,  sustain, bv * SoundConstants.WallToneVolumeMultipliers.EAST,  SoundConstants.WallTonePan.EAST,  sustain: true);
            wallToneWestSustain  = ToneGenerator.GenerateStereoTone(SoundConstants.WallToneFrequencies.WEST,  sustain, bv * SoundConstants.WallToneVolumeMultipliers.WEST,  SoundConstants.WallTonePan.WEST,  sustain: true);
        }

        /// <summary>
        /// Shuts down the audio engine and clears cached state.
        /// </summary>
        public static void Shutdown()
        {
            AudioEngine.Shutdown();
            currentWallDirectionsMask = 0;
            lastWallToneVolume = 50;
        }

        #region Public Playback Methods

        /// <summary>
        /// Plays the wall bump sound effect on the WallBump stream.
        /// </summary>
        public static void PlayWallBump()
        {
            if (wallBumpWav == null) return;
            int len = wallBumpWav.Length - SoundConstants.WAV_HEADER_SIZE;
            if (len <= 0) return;
            float gain = FFII_ScreenReader.Core.PreferencesManager.WallBumpVolume / 50.0f;
            AudioEngine.PlayOneShot(AudioEngine.Stream.WallBump, wallBumpWav, SoundConstants.WAV_HEADER_SIZE, len, gain);
        }

        /// <summary>
        /// Plays the footstep click sound on the Footstep stream.
        /// </summary>
        public static void PlayFootstep()
        {
            if (footstepWav == null) return;
            int len = footstepWav.Length - SoundConstants.WAV_HEADER_SIZE;
            if (len <= 0) return;
            float gain = FFII_ScreenReader.Core.PreferencesManager.FootstepVolume / 50.0f;
            AudioEngine.PlayOneShot(AudioEngine.Stream.Footstep, footstepWav, SoundConstants.WAV_HEADER_SIZE, len, gain);
        }

        /// <summary>
        /// Plays wall tones as continuous looping sound — one SDL stream per active direction,
        /// summed by SDL. Called every ~100ms by the audio loop; each call reconciles which
        /// direction streams are active and tops up their queues so the loop never drains.
        /// </summary>
        public static void PlayWallTonesLooped(IList<Direction> directions)
        {
            if (!AudioEngine.IsInitialized) return;

            int newMask = (directions == null || directions.Count == 0) ? 0 : DirectionsToBitmask(directions);
            if (newMask == 0)
            {
                if (currentWallDirectionsMask != 0)
                    StopWallTone();
                return;
            }

            int volume = FFII_ScreenReader.Core.PreferencesManager.WallToneVolume;
            int activeCount = CountBits(newMask);

            // User volume × clipping headroom — replaces the 1/sqrt(n) the old code baked into
            // the pre-mixed buffer. Applied uniformly as stream gain (pan stays baked in samples).
            float gain = (volume / 50.0f) * (activeCount > 1 ? (float)(1.0 / Math.Sqrt(activeCount)) : 1.0f);

            // Reconcile each of the four streams against the OLD mask (currentWallDirectionsMask),
            // then commit the new mask.
            UpdateDirectionStream(Direction.North, newMask, gain);
            UpdateDirectionStream(Direction.South, newMask, gain);
            UpdateDirectionStream(Direction.East,  newMask, gain);
            UpdateDirectionStream(Direction.West,  newMask, gain);

            currentWallDirectionsMask = newMask;
            lastWallToneVolume = volume;
        }

        /// <summary>
        /// Stops the continuous wall tone loop (clears all four direction streams).
        /// </summary>
        public static void StopWallTone()
        {
            currentWallDirectionsMask = 0;
            lastWallToneVolume = 50;
            AudioEngine.Clear(AudioEngine.Stream.WallNorth);
            AudioEngine.Clear(AudioEngine.Stream.WallSouth);
            AudioEngine.Clear(AudioEngine.Stream.WallEast);
            AudioEngine.Clear(AudioEngine.Stream.WallWest);
        }

        /// <summary>
        /// Returns true if any wall tone direction is currently active.
        /// </summary>
        public static bool IsWallTonePlaying() => currentWallDirectionsMask != 0;

        /// <summary>
        /// Plays an audio beacon ping with directional panning. Synthesizes PCM straight into
        /// the engine's reusable scratch buffer (zero per-ping managed allocation).
        /// </summary>
        public static void PlayBeacon(bool isSouth, float pan, float volumeScale, bool lowPitch = false)
        {
            try
            {
                if (!AudioEngine.IsInitialized) return;

                int frequency = isSouth ? SoundConstants.Beacon.FREQUENCY_SOUTH : SoundConstants.Beacon.FREQUENCY_NORTH;
                if (lowPitch) frequency /= 2;
                int beaconVolumePref = FFII_ScreenReader.Core.PreferencesManager.BeaconVolume;
                float prefMultiplier = beaconVolumePref / 50.0f;
                float volume = Math.Max(SoundConstants.Beacon.MIN_VOLUME,
                    Math.Min(SoundConstants.Beacon.MAX_VOLUME, volumeScale * prefMultiplier));

                int samples = (SoundConstants.SAMPLE_RATE * SoundConstants.Beacon.DURATION_MS) / 1000;
                int dataLength = samples * 4; // stereo 16-bit

                double panAngle = pan * Math.PI / 2;
                float leftVol = volume * (float)Math.Cos(panAngle);
                float rightVol = volume * (float)Math.Sin(panAngle);

                AudioEngine.PlayBeaconDirect(dataLength, bufferPtr =>
                {
                    int attackSamples = samples / 10;
                    for (int i = 0; i < samples; i++)
                    {
                        double t = (double)i / SoundConstants.SAMPLE_RATE;
                        double attack = Math.Min(1.0, (double)i / attackSamples);
                        double decay = (double)(samples - i) / samples;
                        double envelope = attack * decay;
                        double sineValue = Math.Sin(2 * Math.PI * frequency * t) * envelope;

                        Marshal.WriteInt16(bufferPtr, i * 4, (short)(sineValue * leftVol * 32767));
                        Marshal.WriteInt16(bufferPtr, i * 4 + 2, (short)(sineValue * rightVol * 32767));
                    }
                });
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SoundPlayer] Error playing beacon: {ex.Message}");
            }
        }

        #endregion

        #region Direction Helpers

        /// <summary>
        /// Activates / refreshes / deactivates a single direction's stream for this tick.
        /// Reads the OLD mask to tell newly-on from already-on.
        /// </summary>
        private static void UpdateDirectionStream(Direction dir, int newMask, float gain)
        {
            int bit = 1 << (int)dir;
            bool nowActive = (newMask & bit) != 0;
            bool wasActive = (currentWallDirectionsMask & bit) != 0;

            var stream = GetDirectionStream(dir);

            if (!nowActive)
            {
                if (wasActive)
                    AudioEngine.Clear(stream);
                return;
            }

            byte[] buf = GetSustainTone(dir);
            if (buf == null) return;
            int len = buf.Length - SoundConstants.WAV_HEADER_SIZE;
            if (len <= 0) return;

            AudioEngine.SetGain(stream, gain);

            if (!wasActive)
            {
                // Newly active: prime ~2 loops ahead so the queue can't drain before the next tick.
                AudioEngine.Clear(stream);
                AudioEngine.Submit(stream, buf, SoundConstants.WAV_HEADER_SIZE, len);
                AudioEngine.Submit(stream, buf, SoundConstants.WAV_HEADER_SIZE, len);
            }
            else if (AudioEngine.QueuedBytes(stream) < len * 2)
            {
                // Keep ~2 buffers queued (≈2 ticks) so back-to-back loops stay seamless.
                AudioEngine.Submit(stream, buf, SoundConstants.WAV_HEADER_SIZE, len);
            }
        }

        private static byte[] GetSustainTone(Direction dir)
        {
            switch (dir)
            {
                case Direction.North: return wallToneNorthSustain;
                case Direction.South: return wallToneSouthSustain;
                case Direction.East:  return wallToneEastSustain;
                case Direction.West:  return wallToneWestSustain;
                default: return null;
            }
        }

        private static AudioEngine.Stream GetDirectionStream(Direction dir)
        {
            switch (dir)
            {
                case Direction.North: return AudioEngine.Stream.WallNorth;
                case Direction.South: return AudioEngine.Stream.WallSouth;
                case Direction.East:  return AudioEngine.Stream.WallEast;
                case Direction.West:  return AudioEngine.Stream.WallWest;
                default: return AudioEngine.Stream.WallNorth;
            }
        }

        /// <summary>
        /// Converts a direction list to a bitmask for fast comparison.
        /// </summary>
        private static int DirectionsToBitmask(IList<Direction> dirs)
        {
            int mask = 0;
            int count = dirs.Count;
            for (int i = 0; i < count; i++)
                mask |= (1 << (int)dirs[i]);
            return mask;
        }

        private static int CountBits(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }
            return count;
        }

        #endregion
    }
}
