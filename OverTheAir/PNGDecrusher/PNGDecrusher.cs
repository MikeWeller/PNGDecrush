using Ionic.Zlib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web;

namespace OverTheAir.PNGDecrusher
{
    public class PNGDecrusher
    {
        public static void DecrushPNGStreamToStream(Stream input, Stream output)
        {
            IEnumerable<PNGChunk> chunks = PNGChunkParser.ChunksFromStream(input);
            IEnumerable<PNGChunk> decrushed = DecrushChunks(chunks);

            using (MemoryStream pngSoFar = new MemoryStream())
            {
                PNGChunkParser.WriteChunksWithHeader(decrushed, pngSoFar);
                pngSoFar.Position = 0;
                DecrushPixelsInPNG(pngSoFar, output);
            }
        }

        public static void DecrushPixelsInPNG(Stream pngInputStream, Stream outputStream)
        {
            using (Bitmap bitmap = new Bitmap(pngInputStream))
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);

                int totalBytes = bitmapData.Stride * bitmapData.Height;
                byte[] pixelData = new byte[totalBytes];

                bool hasAlpha;
                uint bytesPerPixel;

                switch (bitmapData.PixelFormat)
                {
                    case PixelFormat.Format32bppArgb:
                        hasAlpha = true;
                        bytesPerPixel = 4;
                        break;

                    case PixelFormat.Format32bppRgb:
                        hasAlpha = false;
                        bytesPerPixel = 3;
                        break;

                    default:
                        throw new InvalidDataException("Only 32 bit RGB(A) PNGs are supported by PNGDecrusher");
                }

                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelData, 0, pixelData.Length);

