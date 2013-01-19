namespace LZ4SharpCustom
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Class for compressing a byte array into an LZ4 byte array.
    /// </summary>
    public unsafe class LZ4Compressor32 : ILZ4Compressor
    {
        //**************************************
        // Tuning parameters
        //**************************************
        // COMPRESSIONLEVEL :
        // Increasing this value improves compression ratio
        // Lowering this value reduces memory usage
        // Reduced memory usage typically improves speed, due to cache effect (ex : L1 32KB for Intel, L1 64KB for AMD)
        // Memory usage formula : N->2^(N+2) Bytes (examples : 12 -> 16KB ; 17 -> 512KB)
        const int COMPRESSIONLEVEL = 12;

        // NOTCOMPRESSIBLE_CONFIRMATION :
        // Decreasing this value will make the algorithm skip faster data segments considered "incompressible"
        // This may decrease compression ratio dramatically, but will be faster on incompressible data
        // Increasing this value will make the algorithm search more before declaring a segment "incompressible"
        // This could improve compression a bit, but will be slower on incompressible data
        // The default value (6) is recommended
        // 2 is the minimum value.
        const int NOTCOMPRESSIBLE_CONFIRMATION = 6;

        //**************************************
        // Constants
        //**************************************
        const int HASH_LOG = COMPRESSIONLEVEL;
        const int MAXD_LOG = 16;
        const int MAX_DISTANCE = ((1 << MAXD_LOG) - 1);
        const int MINMATCH = 4;
        const int MFLIMIT = (LZ4Util.COPYLENGTH + MINMATCH);
        const int MINLENGTH = (MFLIMIT + 1);
        const int HASHTABLESIZE = (1 << HASH_LOG);
        const int LASTLITERALS = 5;
        const int SKIPSTRENGTH = (NOTCOMPRESSIBLE_CONFIRMATION > 2 ? NOTCOMPRESSIBLE_CONFIRMATION : 2);
        const int SIZE_OF_LONG_TIMES_TWO_SHIFT = 4;
        const int STEPSIZE = 4;
        static byte[] DeBruijnBytePos = new byte[32] { 0, 0, 3, 0, 3, 1, 3, 0, 3, 2, 2, 1, 3, 2, 0, 1, 3, 3, 1, 2, 2, 2, 2, 0, 3, 1, 2, 0, 1, 0, 1, 1 };
        //**************************************
        // Macros
        //**************************************
        byte[] m_HashTable;


        public LZ4Compressor32()
        {
            m_HashTable = new byte[HASHTABLESIZE * IntPtr.Size];
            if (m_HashTable.Length % 16 != 0)
                throw new Exception("Hash table size must be divisible by 16");
        }


        public byte[] Compress(byte[] source)
        {
            int maxCompressedSize = CalculateMaxCompressedLength(source.Length);
            byte[] dst = new byte[maxCompressedSize];
            int length = Compress(source, dst);
            byte[] dest = new byte[length];
            Buffer.BlockCopy(dst, 0, dest, 0, length);
            return dest;
        }

        /// <summary>
        /// Calculate the max compressed byte[] size given the size of the uncompressed byte[]
        /// </summary>
        /// <param name="uncompressedLength">Length of the uncompressed data</param>
        /// <returns>The maximum required size in bytes of the compressed data</returns>
        public int CalculateMaxCompressedLength(int uncompressedLength)
        {
            return uncompressedLength + (uncompressedLength / 255) + 16;
        }

        /// <summary>
        /// Compress source into dest returning compressed length
        /// </summary>
        /// <param name="source">uncompressed data</param>
        /// <param name="dest">array into which source will be compressed</param>
        /// <returns>compressed length</returns>
        public int Compress(byte[] source, byte[] dest)
        {
            fixed (byte* s = source)
            fixed (byte* d = dest)
            {
                return this.Compress(s, d, source.Length, dest.Length);
            }
        }

        /// <summary>
        /// Compress source into dest returning compressed length
        /// </summary>
        /// <param name="source">uncompressed data</param>
        /// <param name="dest">array into which source will be compressed</param>
        /// <returns>compressed length</returns>
        public int Compress(byte[] source, Stream dest)
        {
            fixed (byte* s = source)
            {
                var writter = new BinaryWriter(dest);
                return this.CompressStream(s, source.Length, writter);
            }
        }

        /// <summary>
        /// Compress source into dest returning compressed length
        /// </summary>
        /// <param name="source">uncompressed data</param>
        /// <param name="srcOffset">offset in source array where reading will start</param>
        /// <param name="count">count of bytes in source array to compress</param>
        /// <param name="dest">array into which source will be compressed</param>
        /// <param name="dstOffset">start index in dest array where writing will start</param>
        /// <returns>compressed length</returns>
        public int Compress(byte[] source, int srcOffset, int count, byte[] dest, int dstOffset)
        {
            fixed (byte* s = &source[srcOffset])
            fixed (byte* d = &dest[dstOffset])
            {
                return this.Compress(s, d, count, dest.Length - dstOffset);
            }
        }

        int CompressStream(byte* source, int isize, BinaryWriter writter)
        {
            var buffer = new byte[MAX_DISTANCE];
            int size = 0;

            fixed (byte* bufferPtr = buffer)
            fixed (byte* hashTablePtr = m_HashTable)
            {
                // Clear(hashTablePtr, sizeof(byte*) * HASHTABLESIZE);
                byte** hashTable = (byte**)hashTablePtr;

                byte* ip = source;
                byte* myBuffer = bufferPtr;
                const int BasePtr = 0;

                byte* anchor = ip;
                byte* iend = ip + isize;
                byte* mflimit = iend - MFLIMIT;
                byte* matchlimit = iend - LASTLITERALS;

                // Init
                if (isize < MINLENGTH)
                {
                    // fim
                    return LastLiterals(writter, iend, anchor, myBuffer, bufferPtr, size);
                }

                // First Byte
                hashTable[(((*(uint*)ip) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))] = ip - BasePtr;
                ip++;
                uint forwardH = (*(uint*)ip * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG);

                // Main Loop
                for (;;)
                {
                    uint findMatchAttempts = (1U << SKIPSTRENGTH) + 3;
                    byte* forwardIp = ip;
                    byte* r;

                    // Find a match
                    do
                    {
                        uint step = findMatchAttempts++ >> SKIPSTRENGTH;
                        ip = forwardIp;
                        forwardIp = ip + step;

                        if (forwardIp > mflimit)
                        {
                            // fim
                            return LastLiterals(writter, iend, anchor, myBuffer, bufferPtr, size);
                        }

                        // LZ4_HASH_VALUE
                        r = hashTable[forwardH] + BasePtr;
                        hashTable[forwardH] = ip - BasePtr;
                        forwardH = (*(uint*)forwardIp * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG);
                    }
                    while ((r < ip - MAX_DISTANCE) || (*(uint*)r != *(uint*)ip));

                    // Catch up
                    while ((ip > anchor) && (r > source) && (ip[-1] == r[-1]))
                    {
                        ip--;
                        r--;
                    }

                    // Encode Literal Length
                    var length = (int)(ip - anchor);
                    byte myToken;

                    if (length >= (int)LZ4Util.RUN_MASK)
                    {
                        myToken = (byte)(LZ4Util.RUN_MASK << LZ4Util.ML_BITS);

                        int len = (int)(length - LZ4Util.RUN_MASK);
                        for (; len > 254; len -= 255)
                        {
                            *myBuffer++ = 255;
                        }

                        *myBuffer++ = (byte)len;
                    }
                    else
                    {
                        myToken = (byte)(length << LZ4Util.ML_BITS);
                    }

                    // Copy Literals
                    LZ4Util.CopyMemory(myBuffer, anchor, length);
                    myBuffer += length;

                    if (NextMatch2(writter, ref size, r, matchlimit, ref myToken, mflimit, hashTable, BasePtr, out anchor, /*ref op,*/ ref myBuffer, ref ip, ref forwardH, bufferPtr))
                    {
                        writter.Write(myToken);
                        size++;
                        return LastLiterals(writter, iend, anchor, myBuffer, bufferPtr, size);
                    }

                    writter.Write(myToken);
                    size++;
                    LZ4Util.CopyMemory(writter, bufferPtr, myBuffer - bufferPtr);
                    size += Convert.ToInt32(myBuffer - bufferPtr);
                    myBuffer = bufferPtr;
                }
            }
        }

        private static bool NextMatch2(
            BinaryWriter writter,
            ref int size,
            byte* r,
            byte* matchlimit,
            ref byte myToken,
            byte* mflimit,
            byte** hashTable,
            int basePtr,
            out byte* anchor,
            ref byte* myBuffer,
            ref byte* ip,
            ref uint forwardH,
            byte* bufferPtr)
        {
            while (true)
            {
                anchor = NextMatch(r, matchlimit, ref myBuffer, ref ip);

                int len = (int)(ip - anchor);

                // Encode MatchLength
                if (len >= (int)LZ4Util.ML_MASK)
                {
                    myToken += (byte)LZ4Util.ML_MASK;
                    len -= (byte)LZ4Util.ML_MASK;
                    for (; len > 509; len -= 510)
                    {
                        *myBuffer++ = 255;
                        *myBuffer++ = 255;
                    }

                    if (len > 254)
                    {
                        len -= 255;
                        *myBuffer++ = 255;
                    }

                    *myBuffer++ = (byte)len;
                }
                else
                {
                    myToken += (byte)len;
                }

                // Test end of chunk
                if (ip > mflimit)
                {
                    anchor = ip;
                    return true;
                }

                // Fill table
                hashTable[(((*(uint*)ip - 2) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))] = ip - 2 - basePtr;

                // Test next position
                r = basePtr + hashTable[(((*(uint*)ip) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))];
                hashTable[(((*(uint*)ip) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))] = ip - basePtr;
                if ((r > ip - (MAX_DISTANCE + 1)) && (*(uint*)r == *(uint*)ip))
                {
                    writter.Write(myToken);
                    size++;
                    LZ4Util.CopyMemory(writter, bufferPtr, myBuffer - bufferPtr);
                    size += Convert.ToInt32(myBuffer - bufferPtr);
                    myBuffer = bufferPtr;

                    myToken = 0;
                    continue;
                }

                // Prepare next loop
                anchor = ip++;
                forwardH = (*(uint*)ip * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG);
                return false;
            }
        }

        private static byte* NextMatch(byte* r, byte* matchlimit, ref byte* myBuffer, ref byte* ip)
        {
            // Encode Offset
            *(ushort*)myBuffer = (ushort)(ip - r);
            myBuffer += 2;

            // Start Counting
            ip += MINMATCH;
            r += MINMATCH; // MinMatch verified
            byte* anchor = ip;

            while (ip < matchlimit - (STEPSIZE - 1))
            {
                int diff = (int)(*(int*)r ^ *(int*)ip);
                if (diff == 0)
                {
                    ip += STEPSIZE;
                    r += STEPSIZE;
                    continue;
                }

                ip += DeBruijnBytePos[((uint)((diff & -diff) * 0x077CB531U)) >> 27];
                return anchor;
            }

            if ((ip < (matchlimit - 1)) && (*(ushort*)r == *(ushort*)ip))
            {
                ip += 2;
                r += 2;
            }

            if ((ip < matchlimit) && (*r == *ip))
            {
                ip++;
            }

            return anchor;
        }

        private static int LastLiterals(BinaryWriter writter, byte* iend, byte* anchor, byte* myBuffer, byte* bufferPtr, int size)
        {
            // Encode Last Literals
            int lastRun = (int)(iend - anchor);

            if (lastRun >= (int)LZ4Util.RUN_MASK)
            {
                *myBuffer++ = (byte)(LZ4Util.RUN_MASK << LZ4Util.ML_BITS);

                lastRun -= (byte)LZ4Util.RUN_MASK;
                for (; lastRun > 254; lastRun -= 255)
                {
                    *myBuffer++ = 255;
                }

                *myBuffer++ = (byte)lastRun;
            }
            else
            {
                *myBuffer++ = (byte)(lastRun << LZ4Util.ML_BITS);
            }

            LZ4Util.CopyMemory(myBuffer, anchor, iend - anchor);
            myBuffer += iend - anchor;
            LZ4Util.CopyMemory(writter, bufferPtr, myBuffer - bufferPtr);
            size += Convert.ToInt32(myBuffer - bufferPtr);

            // End
            return size;
        }

        int Compress(byte* source, byte* dest, int isize, int maxOutputSize)
        {
            fixed (byte* hashTablePtr = m_HashTable)
            fixed (byte* deBruijnBytePos = DeBruijnBytePos)
            {
                Clear(hashTablePtr, sizeof(byte*) * HASHTABLESIZE);
                byte** hashTable = (byte**)hashTablePtr;

                byte* ip = (byte*)source;
                int basePtr = 0;

                byte* anchor = ip;
                byte* iend = ip + isize;
                byte* mflimit = iend - MFLIMIT;
                byte* matchlimit = iend - LASTLITERALS;
                byte* oend = dest + maxOutputSize;

                byte* op = (byte*)dest;

                int len;
                const int skipStrength = SKIPSTRENGTH;

                // Init
                if (isize < MINLENGTH)
                {
                    goto _last_literals;
                }

                // First Byte
                hashTable[(((*(uint*)ip) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))] = ip - basePtr;
                ip++;
                uint forwardH = (*(uint*)ip * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG);

                // Main Loop
                for (; ; )
                {
                    uint findMatchAttempts = (1U << skipStrength) + 3;
                    byte* forwardIp = ip;
                    byte* r;

                    // Find a match
                    do
                    {
                        uint h = forwardH;
                        uint step = findMatchAttempts++ >> skipStrength;
                        ip = forwardIp;
                        forwardIp = ip + step;

                        if (forwardIp > mflimit)
                        {
                            goto _last_literals;
                        }

                        // LZ4_HASH_VALUE
                        forwardH = (*(uint*)forwardIp * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG);
                        r = hashTable[h] + basePtr;
                        hashTable[h] = ip - basePtr;
                    }
                    while ((r < ip - MAX_DISTANCE) || (*(uint*)r != *(uint*)ip));

                    // Catch up
                    while ((ip > anchor) && (r > source) && (ip[-1] == r[-1]))
                    {
                        ip--;
                        r--;
                    }

                    // Encode Literal Length
                    int length = (int)(ip - anchor);
                    byte* token = op++;
                    if (length >= (int)LZ4Util.RUN_MASK)
                    {
                        *token = (byte)(LZ4Util.RUN_MASK << LZ4Util.ML_BITS);
                        len = (int)(length - LZ4Util.RUN_MASK);
                        for (; len > 254; len -= 255)
                        {
                            *op++ = 255;
                        }
                        *op++ = (byte)len;
                    }
                    else
                    {
                        *token = (byte)(length << LZ4Util.ML_BITS);
                    }

                    //Copy Literals
                    {
                        byte* e = (op) + length;
                        do
                        {
                            *(uint*)op = *(uint*)anchor;
                            op += 4;
                            anchor += 4;

                            *(uint*)op = *(uint*)anchor;
                            op += 4;
                            anchor += 4;
                        }
                        while (op < e);
                        op = e;
                    }

                _next_match:
                    // Encode Offset
                    *(ushort*)op = (ushort)(ip - r);
                    op += 2;

                    // Start Counting
                    ip += MINMATCH;
                    r += MINMATCH; // MinMatch verified
                    anchor = ip;

                    while (ip < matchlimit - (STEPSIZE - 1))
                    {
                        int diff = (int)(*(int*)(r) ^ *(int*)(ip));
                        if (diff == 0)
                        {
                            ip += STEPSIZE;
                            r += STEPSIZE;
                            continue;
                        }

                        ip += DeBruijnBytePos[((uint)((diff & -diff) * 0x077CB531U)) >> 27];
                        goto _endCount;
                    }

                    if ((ip < (matchlimit - 1)) && (*(ushort*)(r) == *(ushort*)(ip)))
                    {
                        ip += 2;
                        r += 2;
                    }

                    if ((ip < matchlimit) && (*r == *ip))
                    {
                        ip++;
                    }

                _endCount:

                    len = (int)(ip - anchor);
                    if (op + (1 + LASTLITERALS) + (len >> 8) >= oend)
                    {
                        return 0; // Check output limit
                    }

                    // Encode MatchLength
                    if (len >= (int)LZ4Util.ML_MASK)
                    {
                        *token += (byte)LZ4Util.ML_MASK;
                        len -= (byte)LZ4Util.ML_MASK;
                        for (; len > 509; len -= 510)
                        {
                            *op++ = 255;
                            *op++ = 255;
                        }

                        if (len > 254)
                        {
                            len -= 255;
                            *op++ = 255;
                        }
                        *op++ = (byte)len;
                    }
                    else
                    {
                        *token += (byte)len;
                    }

                    // Test end of chunk
                    if (ip > mflimit)
                    {
                        anchor = ip;
                        break;
                    }

                    // Fill table
                    hashTable[(((*(uint*)ip - 2) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))] = ip - 2 - basePtr;

                    // Test next position
                    r = basePtr + hashTable[(((*(uint*)ip) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))];
                    hashTable[(((*(uint*)ip) * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG))] = ip - basePtr;
                    if ((r > ip - (MAX_DISTANCE + 1)) && (*(uint*)r == *(uint*)ip))
                    {
                        token = op++;
                        *token = 0;
                        goto _next_match;
                    }

                    // Prepare next loop
                    anchor = ip++;
                    forwardH = (*(uint*)ip * 2654435761U) >> ((MINMATCH * 8) - HASH_LOG);
                }

            _last_literals:
                // Encode Last Literals
                {
                    int lastRun = (int)(iend - anchor);
                    if (((byte*)op - dest) + lastRun + 1 + ((lastRun - 15) / 255) >= maxOutputSize)
                    {
                        return 0;
                    }

                    if (lastRun >= (int)LZ4Util.RUN_MASK)
                    {
                        *op++ = (byte)(LZ4Util.RUN_MASK << LZ4Util.ML_BITS);
                        lastRun -= (byte)LZ4Util.RUN_MASK;
                        for (; lastRun > 254; lastRun -= 255)
                        {
                            *op++ = 255;
                        }
                        *op++ = (byte)lastRun;
                    }
                    else
                    {
                        *op++ = (byte)(lastRun << LZ4Util.ML_BITS);
                    }

                    LZ4Util.CopyMemory(op, anchor, iend - anchor);
                    op += iend - anchor;
                }

                // End
                return (int)(((byte*)op) - dest);
            }
        }

        /// <summary>
        /// TODO: test if this is faster or slower than Array.Clear.
        /// </summary>
        /// <param name="array"></param>
        /// <param name="count"></param>
        static void Clear(byte* ptr, int count)
        {
            long* p = (long*)ptr;
            int longCount = count >> SIZE_OF_LONG_TIMES_TWO_SHIFT; // count / sizeof(long) * 2;
            while (longCount-- != 0)
            {
                *p++ = 0L;
                *p++ = 0L;
            }


            Debug.Assert(count % 16 == 0, "HashTable size must be divisible by 16");

            //for (int i = longCount << 4 ; i < count; i++)
            //    ptr[i] = 0;

        }
    }
}
