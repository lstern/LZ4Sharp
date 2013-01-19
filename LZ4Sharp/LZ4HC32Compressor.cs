using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LZ4Sharp
{
    using System.IO;

    using LZ4SharpCustom;

    class LZ4HC32Compressor : ILZ4Compressor
    {
        public int CalculateMaxCompressedLength(int uncompressedLength)
        {
            throw new NotImplementedException();
        }

        public byte[] Compress(byte[] source)
        {
            throw new NotImplementedException();
        }

        public int Compress(byte[] source, byte[] dest)
        {
            throw new NotImplementedException();
        }

        public int Compress(byte[] source, int srcOffset, int count, byte[] dest, int dstOffset)
        {
            throw new NotImplementedException();
        }

        public int Compress(byte[] source, Stream dest)
        {
            throw new NotImplementedException();
        }
    }
}
