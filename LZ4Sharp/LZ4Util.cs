namespace LZ4SharpCustom
{
    using System.IO;

    /// <summary>
    /// Constants and methods shared by LZ4Compressor and LZ4Decompressor
    /// </summary>
    internal class LZ4Util
    {
        //**************************************
        // Constants
        //**************************************
        public const int COPYLENGTH = 8;
        public const int ML_BITS = 4;
        public const uint ML_MASK = ((1U << ML_BITS) - 1);
        public const int RUN_BITS = (8 - ML_BITS);
        public const uint RUN_MASK = ((1U << RUN_BITS) - 1);

        public static unsafe void CopyMemory(byte* dst, BinaryReader src, long length)
        {
            while (length >= 16)
            {
                *(ulong*)dst = src.ReadUInt64();
                dst += 8;
                *(ulong*)dst = src.ReadUInt64();
                dst += 8;
                length -= 16;
            }

            if (length >= 8)
            {
                *(ulong*)dst = src.ReadUInt64();
                dst += 8;
                length -= 8;
            }

            if (length >= 4)
            {
                *(uint*)dst = src.ReadUInt32();
                dst += 4;
                length -= 4;
            }

            if (length >= 2)
            {
                *(ushort*)dst = src.ReadUInt16();
                dst += 2;
                length -= 2;
            }

            if (length != 0)
            {
                *dst = src.ReadByte();
            }
        }

        public static unsafe void CopyMemory(BinaryWriter writer, byte* src, long length)
        {
            while (length >= 16)
            {
                writer.Write(*(ulong*)src);
                src += 8;
                writer.Write(*(ulong*)src);
                src += 8;
                length -= 16;
            }

            if (length >= 8)
            {
                writer.Write(*(ulong*)src);
                src += 8;
                length -= 8;
            }

            if (length >= 4)
            {
                writer.Write(*(uint*)src);
                src += 4;
                length -= 4;
            }

            if (length >= 2)
            {
                writer.Write(*(ushort*)src);
                src += 2;
                length -= 2;
            }

            if (length != 0)
            {
                writer.Write(*src);
            }

            writer.Flush();
        }

        public static unsafe void CopyMemory(byte* dst, byte* src, long length)
        {
            while (length >= 16)
            {
                *(ulong*)dst = *(ulong*)src;
                dst += 8;
                src += 8;
                *(ulong*)dst = *(ulong*)src;
                dst += 8;
                src += 8;
                length -= 16;
            }

            if (length >= 8)
            {
                *(ulong*)dst = *(ulong*)src;
                dst += 8;
                src += 8;
                length -= 8;
            }

            if (length >= 4)
            {
                *(uint*)dst = *(uint*)src;
                dst += 4;
                src += 4;
                length -= 4;
            }

            if (length >= 2)
            {
                *(ushort*)dst = *(ushort*)src;
                dst += 2;
                src += 2;
                length -= 2;
            }

            if (length != 0)
            {
                *dst = *src;
            }
        }
    }
}
