using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LZ4.Tests
{
    using System.IO;
    using System.Linq;

    using LZ4SharpCustom;

    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var s = "Nothing fancy here the server get's the message processes it if needed and then sends back results but to keep it simple the server only gets messages, buffering not needed here really but it will be added as in real life scenario server never just reads data from the client.";
            s += s;
            s += s;
            s += s;
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var sw = new MemoryStream(new byte[bytes.Length]);
            var compress = LZ4.Compress(bytes);
            var size = LZ4CompressorFactory.CreateNew().Compress(bytes, sw);

            var memory = new MemoryStream(compress);
            var decompressed = new byte[bytes.Length];
            var decompress = LZ4DecompressorFactory.CreateNew().DecompressFromReader(memory, decompressed, bytes.Length);

            Assert.IsTrue(bytes.SequenceEqual(decompressed));
        }
    }
}
