namespace LZ4Sharp
{
    using LZ4SharpCustom;

    public static class LZ4DecompressorFactory
    {
        public static ILZ4Decompressor CreateNew()
        {
                return new LZ4Decompressor32();
        }
    }
}
