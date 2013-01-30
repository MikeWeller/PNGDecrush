using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OverTheAir.PNGDecrusher;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Text;
using System.IO.Compression;
using System.Drawing;

namespace OverTheAirTests
{
    [TestClass]
    public class DecrushPNGTests
    {
        // What apple's pngcrush extension (-iphone) does:
        //
        // * Adds the CgBI chunk to the start of the file
        // * Removes the zlib header+checksum from IDAT chunks (leaving just raw deflate data)
        // * RGB/RGBA images are stored in BGR/BGRA order
        // * Image pixels are premultiplied with the alpha. 
        //

        // Why lots of existing solutions are broken:
        //
        // * They do not reverse the precompression line filters before byte swapping.
        //   This will cause sometimes subtle, sometimes not-so-subtle artifacts in the resulting image
        // * They do not handle multiple IDAT chunks
        //

        // So, what we do is:
        //
        // * Remove the CgBI chunk
        // * Inflate the chunk data and deflate again with the zlib headers
        // * Read this new PNG into .net's standard image manipulation classes
        // * Reverse the byte swap
        // * Reverse the premultiplied alpha
        // * Done!
        //

        byte[] Simple10x10WhitePNG = 
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

        byte[] Crushed10x10White =  
            {
                0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x04,
                0x43, 0x67, 0x42, 0x49, 0x50, 0x00, 0x20, 0x06, 0x2c, 0xb8, 0x77, 0x66,
                0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x0a,
                0x00, 0x00, 0x00, 0x0a, 0x08, 0x06, 0x00, 0x00, 0x00, 0x8d, 0x32, 0xcf,
                0xbd, 0x00, 0x00, 0x00, 0x09, 0x70, 0x48, 0x59, 0x73, 0x00, 0x00, 0x0b,
                0x13, 0x00, 0x00, 0x0b, 0x13, 0x01, 0x00, 0x9a, 0x9c, 0x18, 0x00, 0x00,
                0x00, 0x0b, 0x49, 0x44, 0x41, 0x54, 0x63, 0xf8, 0x4f, 0x24, 0x60, 0x18,
                0x55, 0x48, 0x5f, 0x85, 0x00, 0x31, 0xe7, 0x5b, 0x95, 0x00, 0x00, 0x00,
                0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82
            };

        // somehow i messed up, and this actually contains pixels with RGBA(4, 50, 255, 127)
        // NOT pure blue
        byte[] CrushedFiftyPercentAlphaKindaBlue10x10 =  {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x04,
            0x43, 0x67, 0x42, 0x49, 0x50, 0x00, 0x20, 0x02, 0x2b, 0xd5, 0xb3, 0x7f,
            0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x0a,
            0x00, 0x00, 0x00, 0x0a, 0x08, 0x06, 0x00, 0x00, 0x00, 0x8d, 0x32, 0xcf,
            0xbd, 0x00, 0x00, 0x00, 0x09, 0x70, 0x48, 0x59, 0x73, 0x00, 0x00, 0x0b,
            0x13, 0x00, 0x00, 0x0b, 0x13, 0x01, 0x00, 0x9a, 0x9c, 0x18, 0x00, 0x00,
            0x00, 0x0e, 0x49, 0x44, 0x41, 0x54, 0x63, 0xa8, 0x97, 0x64, 0xaa, 0x27,
            0x06, 0x33, 0x8c, 0x2a, 0xa4, 0xaf, 0x42, 0x00, 0xf9, 0xa6, 0x03, 0x21,
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82
        };

