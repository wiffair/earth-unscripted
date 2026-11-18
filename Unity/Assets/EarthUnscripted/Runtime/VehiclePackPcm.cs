using System;
using System.IO;
using System.Text;

namespace EarthUnscripted
{
    /// <summary>Worker-safe decoder for the deliberately narrow pack WAV contract.
    /// PCM16 mono, 22050/44100/48000Hz, 0.25–30 seconds. Exact RIFF bounds and
    /// fmt/data uniqueness are validated before any sample array allocation.
    /// No codecs, external paths, Unity calls or native audio decoders are used.</summary>
    public static class VehiclePackPcm
    {
        public const int MaximumBytes = 4 * 1024 * 1024;
        public const int MaximumSeconds = 30;
        public static VehiclePackPcmData Decode(byte[] bytes)
        {
            Require(bytes != null && bytes.Length >= 44 && bytes.Length <= MaximumBytes, "Invalid WAV size.");
            Require(Tag(bytes, 0) == "RIFF" && Tag(bytes, 8) == "WAVE", "Expected RIFF WAVE.");
            Require(U32(bytes, 4) == bytes.Length - 8, "RIFF size differs from payload.");
            int position = 12, rate = 0, dataOffset = -1, dataLength = 0;
            bool formatSeen = false;
            while (position < bytes.Length)
            {
                Require(bytes.Length - position >= 8, "Truncated WAV chunk header.");
                string tag = Tag(bytes, position);
                uint length = U32(bytes, position + 4);
                position += 8;
                Require(length <= bytes.Length - position, "WAV chunk exceeds payload.");
                int count = (int)length;
                if (tag == "fmt ")
                {
                    Require(!formatSeen && count == 16, "Expected one 16-byte PCM format chunk.");
                    Require(U16(bytes, position) == 1 && U16(bytes, position + 2) == 1 && U16(bytes, position + 14) == 16,
                        "Only PCM16 mono engine loops are supported.");
                    uint sampleRate = U32(bytes, position + 4);
                    Require(sampleRate == 22050 || sampleRate == 44100 || sampleRate == 48000, "Unsupported WAV sample rate.");
                    rate = (int)sampleRate;
                    Require(U16(bytes, position + 12) == 2 && U32(bytes, position + 8) == rate * 2,
                        "PCM block alignment or byte rate differs.");
                    formatSeen = true;
                }
                else if (tag == "data")
                {
                    Require(dataOffset < 0 && count > 0 && count % 2 == 0, "Invalid or duplicate PCM data chunk.");
                    dataOffset = position; dataLength = count;
                }
                else Require(tag == "JUNK" || tag == "LIST", "Unsupported WAV chunk.");
                long next = (long)position + count + (count & 1);
                Require(next <= bytes.Length, "Missing WAV chunk padding.");
                position = (int)next;
            }
            Require(formatSeen && dataOffset >= 0, "WAV needs format and data chunks.");
            int samples = dataLength / 2;
            Require(samples >= rate / 4 && samples <= rate * MaximumSeconds, "WAV duration must be 0.25–30 seconds.");
            var decoded = new float[samples];
            for (int index = 0; index < samples; index++)
                decoded[index] = unchecked((short)U16(bytes, dataOffset + index * 2)) / 32768f;
            return new VehiclePackPcmData { SampleRate = rate, Samples = decoded };
        }
        static string Tag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);
        static ushort U16(byte[] data, int offset) => (ushort)(data[offset] | data[offset + 1] << 8);
        static uint U32(byte[] data, int offset) => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    }
    public sealed class VehiclePackPcmData
    {
        public int SampleRate;
        public float[] Samples;
        public float ReferenceRpm, Gain;
        public bool ReferenceRpmEstimated;
    }
    public sealed class VehiclePackEngineAudio
    {
        public VehiclePackPcmData Exhaust, Mechanical;
        public float MinimumPitch, MaximumPitch, BlendStartThrottle, BlendEndThrottle;
        // Optional perceptual response; defaults reproduce v1 playback exactly.
        public float PitchResponseExponent = 1, ExhaustLoadRetention = .5f;
    }
}
