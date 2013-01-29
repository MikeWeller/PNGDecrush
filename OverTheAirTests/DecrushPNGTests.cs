using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OverTheAir.PNGDecrusher;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Text;

namespace OverTheAirTests
{
    [TestClass]
    public class DecrushPNGTests
    {
        [TestMethod]
        public void fdaf()
        {
            // What does apple do:
            //
            // * Adds the CgBI chunk to the start of the file
            // * Removes the zlib header+checksum from IDAT chunks (leaving just raw deflate data)
            // * RGB/RGBA images are stored in BGR/BGRA order
            // * Image pixels are premultiplied with the alpha. 
            //

            // We want to use as much of the standard .net class libraries as possible

            // So, what we could do is:
            //
            // * Remove the CgBI chunk
            // * Inflate the chunk data and deflate again with the headers
            //
            // * Read this new PNG into .net's standard image manipulation classes
            // * Reverse the byte swap
            // * Reverse the premultiplied alpha
            // * Done?

            // Other notes:
            //
            // * We need to make sure we're dealing with RGB or RGBA, fail on anything else
            // * We want to avoid having to deal with precompression row filters etc. so we just fix the zlib headers and then defer to .net
        }

        byte[] singleIDAT10x10White = 
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
                0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x0a, 0x00, 0x00, 0x00, 0x0a,
                0x08, 0x02, 0x00, 0x00, 0x00, 0x02, 0x50, 0x58, 0xea, 0x00, 0x00, 0x00,
                0x01, 0x73, 0x52, 0x47, 0x42, 0x00, 0xae, 0xce, 0x1c, 0xe9, 0x00, 0x00,
                0x00, 0x04, 0x67, 0x41, 0x4d, 0x41, 0x00, 0x00, 0xb1, 0x8f, 0x0b, 0xfc,
                0x61, 0x05, 0x00, 0x00, 0x00, 0x09, 0x70, 0x48, 0x59, 0x73, 0x00, 0x00,
                0x0e, 0xc3, 0x00, 0x00, 0x0e, 0xc3, 0x01, 0xc7, 0x6f, 0xa8, 0x64, 0x00,
                0x00, 0x00, 0x11, 0x49, 0x44, 0x41, 0x54, 0x28, 0x53, 0x63, 0xf8, 0x8f,
                0x17, 0x30, 0x8c, 0x4a, 0x63, 0x0b, 0x01, 0x00, 0xfa, 0x1e, 0x2a, 0xe4,
                0x91, 0xb5, 0xdf, 0x86, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44,
                0xae, 0x42, 0x60, 0x82
            };

        [TestMethod]
        public void TestChunksAreCorrectlyParsed()
        {
            using (MemoryStream stream = new MemoryStream(singleIDAT10x10White))
            {
                PNGChunk[] chunks = PNGDecrusher.ChunksFromStream(stream).ToArray();
                Assert.AreEqual(6, chunks.Length);

                PNGChunk idatChunk = chunks[4];
                Assert.AreEqual("IDAT", idatChunk.TypeString);
                Assert.AreEqual(PNGChunk.ChunkType.IDAT, idatChunk.Type);
                Assert.AreEqual(17, idatChunk.Data.Length);
            }
        }

        [TestMethod]
        public void TestWritingChunksProducesIdenticalBytesAsInput()
        {
            byte[] input = singleIDAT10x10White;
            using (MemoryStream stream = new MemoryStream(singleIDAT10x10White))
            {
                PNGChunk[] chunks = PNGDecrusher.ChunksFromStream(stream).ToArray();

                MemoryStream output = new MemoryStream();
                PNGDecrusher.WriteChunksWithHeader(chunks, output);

                byte[] outputBytes = output.ToArray();

                CollectionAssert.AreEqual(input, outputBytes);
            }
        }
    }
}
