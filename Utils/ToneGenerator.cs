using System;
using System.Collections.Generic;
using System.IO;

namespace FFII_ScreenReader.Utils
{
    /// <summary>
    /// Generates PCM audio tones as WAV byte arrays.
    /// All output is 16-bit at SoundConstants.SAMPLE_RATE.
    /// </summary>
    public static class ToneGenerator
    {
        /// <summary>
        /// Generates a mono "thud" tone with soft quadratic attack, noise mix, and decay.
        /// Used for wall bump sounds.
        /// </summary>
        public static byte[] GenerateThudTone(int frequency, int durationMs, float volume)
        {
            int samples = (SoundConstants.SAMPLE_RATE * durationMs) / 1000;
            int attackSamples = samples / 4;
            var random = new Random(42);
            int dataSize = samples * 2;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteWavHeader(writer, 1, dataSize);

                double filteredNoise = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / SoundConstants.SAMPLE_RATE;
                    double attackLinear = Math.Min(1.0, (double)i / attackSamples);
                    double attack = attackLinear * attackLinear;
                    double decay = (double)(samples - i) / samples;
                    double envelope = attack * decay;

                    double sine = Math.Sin(2 * Math.PI * frequency * t);
                    double rawNoise = (random.NextDouble() * 2 - 1);
                    filteredNoise = filteredNoise * 0.9 + rawNoise * 0.1;
                    double noise = filteredNoise * 0.3 * attack;
                    double value = (sine * 0.7 + noise) * volume * envelope;

                    writer.Write((short)(value * 32767));
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a mono filtered-noise click tone with exponential decay.
        /// Used for footstep sounds.
        /// </summary>
        public static byte[] GenerateClickTone(int frequency, int durationMs, float volume)
        {
            int samples = (SoundConstants.SAMPLE_RATE * durationMs) / 1000;
            var random = new Random(42);
            int dataSize = samples * 2;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteWavHeader(writer, 1, dataSize);

                for (int i = 0; i < samples; i++)
                {
                    double decay = Math.Exp(-10.0 * i / samples);
                    double noise = (random.NextDouble() * 2 - 1) * volume * decay;
                    writer.Write((short)(noise * 32767));
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a 16-bit stereo sine tone with constant-power panning.
        /// When sustain is false: applies attack+decay envelope (for one-shot sounds).
        /// When sustain is true: cycle-aligned flat amplitude for seamless hardware looping.
        /// </summary>
        public static byte[] GenerateStereoTone(int frequency, int durationMs, float volume, float pan, bool sustain = false)
        {
            int samples;
            if (sustain)
            {
                double samplesPerCycle = (double)SoundConstants.SAMPLE_RATE / frequency;
                int targetSamples = (SoundConstants.SAMPLE_RATE * durationMs) / 1000;
                int numCycles = (int)Math.Round(targetSamples / samplesPerCycle);
                if (numCycles < 1) numCycles = 1;
                samples = (int)Math.Round(numCycles * samplesPerCycle);
            }
            else
            {
                samples = (SoundConstants.SAMPLE_RATE * durationMs) / 1000;
            }

            int dataSize = samples * 4;

            double panAngle = pan * Math.PI / 2;
            float leftVol = volume * (float)Math.Cos(panAngle);
            float rightVol = volume * (float)Math.Sin(panAngle);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteWavHeader(writer, 2, dataSize);

                int attackSamples = samples / 10;
                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / SoundConstants.SAMPLE_RATE;
                    double sineValue;

                    if (sustain)
                    {
                        sineValue = Math.Sin(2 * Math.PI * frequency * t);
                    }
                    else
                    {
                        double attack = Math.Min(1.0, (double)i / attackSamples);
                        double decay = (double)(samples - i) / samples;
                        sineValue = Math.Sin(2 * Math.PI * frequency * t) * attack * decay;
                    }

                    writer.Write((short)(sineValue * leftVol * 32767));
                    writer.Write((short)(sineValue * rightVol * 32767));
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Converts a mono 16-bit WAV to stereo by duplicating each sample to both channels.
        /// </summary>
        public static byte[] MonoToStereo(byte[] monoWav)
        {
            if (monoWav == null || monoWav.Length < SoundConstants.WAV_HEADER_SIZE) return monoWav;

            using (var reader = new BinaryReader(new MemoryStream(monoWav)))
            {
                reader.ReadBytes(4);
                reader.ReadInt32();
                reader.ReadBytes(4);
                reader.ReadBytes(4);
                int fmtSize = reader.ReadInt32();
                reader.ReadInt16();
                int channels = reader.ReadInt16();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                reader.ReadInt16();

                if (fmtSize > 16)
                    reader.ReadBytes(fmtSize - 16);

                reader.ReadBytes(4);
                int dataSize = reader.ReadInt32();

                if (channels == 2) return monoWav;

                byte[] monoData = reader.ReadBytes(dataSize);
                int stereoDataSize = dataSize * 2;

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    WriteWavHeader(writer, 2, stereoDataSize);

                    for (int i = 0; i < monoData.Length; i += 2)
                    {
                        writer.Write(monoData[i]);
                        writer.Write(monoData[i + 1]);
                        writer.Write(monoData[i]);
                        writer.Write(monoData[i + 1]);
                    }
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Generates a single 16-bit stereo buffer that mixes several sine tones and loops
        /// seamlessly. Each frequency is snapped to a whole number of cycles over the shared
        /// buffer length, so the SUM returns to its starting phase at the end of the buffer —
        /// no per-loop click. A single uniform 1/sqrt(n) headroom is applied across the whole
        /// buffer, so there is no amplitude step (unlike summing independently length-aligned
        /// tones via MixWavFiles, which leaves a gap + gain step for the shorter tone).
        /// Used for the looping wall-tone playback (driver loops the whole buffer back-to-back).
        /// </summary>
        public static byte[] GenerateMixedLoopTone(IList<(int frequency, float volume, float pan)> tones, int durationMs)
        {
            if (tones == null || tones.Count == 0) return null;

            // One shared loop length for every component, independent of which are active.
            int length = (SoundConstants.SAMPLE_RATE * durationMs) / 1000;
            if (length < 1) length = 1;

            int n = tones.Count;
            double headroom = n > 1 ? 1.0 / Math.Sqrt(n) : 1.0;

            // Snap each frequency to an integer cycle count over the buffer so the whole mix is
            // exactly periodic; precompute panned per-tone gains (constant-power, headroom-scaled).
            var cycles = new int[n];
            var leftVol = new double[n];
            var rightVol = new double[n];
            for (int t = 0; t < n; t++)
            {
                int c = (int)Math.Round((double)tones[t].frequency * length / SoundConstants.SAMPLE_RATE);
                cycles[t] = c < 1 ? 1 : c;
                double panAngle = tones[t].pan * Math.PI / 2;
                leftVol[t] = tones[t].volume * Math.Cos(panAngle) * headroom;
                rightVol[t] = tones[t].volume * Math.Sin(panAngle) * headroom;
            }

            int dataSize = length * 4; // stereo 16-bit

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteWavHeader(writer, 2, dataSize);

                for (int i = 0; i < length; i++)
                {
                    double left = 0.0;
                    double right = 0.0;
                    for (int t = 0; t < n; t++)
                    {
                        // Integer-cycle phase: exactly periodic over `length`, no float drift.
                        double sine = Math.Sin(2 * Math.PI * cycles[t] * i / length);
                        left += sine * leftVol[t];
                        right += sine * rightVol[t];
                    }

                    left = Math.Max(-1.0, Math.Min(1.0, left));
                    right = Math.Max(-1.0, Math.Min(1.0, right));
                    writer.Write((short)(left * 32767));
                    writer.Write((short)(right * 32767));
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Mixes multiple 16-bit stereo WAV files into one.
        /// Uses sqrt(n) headroom scaling to prevent clipping.
        /// </summary>
        public static byte[] MixWavFiles(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0) return null;

            int maxDataLength = 0;
            foreach (var wav in wavFiles)
            {
                if (wav.Length > SoundConstants.WAV_HEADER_SIZE)
                {
                    int dataLen = wav.Length - SoundConstants.WAV_HEADER_SIZE;
                    if (dataLen > maxDataLength) maxDataLength = dataLen;
                }
            }
            if (maxDataLength == 0) return null;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteWavHeader(writer, 2, maxDataLength);

                int sampleCount = maxDataLength / 2;
                for (int i = 0; i < sampleCount; i++)
                {
                    int mixedValue = 0;
                    int count = 0;

                    foreach (var wav in wavFiles)
                    {
                        int pos = SoundConstants.WAV_HEADER_SIZE + (i * 2);
                        if (pos + 1 < wav.Length)
                        {
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

                    mixedValue = Math.Max(short.MinValue, Math.Min(short.MaxValue, mixedValue));
                    writer.Write((short)mixedValue);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Writes a standard 44-byte WAV file header for 16-bit PCM audio.
        /// </summary>
        private static void WriteWavHeader(BinaryWriter writer, int numChannels, int dataSize)
        {
            int blockAlign = numChannels * 2;
            int byteRate = SoundConstants.SAMPLE_RATE * blockAlign;

            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });

            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)numChannels);
            writer.Write(SoundConstants.SAMPLE_RATE);
            writer.Write(byteRate);
            writer.Write((short)blockAlign);
            writer.Write((short)16);

            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
        }
    }
}
