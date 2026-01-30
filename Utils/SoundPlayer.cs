using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Sound channels for concurrent playback.
    /// Each channel has its own waveOut handle and plays completely independently.
    /// </summary>
    public enum SoundChannel
    {
        Movement,    // Footsteps only
        WallBump,    // Wall bump sounds (separate from footsteps to avoid timing conflicts)
        WallTone,    // Wall proximity tones (loopable)
        Beacon       // Audio beacon pings
    }

    /// <summary>
    /// Request for a wall tone in a specific direction (adjacent only).
    /// </summary>
    public struct WallToneRequest
    {
        public SoundPlayer.Direction Direction;

        public WallToneRequest(SoundPlayer.Direction dir)
        {
            Direction = dir;
        }
    }

    /// <summary>
    /// Sound player using Windows waveOut API for true concurrent playback.
    /// Each channel has its own waveOut handle, allowing independent playback
    /// without mixing or timing synchronization issues.
    /// Wall tones use hardware looping for continuous steady tones.
    /// </summary>
    public static class SoundPlayer
    {
        #region waveOut P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;      // WAVE_FORMAT_PCM = 1
            public ushort nChannels;       // 2 for stereo
            public uint nSamplesPerSec;    // 22050
            public uint nAvgBytesPerSec;   // 22050 * 2 * 2 (stereo 16-bit)
            public ushort nBlockAlign;     // 4 (stereo 16-bit)
            public ushort wBitsPerSample;  // 16
            public ushort cbSize;          // 0
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0;
        private const uint WHDR_PREPARED = 0x02;
        private const uint WHDR_DONE = 0x01;
        private const uint WHDR_BEGINLOOP = 0x04;
        private const uint WHDR_ENDLOOP = 0x08;

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID,
            ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        #endregion

        #region Channel State

        /// <summary>
        /// State for each independent waveOut channel.
        /// Each channel has its own handle, buffer, and header - completely independent.
        /// </summary>
        private class ChannelState
        {
            public IntPtr WaveOutHandle;
            public IntPtr BufferPtr;        // Unmanaged memory for WAV PCM data
            public WAVEHDR Header;
            public bool IsPlaying;
            public bool HeaderPrepared;
            public bool IsLooping;          // True if currently in hardware loop mode
            public readonly object Lock = new object();
        }

        private static ChannelState[] channels;
        private static WAVEFORMATEX waveFormat;
        private static bool initialized = false;

        // Track current wall tone directions as a bitmask to avoid unnecessary loop restarts
        // Each bit represents a Direction enum value (North=0, South=1, East=2, West=3)
        private static int currentWallDirectionsMask = 0;

        // Track last wall tone volume to detect changes (triggers loop restart)
        private static int lastWallToneVolume = 50;

        #endregion

        #region Pre-cached Sounds

        // Pre-generated wall bump sound (cached) - stored as stereo
        private static byte[] wallBumpWav;

        // Footstep click sound - stored as stereo
        private static byte[] footstepWav;

        // Wall tones with decay (one-shot) - all stereo
        private static byte[] wallToneNorth;
        private static byte[] wallToneSouth;
        private static byte[] wallToneEast;
        private static byte[] wallToneWest;

        // Wall tones without decay (sustain, for looping) - all stereo
        private static byte[] wallToneNorthSustain;
        private static byte[] wallToneSouthSustain;
        private static byte[] wallToneEastSustain;
        private static byte[] wallToneWestSustain;

        // Audio beacon tones no longer pre-cached - PlayBeacon writes directly to unmanaged buffer

        #endregion

        /// <summary>
        /// Initializes the SoundPlayer by pre-generating tones and opening waveOut handles.
        /// Call this once during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;

            // Pre-generate wall bump tone: deep "thud" with soft attack
            byte[] wallBumpMono = GenerateThudTone(frequency: 27, durationMs: 60, volume: 0.506f);
            wallBumpWav = MonoToStereo(wallBumpMono);

            // Footstep: light click simulating 8/16-bit character steps
            byte[] footstepMono = GenerateClickTone(frequency: 500, durationMs: 25, volume: 0.338f);
            footstepWav = MonoToStereo(footstepMono);

            // Wall tones with frequency-compensated volumes (Fletcher-Munson equal-loudness)
            const float BASE_VOLUME = 0.12f;

            // One-shot tones (with decay) - kept for PlayWallTone single-direction
            wallToneNorth = GenerateStereoTone(330, 150, BASE_VOLUME * 1.00f, 0.5f);
            wallToneSouth = GenerateStereoTone(110, 150, BASE_VOLUME * 0.70f, 0.5f);
            wallToneEast = GenerateStereoTone(220, 150, BASE_VOLUME * 0.85f, 1.0f);
            wallToneWest = GenerateStereoTone(200, 150, BASE_VOLUME * 0.85f, 0.0f);

            // Sustain tones (no decay, for seamless looping) - 200ms buffer
            wallToneNorthSustain = GenerateStereoToneSustain(330, 200, BASE_VOLUME * 1.00f, 0.5f);
            wallToneSouthSustain = GenerateStereoToneSustain(110, 200, BASE_VOLUME * 0.70f, 0.5f);
            wallToneEastSustain = GenerateStereoToneSustain(220, 200, BASE_VOLUME * 0.85f, 1.0f);
            wallToneWestSustain = GenerateStereoToneSustain(200, 200, BASE_VOLUME * 0.85f, 0.0f);

            // Audio beacons use direct buffer writes in PlayBeacon() - no pre-caching needed

            // Setup wave format (stereo 16-bit 22050Hz - matches our generated tones)
            // 16-bit provides 65536 amplitude levels vs 256 for 8-bit, eliminating quantization noise
            waveFormat = new WAVEFORMATEX
            {
                wFormatTag = 1,           // PCM
                nChannels = 2,            // Stereo
                nSamplesPerSec = 22050,
                nAvgBytesPerSec = 88200,  // 22050 * 2 channels * 2 bytes
                nBlockAlign = 4,          // 2 channels * 2 bytes
                wBitsPerSample = 16,
                cbSize = 0
            };

            // Open one waveOut handle per channel (4 channels = 4 independent handles)
            channels = new ChannelState[4];

            for (int i = 0; i < 4; i++)
            {
                channels[i] = new ChannelState();

                int result = waveOutOpen(out channels[i].WaveOutHandle, WAVE_MAPPER,
                    ref waveFormat, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);

                if (result != 0)
                {
                    MelonLogger.Error($"[SoundPlayer] Failed to open waveOut for channel {i}: error {result}");
                    channels[i].WaveOutHandle = IntPtr.Zero;
                }
                else
                {
                    // Pre-allocate buffer for longest sound
                    // Sustain tones at 16-bit: 200ms * 22050 * 2 channels * 2 bytes = 17640 bytes
                    // Mixed sustain (same length): 17640 bytes
                    // Use 32768 for headroom
                    channels[i].BufferPtr = Marshal.AllocHGlobal(32768);
                }
            }

            initialized = true;
            MelonLogger.Msg("[SoundPlayer] Initialized with 4 independent waveOut channels (looping supported)");
        }

        /// <summary>
        /// Scales 16-bit PCM samples in an unmanaged buffer by a volume percentage.
        /// Volume 50 = no change, 0 = silence, 100 = 2x volume.
        /// </summary>
        private static void ScaleSamples(IntPtr bufferPtr, int length, int volumePercent)
        {
            if (volumePercent == 50) return; // No scaling needed at default
            float multiplier = volumePercent / 50.0f;
            // 16-bit samples: 2 bytes per sample
            int sampleCount = length / 2;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = Marshal.ReadInt16(bufferPtr, i * 2);
                int scaled = (int)(sample * multiplier);
                scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
                Marshal.WriteInt16(bufferPtr, i * 2, (short)scaled);
            }
        }

        /// <summary>
        /// Plays a sound on the specified channel using waveOut API.
        /// Each channel plays independently - no waiting, no mixing, no batching.
        /// When loop=true, uses hardware looping for continuous playback until StopChannel is called.
        /// </summary>
        private static void PlayOnChannel(byte[] wavData, SoundChannel channel, bool loop = false)
        {
            PlayOnChannel(wavData, channel, loop, 50); // Default volume (no scaling)
        }

        /// <summary>
        /// Plays a sound on the specified channel with volume scaling.
        /// volumePercent: 0-100 where 50 is default/no change.
        /// </summary>
        private static void PlayOnChannel(byte[] wavData, SoundChannel channel, bool loop, int volumePercent)
        {
            if (wavData == null || !initialized) return;

            int channelIndex = (int)channel;
            var state = channels[channelIndex];
            if (state?.WaveOutHandle == IntPtr.Zero) return;

            lock (state.Lock)
            {
                // If still playing, reset (stops current sound on THIS channel only)
                if (state.IsPlaying || state.HeaderPrepared)
                {
                    waveOutReset(state.WaveOutHandle);

                    if (state.HeaderPrepared)
                    {
                        waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());
                        state.HeaderPrepared = false;
                    }
                    state.IsPlaying = false;
                    state.IsLooping = false;
                }

                // Skip WAV header (44 bytes), copy PCM data to unmanaged buffer
                const int WAV_HEADER_SIZE = 44;
                if (wavData.Length <= WAV_HEADER_SIZE) return;

                int dataLength = wavData.Length - WAV_HEADER_SIZE;
                Marshal.Copy(wavData, WAV_HEADER_SIZE, state.BufferPtr, dataLength);

                // Apply volume scaling to the buffer
                if (volumePercent != 50)
                    ScaleSamples(state.BufferPtr, dataLength, volumePercent);

                // Setup WAVEHDR with optional loop flags
                state.Header = new WAVEHDR
                {
                    lpData = state.BufferPtr,
                    dwBufferLength = (uint)dataLength,
                    dwBytesRecorded = 0,
                    dwUser = IntPtr.Zero,
                    dwFlags = loop ? (WHDR_BEGINLOOP | WHDR_ENDLOOP) : 0,
                    dwLoops = loop ? 0xFFFFFFFF : 0,
                    lpNext = IntPtr.Zero,
                    reserved = IntPtr.Zero
                };

                // Prepare and write
                int prepResult = waveOutPrepareHeader(state.WaveOutHandle, ref state.Header,
                    (uint)Marshal.SizeOf<WAVEHDR>());

                if (prepResult == 0)
                {
                    state.HeaderPrepared = true;

                    int writeResult = waveOutWrite(state.WaveOutHandle, ref state.Header,
                        (uint)Marshal.SizeOf<WAVEHDR>());

                    if (writeResult == 0)
                    {
                        state.IsPlaying = true;
                        state.IsLooping = loop;
                    }
                    else
                    {
                        // Write failed, unprepare immediately
                        waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());
                        state.HeaderPrepared = false;
                    }
                }
            }
        }

        /// <summary>
        /// Stops playback on a specific channel. Used to halt looping tones.
        /// </summary>
        public static void StopChannel(SoundChannel channel)
        {
            if (!initialized || channels == null) return;

            int channelIndex = (int)channel;
            var state = channels[channelIndex];
            if (state?.WaveOutHandle == IntPtr.Zero) return;

            lock (state.Lock)
            {
                if (state.IsPlaying || state.HeaderPrepared)
                {
                    waveOutReset(state.WaveOutHandle);

                    if (state.HeaderPrepared)
                    {
                        waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());
                        state.HeaderPrepared = false;
                    }
                    state.IsPlaying = false;
                    state.IsLooping = false;
                }
            }
        }

        /// <summary>
        /// Shuts down all waveOut channels and frees resources.
        /// Call this when the mod is unloaded.
        /// </summary>
        public static void Shutdown()
        {
            if (!initialized || channels == null) return;

            for (int i = 0; i < 4; i++)
            {
                if (channels[i] != null)
                {
                    lock (channels[i].Lock)
                    {
                        if (channels[i].WaveOutHandle != IntPtr.Zero)
                        {
                            waveOutReset(channels[i].WaveOutHandle);

                            if (channels[i].HeaderPrepared)
                            {
                                waveOutUnprepareHeader(channels[i].WaveOutHandle, ref channels[i].Header,
                                    (uint)Marshal.SizeOf<WAVEHDR>());
                                channels[i].HeaderPrepared = false;
                            }

                            waveOutClose(channels[i].WaveOutHandle);
                            channels[i].WaveOutHandle = IntPtr.Zero;
                        }

                        if (channels[i].BufferPtr != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(channels[i].BufferPtr);
                            channels[i].BufferPtr = IntPtr.Zero;
                        }
                    }
                }
            }

            currentWallDirectionsMask = 0;
            lastWallToneVolume = 50;
            initialized = false;
            MelonLogger.Msg("[SoundPlayer] All waveOut channels closed");
        }

        #region Public Playback Methods

        /// <summary>
        /// Plays the wall bump sound effect on the WallBump channel.
        /// </summary>
        public static void PlayWallBump()
        {
            if (wallBumpWav == null) return;
            int volume = FFII_ScreenReader.Core.FFII_ScreenReaderMod.WallBumpVolume;
            PlayOnChannel(wallBumpWav, SoundChannel.WallBump, false, volume);
        }

        /// <summary>
        /// Plays the footstep click sound on the Movement channel.
        /// </summary>
        public static void PlayFootstep()
        {
            if (footstepWav == null) return;
            int volume = FFII_ScreenReader.Core.FFII_ScreenReaderMod.FootstepVolume;
            PlayOnChannel(footstepWav, SoundChannel.Movement, false, volume);
        }

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
        /// Plays a one-shot wall proximity tone for the given direction.
        /// Uses WallTone channel - independent from movement and beacon channels.
        /// </summary>
        public static void PlayWallTone(Direction dir)
        {
            byte[] tone = null;

            switch (dir)
            {
                case Direction.North: tone = wallToneNorth; break;
                case Direction.South: tone = wallToneSouth; break;
                case Direction.East: tone = wallToneEast; break;
                case Direction.West: tone = wallToneWest; break;
            }

            if (tone == null) return;
            int volume = FFII_ScreenReader.Core.FFII_ScreenReaderMod.WallToneVolume;
            PlayOnChannel(tone, SoundChannel.WallTone, false, volume);
        }

        /// <summary>
        /// Plays one-shot wall tones (multiple directions mixed).
        /// </summary>
        public static void PlayWallTones(WallToneRequest[] requests)
        {
            if (requests == null || requests.Length == 0) return;

            var tonesToMix = new List<byte[]>();
            foreach (var req in requests)
            {
                byte[] tone = null;
                switch (req.Direction)
                {
                    case Direction.North: tone = wallToneNorth; break;
                    case Direction.South: tone = wallToneSouth; break;
                    case Direction.East: tone = wallToneEast; break;
                    case Direction.West: tone = wallToneWest; break;
                }
                if (tone != null)
                    tonesToMix.Add(tone);
            }

            if (tonesToMix.Count == 0) return;

            int volume = FFII_ScreenReader.Core.FFII_ScreenReaderMod.WallToneVolume;

            if (tonesToMix.Count == 1)
            {
                PlayOnChannel(tonesToMix[0], SoundChannel.WallTone, false, volume);
                return;
            }

            byte[] mixedWav = MixWavFiles(tonesToMix);
            if (mixedWav != null)
            {
                PlayOnChannel(mixedWav, SoundChannel.WallTone, false, volume);
            }
        }

        /// <summary>
        /// Plays wall tones as a continuous looping sound.
        /// Mixes sustain tones for all given directions and loops them.
        /// Only restarts the loop if directions have changed OR volume has changed.
        /// Pass empty/null to stop wall tones.
        ///
        /// Note: Volume is applied during tone generation (not post-scaling) to preserve
        /// 16-bit dynamic range at low volumes, avoiding quantization distortion/buzzing.
        /// Accepts IList&lt;Direction&gt; to avoid ToArray() allocations from callers.
        /// </summary>
        public static void PlayWallTonesLooped(IList<Direction> directions)
        {
            if (directions == null || directions.Count == 0)
            {
                StopWallTone();
                return;
            }

            int volume = FFII_ScreenReader.Core.FFII_ScreenReaderMod.WallToneVolume;

            // Compare bitmasks AND volume to skip restart if nothing changed
            int newMask = DirectionsToBitmask(directions);
            if (newMask == currentWallDirectionsMask && volume == lastWallToneVolume)
                return;

            // Store new directions mask and volume
            currentWallDirectionsMask = newMask;
            lastWallToneVolume = volume;

            // Generate sustain tones with volume baked in during generation
            // This preserves 16-bit dynamic range at low volumes (no post-scaling quantization)
            const float BASE_VOLUME = 0.12f;
            var tonesToMix = new List<byte[]>();
            foreach (var dir in directions)
            {
                byte[] tone = null;
                switch (dir)
                {
                    case Direction.North:
                        tone = GenerateStereoToneSustainWithVolume(330, 200, BASE_VOLUME * 1.00f, 0.5f, volume);
                        break;
                    case Direction.South:
                        tone = GenerateStereoToneSustainWithVolume(110, 200, BASE_VOLUME * 0.70f, 0.5f, volume);
                        break;
                    case Direction.East:
                        tone = GenerateStereoToneSustainWithVolume(220, 200, BASE_VOLUME * 0.85f, 1.0f, volume);
                        break;
                    case Direction.West:
                        tone = GenerateStereoToneSustainWithVolume(200, 200, BASE_VOLUME * 0.85f, 0.0f, volume);
                        break;
                }
                if (tone != null)
                    tonesToMix.Add(tone);
            }

            if (tonesToMix.Count == 0)
            {
                StopWallTone();
                return;
            }

            // Single tone or mix multiple
            byte[] loopBuffer;
            if (tonesToMix.Count == 1)
            {
                loopBuffer = tonesToMix[0];
            }
            else
            {
                loopBuffer = MixWavFiles(tonesToMix);
            }

            if (loopBuffer != null)
            {
                // Volume already baked in during generation - use 50 (no scaling)
                PlayOnChannel(loopBuffer, SoundChannel.WallTone, loop: true, volumePercent: 50);
            }
        }

        /// <summary>
        /// Stops the continuous wall tone loop.
        /// </summary>
        public static void StopWallTone()
        {
            currentWallDirectionsMask = 0;
            lastWallToneVolume = 50;
            StopChannel(SoundChannel.WallTone);
        }

        /// <summary>
        /// Returns true if the wall tone channel is currently playing.
        /// Used to avoid redundant StopWallTone calls.
        /// </summary>
        public static bool IsWallTonePlaying()
        {
            if (!initialized || channels == null) return false;
            var state = channels[(int)SoundChannel.WallTone];
            if (state == null) return false;
            lock (state.Lock)
            {
                return state.IsPlaying;
            }
        }

        /// <summary>
        /// Plays an audio beacon on Channel 3 (Beacon).
        /// Writes PCM samples directly to the pre-allocated unmanaged buffer,
        /// avoiding managed allocations (no MemoryStream, BinaryWriter, or byte[]).
        /// </summary>
        public static void PlayBeacon(bool isSouth, float pan, float volumeScale)
        {
            if (!initialized) return;

            int channelIndex = (int)SoundChannel.Beacon;
            var state = channels[channelIndex];
            if (state?.WaveOutHandle == IntPtr.Zero) return;

            try
            {
                int frequency = isSouth ? 280 : 400;
                // Apply preference volume multiplier (50 = 1.0x, 100 = 2.0x, 0 = 0x)
                int beaconVolumePref = FFII_ScreenReader.Core.FFII_ScreenReaderMod.BeaconVolume;
                float prefMultiplier = beaconVolumePref / 50.0f;
                float volume = Math.Max(0.10f, Math.Min(0.50f, volumeScale * prefMultiplier));

                int sampleRate = 22050;
                int durationMs = 60;
                int samples = (sampleRate * durationMs) / 1000; // 1323 samples
                int dataLength = samples * 4; // stereo 16-bit = 4 bytes per sample frame

                double panAngle = pan * Math.PI / 2;
                float leftVol = volume * (float)Math.Cos(panAngle);
                float rightVol = volume * (float)Math.Sin(panAngle);

                lock (state.Lock)
                {
                    // Stop current playback if any
                    if (state.IsPlaying || state.HeaderPrepared)
                    {
                        waveOutReset(state.WaveOutHandle);
                        if (state.HeaderPrepared)
                        {
                            waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                                (uint)Marshal.SizeOf<WAVEHDR>());
                            state.HeaderPrepared = false;
                        }
                        state.IsPlaying = false;
                        state.IsLooping = false;
                    }

                    // Write 16-bit PCM samples directly to unmanaged buffer (no managed allocations)
                    int attackSamples = samples / 10;
                    for (int i = 0; i < samples; i++)
                    {
                        double t = (double)i / sampleRate;
                        double attack = Math.Min(1.0, (double)i / attackSamples);
                        double decay = (double)(samples - i) / samples;
                        double envelope = attack * decay;
                        double sineValue = Math.Sin(2 * Math.PI * frequency * t) * envelope;

                        // 16-bit signed: range -32767 to +32767
                        short left = (short)(sineValue * leftVol * 32767);
                        short right = (short)(sineValue * rightVol * 32767);

                        Marshal.WriteInt16(state.BufferPtr, i * 4, left);
                        Marshal.WriteInt16(state.BufferPtr, i * 4 + 2, right);
                    }

                    // Setup WAVEHDR (one-shot, no loop)
                    state.Header = new WAVEHDR
                    {
                        lpData = state.BufferPtr,
                        dwBufferLength = (uint)dataLength,
                        dwBytesRecorded = 0,
                        dwUser = IntPtr.Zero,
                        dwFlags = 0,
                        dwLoops = 0,
                        lpNext = IntPtr.Zero,
                        reserved = IntPtr.Zero
                    };

                    int prepResult = waveOutPrepareHeader(state.WaveOutHandle, ref state.Header,
                        (uint)Marshal.SizeOf<WAVEHDR>());

                    if (prepResult == 0)
                    {
                        state.HeaderPrepared = true;
                        int writeResult = waveOutWrite(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());

                        if (writeResult == 0)
                        {
                            state.IsPlaying = true;
                        }
                        else
                        {
                            waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                                (uint)Marshal.SizeOf<WAVEHDR>());
                            state.HeaderPrepared = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SoundPlayer] Error playing beacon: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Converts a direction list to a bitmask for fast comparison.
        /// Each Direction enum value maps to a single bit.
        /// Accepts IList&lt;Direction&gt; to avoid array allocations.
        /// </summary>
        private static int DirectionsToBitmask(IList<Direction> dirs)
        {
            int mask = 0;
            int count = dirs.Count;
            for (int i = 0; i < count; i++)
                mask |= (1 << (int)dirs[i]);
            return mask;
        }

        #endregion

        #region Tone Generation

        /// <summary>
        /// Converts a mono 16-bit WAV to stereo by duplicating each sample to both channels.
        /// </summary>
        private static byte[] MonoToStereo(byte[] monoWav)
        {
            if (monoWav == null || monoWav.Length < 44) return monoWav;

            using (var reader = new BinaryReader(new MemoryStream(monoWav)))
            {
                reader.ReadBytes(4);  // "RIFF"
                reader.ReadInt32();   // file size
                reader.ReadBytes(4);  // "WAVE"
                reader.ReadBytes(4);  // "fmt "
                int fmtSize = reader.ReadInt32();
                reader.ReadInt16();   // audio format
                int channels = reader.ReadInt16();
                int sampleRate = reader.ReadInt32();
                reader.ReadInt32();   // byte rate
                reader.ReadInt16();   // block align
                int bitsPerSample = reader.ReadInt16();

                if (fmtSize > 16)
                    reader.ReadBytes(fmtSize - 16);

                reader.ReadBytes(4);  // "data"
                int dataSize = reader.ReadInt32();

                if (channels == 2)
                    return monoWav;

                byte[] monoData = reader.ReadBytes(dataSize);

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // 16-bit mono to stereo: each 2-byte sample becomes 4 bytes (L+R)
                    int stereoDataSize = dataSize * 2;

                    writer.Write(new[] { 'R', 'I', 'F', 'F' });
                    writer.Write(36 + stereoDataSize);
                    writer.Write(new[] { 'W', 'A', 'V', 'E' });

                    writer.Write(new[] { 'f', 'm', 't', ' ' });
                    writer.Write(16);
                    writer.Write((short)1);           // PCM
                    writer.Write((short)2);           // Stereo
                    writer.Write(sampleRate);
                    writer.Write(sampleRate * 4);     // Byte rate (stereo 16-bit)
                    writer.Write((short)4);           // Block align (2 channels * 2 bytes)
                    writer.Write((short)16);          // Bits per sample

                    writer.Write(new[] { 'd', 'a', 't', 'a' });
                    writer.Write(stereoDataSize);

                    // Copy 16-bit samples (2 bytes each) to both channels
                    for (int i = 0; i < monoData.Length; i += 2)
                    {
                        writer.Write(monoData[i]);      // Left low byte
                        writer.Write(monoData[i + 1]);  // Left high byte
                        writer.Write(monoData[i]);      // Right low byte
                        writer.Write(monoData[i + 1]);  // Right high byte
                    }

                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Generates a 16-bit WAV file containing a "thud" sound with soft attack and noise mix.
        /// </summary>
        private static byte[] GenerateThudTone(int frequency, int durationMs, float volume)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;
            int attackSamples = samples / 4;
            var random = new System.Random(42);
            int dataSize = samples * 2; // 16-bit = 2 bytes per sample

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)1);           // Mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);     // Byte rate (mono 16-bit)
                writer.Write((short)2);           // Block align (1 channel * 2 bytes)
                writer.Write((short)16);          // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                double filteredNoise = 0;

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;

                    double attackLinear = Math.Min(1.0, (double)i / attackSamples);
                    double attack = attackLinear * attackLinear;
                    double decay = (double)(samples - i) / samples;
                    double envelope = attack * decay;

                    double sine = Math.Sin(2 * Math.PI * frequency * t);
                    double rawNoise = (random.NextDouble() * 2 - 1);
                    filteredNoise = filteredNoise * 0.9 + rawNoise * 0.1;
                    double noise = filteredNoise * 0.3 * attack;
                    double value = (sine * 0.7 + noise) * volume * envelope;

                    // 16-bit signed: range -32767 to +32767
                    writer.Write((short)(value * 32767));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a short 16-bit click/tap sound for footsteps using filtered noise burst.
        /// </summary>
        private static byte[] GenerateClickTone(int frequency, int durationMs, float volume)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;
            var random = new System.Random(42);
            int dataSize = samples * 2; // 16-bit = 2 bytes per sample

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)1);           // Mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);     // Byte rate (mono 16-bit)
                writer.Write((short)2);           // Block align (1 channel * 2 bytes)
                writer.Write((short)16);          // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                for (int i = 0; i < samples; i++)
                {
                    double decay = Math.Exp(-10.0 * i / samples);
                    double noise = (random.NextDouble() * 2 - 1) * volume * decay;
                    // 16-bit signed: range -32767 to +32767
                    writer.Write((short)(noise * 32767));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a 16-bit stereo WAV tone with constant-power panning and decay envelope.
        /// Used for one-shot sounds (beacons, single wall tone pings).
        /// </summary>
        private static byte[] GenerateStereoTone(int frequency, int durationMs, float volume, float pan)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;
            int dataSize = samples * 4; // stereo 16-bit = 4 bytes per sample frame

            double panAngle = pan * Math.PI / 2;
            float leftVol = volume * (float)Math.Cos(panAngle);
            float rightVol = volume * (float)Math.Sin(panAngle);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)2);           // Stereo
                writer.Write(sampleRate);
                writer.Write(sampleRate * 4);     // Byte rate (stereo 16-bit)
                writer.Write((short)4);           // Block align (2 channels * 2 bytes)
                writer.Write((short)16);          // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;

                    int attackSamples = samples / 10;
                    double attack = Math.Min(1.0, (double)i / attackSamples);
                    double decay = (double)(samples - i) / samples;
                    double envelope = attack * decay;

                    double sineValue = Math.Sin(2 * Math.PI * frequency * t) * envelope;

                    // 16-bit signed: range -32767 to +32767
                    writer.Write((short)(sineValue * leftVol * 32767));
                    writer.Write((short)(sineValue * rightVol * 32767));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a stereo WAV tone with sustain for seamless hardware looping.
        /// Volume is applied during generation to preserve 16-bit dynamic range at low volumes.
        /// This avoids quantization distortion that occurs when scaling samples post-generation.
        /// </summary>
        private static byte[] GenerateStereoToneSustainWithVolume(int frequency, int durationMs, float baseVolume, float pan, int volumePercent)
        {
            // Apply volume scaling to the base volume before generation
            // volumePercent: 0-100, where 50 is the default (1.0x multiplier)
            float scaledVolume = baseVolume * (volumePercent / 50.0f);
            return GenerateStereoToneSustain(frequency, durationMs, scaledVolume, pan);
        }

        /// <summary>
        /// Generates a 16-bit stereo WAV tone with sustain (no decay) for seamless hardware looping.
        /// Uses cycle-aligned sample counts to ensure the waveform starts and ends at the same phase,
        /// creating seamless loops without clicks or pops at buffer boundaries.
        /// 16-bit audio eliminates quantization noise that causes static at low volumes.
        /// </summary>
        private static byte[] GenerateStereoToneSustain(int frequency, int durationMs, float volume, float pan)
        {
            int sampleRate = 22050;

            // Calculate cycle-aligned sample count for seamless looping
            // This ensures the buffer contains exactly N complete cycles of the waveform
            double samplesPerCycle = (double)sampleRate / frequency;
            int targetSamples = (sampleRate * durationMs) / 1000;
            int numCycles = (int)Math.Round(targetSamples / samplesPerCycle);
            if (numCycles < 1) numCycles = 1;
            int samples = (int)Math.Round(numCycles * samplesPerCycle);
            int dataSize = samples * 4; // stereo 16-bit = 4 bytes per sample frame

            double panAngle = pan * Math.PI / 2;
            float leftVol = volume * (float)Math.Cos(panAngle);
            float rightVol = volume * (float)Math.Sin(panAngle);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)2);           // Stereo
                writer.Write(sampleRate);
                writer.Write(sampleRate * 4);     // Byte rate (stereo 16-bit)
                writer.Write((short)4);           // Block align (2 channels * 2 bytes)
                writer.Write((short)16);          // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                // No attack envelope - the cycle-aligned buffer creates seamless loops
                // Adding an attack would create a discontinuity on each loop iteration
                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;
                    double sineValue = Math.Sin(2 * Math.PI * frequency * t);

                    // 16-bit signed: range -32767 to +32767
                    writer.Write((short)(sineValue * leftVol * 32767));
                    writer.Write((short)(sineValue * rightVol * 32767));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Mixes multiple 16-bit WAV files into a single stereo WAV.
        /// Used for playing multiple wall tones in different directions simultaneously.
        /// </summary>
        private static byte[] MixWavFiles(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0) return null;

            const int HEADER_SIZE = 44;

            int maxDataLength = 0;
            foreach (var wav in wavFiles)
            {
                if (wav.Length > HEADER_SIZE)
                {
                    int dataLen = wav.Length - HEADER_SIZE;
                    if (dataLen > maxDataLength)
                        maxDataLength = dataLen;
                }
            }

            if (maxDataLength == 0) return null;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                int sampleRate = 22050;

                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + maxDataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)2);           // Stereo
                writer.Write(sampleRate);
                writer.Write(sampleRate * 4);     // Byte rate (stereo 16-bit)
                writer.Write((short)4);           // Block align (2 channels * 2 bytes)
                writer.Write((short)16);          // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(maxDataLength);

                // Process 16-bit samples (2 bytes each)
                int sampleCount = maxDataLength / 2;
                for (int i = 0; i < sampleCount; i++)
                {
                    int mixedValue = 0;
                    int count = 0;

                    foreach (var wav in wavFiles)
                    {
                        int pos = HEADER_SIZE + (i * 2);
                        if (pos + 1 < wav.Length)
                        {
                            // Read 16-bit signed sample (little-endian)
                            short sample = (short)(wav[pos] | (wav[pos + 1] << 8));
                            mixedValue += sample;
                            count++;
                        }
                    }

                    if (count > 1)
                    {
                        double headroom = 1.0 / Math.Sqrt(count);
                        mixedValue = (int)(mixedValue * headroom);
                    }

                    // Clamp to 16-bit signed range
                    mixedValue = Math.Max(short.MinValue, Math.Min(short.MaxValue, mixedValue));
                    writer.Write((short)mixedValue);
                }

                return ms.ToArray();
            }
        }

        #endregion
    }
}