                for (uint i = 0; i < totalBytes; i += bytesPerPixel)
                {
                    // Swap R and B (0th and 2nd bytes)
                    byte temp = pixelData[i + 2];
                    pixelData[i + 2] = pixelData[i + 0];
                    pixelData[i + 0] = temp;

                    if (hasAlpha)
                    {
                        ReversePremultipliedAlpha(pixelData, i);
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bitmapData.Scan0, pixelData.Length);
                bitmap.UnlockBits(bitmapData);

                bitmap.Save(outputStream, ImageFormat.Png);
            }
        }

        private static void ReversePremultipliedAlpha(byte[] pixelData, uint startOffset)
        {
            byte red = pixelData[startOffset + 0];
            byte green = pixelData[startOffset + 1];
            byte blue = pixelData[startOffset + 2];
            byte alpha = pixelData[startOffset + 3];

            if (alpha == 0)
            {
                return;
            }

            red = (byte)((red * 255) / alpha);
            green = (byte)((green * 255) / alpha);
            blue = (byte)((blue * 255) / alpha);

            pixelData[startOffset + 0] = red;
            pixelData[startOffset + 1] = green;
            pixelData[startOffset + 2] = blue;
        }

        public static IEnumerable<PNGChunk> DecrushChunks(IEnumerable<PNGChunk> chunks)
        {
            IEnumerable<PNGChunk> chunksMinusAppleChunk = ChunksByRemovingAppleCgBIChunks(chunks);
            IEnumerable<PNGChunk> result = RecompressIDATChunksFromDeflateToZlib(chunksMinusAppleChunk);
            if (result.Count() == chunks.Count())
            {
                // there wasn't an Apple CgBI removed, throw an exception
                throw new InvalidDataException("Could not find a CgBI chunk");
            }

            return result;
        }

        private static IEnumerable<PNGChunk> RecompressIDATChunksFromDeflateToZlib(IEnumerable<PNGChunk> chunks)
        {
            // need to combine the data for multiple IDAT chunks and deflate it together
            // then split again at similar byte offsets... arg

            IEnumerable<PNGChunk> idatChunks = chunks.Where(c => c.Type == PNGChunk.ChunkType.IDAT);
            IEnumerable<int> splitOffsets = idatChunks.Select(c => c.Data.Length).Take(idatChunks.Count() - 1);

            byte[] combinedChunkData = CombinedChunkData(idatChunks);
            byte[] zlibFixed = RecompressDeflateDataAsZlib(combinedChunkData);

            IEnumerable<byte[]> newIDATDataChunks = SplitBufferAtOffsets(zlibFixed, splitOffsets);

            IEnumerable<PNGChunk> newIDATChunks = newIDATDataChunks.Select(c => IDATChunkFrombytes(c));

            List<PNGChunk> originalMinusIDAT = chunks.Where(c => c.Type != PNGChunk.ChunkType.IDAT).ToList();

            int indexOfFirstOriginalIdat = chunks.Select((c, i) => new { index = i, chunk = c }).First(o => o.chunk.Type == PNGChunk.ChunkType.IDAT).index;

            originalMinusIDAT.InsertRange(indexOfFirstOriginalIdat, newIDATChunks);

            return originalMinusIDAT;
        }

        private static PNGChunk IDATChunkFrombytes(byte[] bytes)
        {
            string type = PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT);
            return new PNGChunk(type, bytes, CalculateCRCForChunk(type, bytes));
        }

        public static IEnumerable<byte[]> SplitBufferAtOffsets(byte[] input, IEnumerable<int> offsets)
        {
            List<int> offsetsWithLastIndex = offsets.ToList();
            offsetsWithLastIndex.Add(input.Length);

            List<byte[]> result = new List<byte[]>();
            int nextPositionToCopyFrom = 0;
            int lastOffset = 0;

            foreach (int i in offsetsWithLastIndex)
            {
                byte[] chunk = new byte[i - lastOffset];
                Array.Copy(input, nextPositionToCopyFrom, chunk, 0, chunk.Length);
                nextPositionToCopyFrom += chunk.Length;
                lastOffset = i;
                result.Add(chunk);
            }

            return result;
        }

        private static byte[] CombinedChunkData(IEnumerable<PNGChunk> chunks)
        {
            int totalLength = chunks.Select(c => c.Data.Length).Sum();

            byte[] result = new byte[totalLength];
            int offset = 0;

            foreach (PNGChunk chunk in chunks)
            {
                chunk.Data.CopyTo(result, offset);
                offset += chunk.Data.Length;
            }

            return result;
        }

        private static byte[] RecompressDeflateDataAsZlib(byte[] input)
        {
            using (MemoryStream compressedData = new MemoryStream(input))
            using (System.IO.Compression.DeflateStream deflateStream = new System.IO.Compression.DeflateStream(compressedData, System.IO.Compression.CompressionMode.Decompress))
            using (ZlibStream zlibStream = new ZlibStream(deflateStream, Ionic.Zlib.CompressionMode.Compress))
            using (MemoryStream zlibCompressed = new MemoryStream())
            {
                zlibStream.CopyTo(zlibCompressed);
                return zlibCompressed.ToArray();
            }
        }

        private static PNGChunk ChunkByFixingZlibHeader(PNGChunk chunk)
        {
            // Apple's -iphone addition to png crush strips the zlib header and checksum, so we
            // deflate the chunk data and recompress as zlib

            using (MemoryStream compressedData = new MemoryStream(chunk.Data))
            using (System.IO.Compression.DeflateStream deflateStream = new System.IO.Compression.DeflateStream(compressedData, System.IO.Compression.CompressionMode.Decompress))
            using (ZlibStream zlibStream = new ZlibStream(deflateStream, Ionic.Zlib.CompressionMode.Compress))
            using (MemoryStream zlibCompressed = new MemoryStream())
            {
                zlibStream.CopyTo(zlibCompressed);

                byte[] chunkData = zlibCompressed.ToArray();

                uint crc32 = CalculateCRCForChunk(chunk.TypeString, chunkData);

                return new PNGChunk(chunk.TypeString, zlibCompressed.ToArray(), crc32);
            }
        }

        private static IEnumerable<PNGChunk> ChunksByRemovingAppleCgBIChunks(IEnumerable<PNGChunk> chunks)
        {
            return chunks.Where(c => c.Type != PNGChunk.ChunkType.CgBI);
        }

        public static uint CalculateCRCForChunk(string chunkType, byte[] chunkData)
        {
            byte[] chunkTypeBytes = Encoding.UTF8.GetBytes(chunkType);

            Ionic.Crc.CRC32 crc32calculator = new Ionic.Crc.CRC32();
            crc32calculator.SlurpBlock(chunkTypeBytes, 0, chunkTypeBytes.Length);
            crc32calculator.SlurpBlock(chunkData, 0, chunkData.Length);
            int crc32 = crc32calculator.Crc32Result;
            return (uint)crc32;
        }
    }
}