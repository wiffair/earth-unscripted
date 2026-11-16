using System;
using System.Security.Cryptography;
using UnityEngine;
using EarthUnscripted.FunnyAnimals.VehicleCaptureProposal;

namespace EarthUnscripted.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Owned bounded-buffer SHA256 stream for captured source positions/indices/state.
    /// Not a provider/authentication issuer. Native identities are only opaque process
    /// equality tokens and are separately retained privately for reference comparison.
    /// Dispose releases crypto resources; never touches borrowed Unity objects.
    /// </summary>
    internal sealed class PresentationSignatureWriter : IDisposable
    {
        private const int BufferBytes = 4096;
        private readonly HashAlgorithm hash = SHA256.Create();
        private readonly byte[] buffer = new byte[BufferBytes];
        private int count;
        private bool finished;

        internal void WriteInt(int value)
        {
            if (count > buffer.Length - sizeof(int)) Flush();
            unchecked
            {
                buffer[count++] = (byte)value;
                buffer[count++] = (byte)(value >> 8);
                buffer[count++] = (byte)(value >> 16);
                buffer[count++] = (byte)(value >> 24);
            }
        }

        internal void WriteOpaqueIdentity(UnityEngine.Object value)
        {
            // No pointer/raw native object escapes. Token bit layout is never interpreted.
            ulong identity = value ? EntityId.ToULong(value.GetEntityId()) : 0;
            WriteInt(unchecked((int)identity));
            WriteInt(unchecked((int)(identity >> 32)));
        }

        internal void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));
        internal void WriteVector(Vector3 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
        }
        /// <summary>Hash derived double bounds without rounding them to float.</summary>
        internal void WriteBounds(OccupiedBounds value)
        {
            WriteDouble(value.Minimum.X); WriteDouble(value.Minimum.Y); WriteDouble(value.Minimum.Z);
            WriteDouble(value.Maximum.X); WriteDouble(value.Maximum.Y); WriteDouble(value.Maximum.Z);
        }
        private void WriteDouble(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            WriteInt(unchecked((int)bits));
            WriteInt(unchecked((int)(bits >> 32)));
        }
        internal void WriteMatrix(Matrix4x4 value)
        {
            for (int index = 0; index < 16; index++) WriteFloat(value[index]);
        }
        internal void WriteDigest(byte[] digest)
        {
            WriteInt(digest.Length);
            foreach (byte value in digest)
            {
                if (count == buffer.Length) Flush();
                buffer[count++] = value;
            }
        }

        internal byte[] Finish()
        {
            if (finished) throw new InvalidOperationException("Signature already completed");
            hash.TransformFinalBlock(buffer, 0, count);
            finished = true;
            return (byte[])hash.Hash.Clone();
        }

        internal static string Hex(byte[] digest) => BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();

        private void Flush()
        {
            if (finished) throw new InvalidOperationException("Signature already completed");
            hash.TransformBlock(buffer, 0, count, buffer, 0);
            count = 0;
        }

        public void Dispose() => hash.Dispose();
    }
}