        [TestMethod]
        public void TestChunksAreCorrectlyParsed()
        {
            using (MemoryStream stream = new MemoryStream(Simple10x10WhitePNG))
            {
                PNGChunk[] chunks = PNGChunkParser.ChunksFromStream(stream).ToArray();
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
            byte[] input = Simple10x10WhitePNG;
            using (MemoryStream stream = new MemoryStream(Simple10x10WhitePNG))
            {
                PNGChunk[] chunks = PNGChunkParser.ChunksFromStream(stream).ToArray();

                MemoryStream output = new MemoryStream();
                PNGChunkParser.WriteChunksWithHeader(chunks, output);

                byte[] outputBytes = output.ToArray();

                CollectionAssert.AreEqual(input, outputBytes);
            }
        }

        [TestMethod]
        public void TestWritingChunksAfterRecalculatingCRCProducesIdenticalBytesAsInput()
        {
            byte[] input = Simple10x10WhitePNG;
            using (MemoryStream stream = new MemoryStream(Simple10x10WhitePNG))
            {
                PNGChunk[] chunks = PNGChunkParser.ChunksFromStream(stream).ToArray();
                chunks = chunks.Select(c =>
                {
                    return new PNGChunk(c.TypeString, c.Data, PNGDecrusher.CalculateCRCForChunk(c.TypeString, c.Data));
                }).ToArray();

                MemoryStream output = new MemoryStream();
                PNGChunkParser.WriteChunksWithHeader(chunks, output);

                byte[] outputBytes = output.ToArray();

                CollectionAssert.AreEqual(input, outputBytes);
            }
        }

        [TestMethod]
        public void TestAppleChunkIsRemovedWhenDecrushing()
        {
            IEnumerable<PNGChunk> chunks = new List<PNGChunk>()
            {
                new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.CgBI), new byte[] { 0x01, 0x02, 0x03 }, 12345),
                new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT), DeflateBytes(new byte[] { 0x01, 0x02, 0x03 }), 12345),
                new PNGChunk("IEND", new byte[] { }, 0xFFFFFF)
            };

            IEnumerable<PNGChunk> fixedChunks = PNGDecrusher.DecrushChunks(chunks);

            Assert.AreEqual(2, fixedChunks.Count());
            Assert.AreEqual(PNGChunk.ChunkType.IDAT, fixedChunks.ToArray()[0].Type);
            Assert.AreEqual("IEND", fixedChunks.ToArray()[1].TypeString);
        }

        [TestMethod]
        public void TestStrippedDeflateHeadersAreAddedBack()
        {
            IEnumerable<PNGChunk> chunks = PNGChunkParser.ChunksFromStream(new MemoryStream(Crushed10x10White));

            IEnumerable<PNGChunk> decrushedChunks = PNGDecrusher.DecrushChunks(chunks);
            PNGChunk decrushedIdatChunk = decrushedChunks.First(c => c.Type == PNGChunk.ChunkType.IDAT);

            using (MemoryStream idatDataStream = new MemoryStream(decrushedIdatChunk.Data))
            {
                // basic check of the zlib header
                // http://tools.ietf.org/html/rfc1950#page-4
                int CMF = idatDataStream.ReadByte();
                int FLG = idatDataStream.ReadByte();

                int compressionMethod = CMF & 0x0F;
                int compressionInfo = (CMF & 0xF0) >> 4;

                int COMPRESSION_METHOD_DEFLATE = 8;
                Assert.AreEqual(COMPRESSION_METHOD_DEFLATE, compressionMethod);
               
                using (DeflateStream deflateStream = new DeflateStream(idatDataStream, CompressionMode.Decompress))
                using (MemoryStream decompressedStream = new MemoryStream())
                {
                    deflateStream.CopyTo(decompressedStream);
                    Assert.AreEqual(410, decompressedStream.Length);
                }
            }
        }

        private byte[] DeflateBytes(byte[] input)
        {
            using (MemoryStream originalDataStream = new MemoryStream(input))
            using (MemoryStream deflatedDataStream = new MemoryStream())
            using (DeflateStream deflateStream = new DeflateStream(deflatedDataStream, CompressionMode.Compress, true))
            {
                originalDataStream.CopyTo(deflateStream);
                deflateStream.Close();

                return deflatedDataStream.ToArray();
            }
        }

        private byte[] DecompressZlibBytes(byte[] input)
        {
            using (MemoryStream zlibDataStream = new MemoryStream(input))
            {
                // ignore zlib header
                zlibDataStream.ReadByte();
                zlibDataStream.ReadByte();

                using (DeflateStream deflateStream = new DeflateStream(zlibDataStream, CompressionMode.Decompress))
                using (MemoryStream dezlibbedStream = new MemoryStream())
                {
                    deflateStream.CopyTo(dezlibbedStream);
                    return dezlibbedStream.ToArray();
                }
            }
        }

        [TestMethod]
        public void TestDecompressedDecrushedIDATDataIsSameAsOriginalDeflatedData()
        {
            byte[] originalData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
            byte[] deflatedOriginalData = DeflateBytes(originalData);

            List<PNGChunk> chunks = new List<PNGChunk>()
                {
                    new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.CgBI), new byte[] { }, 0),
                    new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT), deflatedOriginalData, 0)
                };

            IEnumerable<PNGChunk> decrushedChunks = PNGDecrusher.DecrushChunks(chunks);
            Assert.AreEqual(1, decrushedChunks.Count());

            byte[] zlibData = decrushedChunks.First().Data;
            byte[] dezlibbedData = DecompressZlibBytes(zlibData);

            Assert.AreEqual(originalData.Length, dezlibbedData.Length);
            CollectionAssert.AreEqual(originalData, dezlibbedData);
        }

        [TestMethod]
        public void TestMultipleIDATChunksAreTreatedAsASingleDeflatedBuffer()
        {
            byte[] originalData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xA0 };
            byte[] deflateOriginalData = DeflateBytes(originalData);

            int splitAtIndex = 4;
            byte[] chunk1Data = deflateOriginalData.Take(splitAtIndex).ToArray();
            byte[] chunk2Data = deflateOriginalData.Skip(splitAtIndex).ToArray();

            List<PNGChunk> chunks = new List<PNGChunk>()
                {
                    new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.CgBI), new byte[] { }, 0),
                    new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT), chunk1Data, 0),
                    new PNGChunk(PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT), chunk2Data, 0)
                };

            IEnumerable<PNGChunk> decrushedChunks = PNGDecrusher.DecrushChunks(chunks);

            byte[] zlibChunk1 = decrushedChunks.First().Data;
            byte[] zlibChunk2 = decrushedChunks.Last().Data;

            byte[] zlibChunksCombined = new byte[zlibChunk1.Length + zlibChunk2.Length];
            zlibChunk1.CopyTo(zlibChunksCombined, 0);
            zlibChunk2.CopyTo(zlibChunksCombined, zlibChunk1.Length);

            byte[] dezlibbedData = DecompressZlibBytes(zlibChunksCombined);

            Assert.AreEqual(originalData.Length, dezlibbedData.Length);
            CollectionAssert.AreEqual(originalData, dezlibbedData);
        }

        private Bitmap BitmapFromDecrushedImage(byte[] imageData)
        {
            using (MemoryStream crushed = new MemoryStream(imageData))
            using (MemoryStream decrushed = new MemoryStream())
            {
                PNGDecrusher.DecrushPNGStreamToStream(crushed, decrushed);
                decrushed.Position = 0;
                return new Bitmap(decrushed);
            }
        }

        [TestMethod]
        public void TestDecrushedImageCanBeLoadedByDotNetImageClasses()
        {
            using (Bitmap bitmap = BitmapFromDecrushedImage(CrushedFiftyPercentAlphaKindaBlue10x10))
            {
                Assert.AreEqual(bitmap.Size, new Size(10, 10));
                Assert.AreEqual(System.Drawing.Imaging.PixelFormat.Format32bppArgb, bitmap.PixelFormat);
                Assert.AreEqual(System.Drawing.Imaging.ImageFormat.Png, bitmap.RawFormat);
            }
        }

        [TestMethod]
        public void Debugging()
        {
            string inputFilePath = @"c:\Users\mike\Dropbox\OverTheAirTestFiles\Icon-72@2x_crushed.png";
            string outputFilePath = @"c:\Users\mike\Dropbox\OverTheAirTestFiles\_test_decrushed.png";

            using (FileStream inputStream = new FileStream(inputFilePath, FileMode.Open))
            using (FileStream outputStream = new FileStream(outputFilePath, FileMode.Create))
            {
                PNGDecrusher.DecrushPNGStreamToStream(inputStream, outputStream);
            }

            IEnumerable<PNGChunk> inputChunks = PNGChunkParser.ChunksFromStream(new FileStream(inputFilePath, FileMode.Open));
            IEnumerable<PNGChunk> outputChunks = PNGChunkParser.ChunksFromStream(new FileStream(outputFilePath, FileMode.Open));

        }

        [TestMethod]
        public void TestDecrushedImagesHavePremultipliedAlphaAndPixelByteOrderFixed()
        {
            using (Bitmap bitmap = BitmapFromDecrushedImage(CrushedFiftyPercentAlphaKindaBlue10x10))
            {
                Assert.AreEqual(bitmap.Size, new Size(10, 10));

                Color pixel = bitmap.GetPixel(4, 4);
                Assert.AreEqual(Color.FromArgb(127, 4, 50, 255), pixel);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidDataException))]
        public void TestExceptionOnNonAppleCrushedImages()
        {
            using (MemoryStream crushed = new MemoryStream(Simple10x10WhitePNG))
            using (MemoryStream decrushed = new MemoryStream())
            {
                PNGDecrusher.DecrushPNGStreamToStream(crushed, decrushed);
            }
        }

        [TestMethod]
        public void TestCRCCalculation()
        {
            byte[][] validPNGs = 
            {
                 Simple10x10WhitePNG,
                 Crushed10x10White,
                 CrushedFiftyPercentAlphaKindaBlue10x10
            };

            foreach (byte[] png in validPNGs)
            {
                IEnumerable<PNGChunk> chunks = PNGChunkParser.ChunksFromStream(new MemoryStream(Simple10x10WhitePNG));
                foreach (PNGChunk chunk in chunks)
                {
                    uint expectedCRC = chunk.DataCRC;
                    uint recalculatedCRC = PNGDecrusher.CalculateCRCForChunk(chunk.TypeString, chunk.Data);
                    Assert.AreEqual(expectedCRC, recalculatedCRC);
                }
            }
        }

        [TestMethod]
        public void TestSplitBufferWithNoOffsetsReturnsOriginalBuffer()
        {
            byte[] data = new byte[] { 0, 1, 2 };
            IEnumerable<byte[]> split = PNGDecrusher.SplitBufferAtOffsets(data, new List<int>());
            Assert.AreEqual(1, split.Count());
            CollectionAssert.AreEqual(data, split.First());
        }

        [TestMethod]
        public void TestSplitBufferWithOffsetsReturnsCorrectBuffers()
        {
            byte[] data = new byte[] { 0, 1, 2 };
            IEnumerable<byte[]> split = PNGDecrusher.SplitBufferAtOffsets(data, new List<int>() { 1 });
            Assert.AreEqual(2, split.Count());

            CollectionAssert.AreEqual(new byte[] { 0 }, split.First());
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, split.Last());
        }
    }
}
