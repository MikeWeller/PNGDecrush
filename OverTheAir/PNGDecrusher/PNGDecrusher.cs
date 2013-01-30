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
        public static void Decrush(Stream input, Stream output)
        {
            IEnumerable<PNGChunk> decrushedChunks = DecrushChunksFromStream(input);
            ReverseApplePixelOptimizationInChunks(decrushedChunks, output);
        }

        private static IEnumerable<PNGChunk> DecrushChunksFromStream(Stream input)
        {
            return DecrushChunks(PNGChunkParser.ChunksFromStream(input));
        }

        private static void ReverseApplePixelOptimizationInChunks(IEnumerable<PNGChunk> pngChunks, Stream output)
        {
            using (MemoryStream pngSoFar = new MemoryStream())
            {
                PNGChunkParser.WriteChunksWithHeader(pngChunks, pngSoFar);
                pngSoFar.Position = 0;
                ReverseApplePixelOptimizations(pngSoFar, output);
            }
        }

        public static void ReverseApplePixelOptimizations(Stream pngInputStream, Stream outputStream)
        {
            using (Bitmap bitmap = new Bitmap(pngInputStream))
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                {
                    FixPixelsInBitmapData(bitmapData);
                }
                bitmap.UnlockBits(bitmapData);
                bitmap.Save(outputStream, ImageFormat.Png);
            }
        }

        private static void FixPixelsInBitmapData(BitmapData bitmapData)
        {
            int totalBytes = bitmapData.Stride * bitmapData.Height;
            byte[] pixelData = new byte[totalBytes];

            bool hasAlpha = BitmapDataHasAlpha(bitmapData);
            uint bytesPerPixel = BytesPerPixelFromBitmapData(bitmapData);

            System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelData, 0, pixelData.Length);

            for (uint i = 0; i < totalBytes; i += bytesPerPixel)
            {
                ReverseAppleByteSwap(pixelData, i);

                if (hasAlpha)
                {
                    ReversePremultipliedAlpha(pixelData, i);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bitmapData.Scan0, pixelData.Length);
        }

        private static void ReverseAppleByteSwap(byte[] pixelData, uint i)
        {
            byte temp = pixelData[i + 2];
            pixelData[i + 2] = pixelData[i + 0];
            pixelData[i + 0] = temp;
        }

        private static uint BytesPerPixelFromBitmapData(BitmapData bitmapData)
        {
            switch (bitmapData.PixelFormat)
            {
                case PixelFormat.Format32bppArgb:
                    return 4;

                case PixelFormat.Format32bppRgb:
                    return 3;

                default:
                    throw new InvalidDataException("Only 32 bit RGB(A) PNGs are supported by PNGDecrusher");
            }
        }

        private static bool BitmapDataHasAlpha(BitmapData bitmapData)
        {
            switch (bitmapData.PixelFormat)
            {
                case PixelFormat.Format32bppArgb:
                    return true;

                case PixelFormat.Format32bppRgb:
                    return false;

                default:
                    throw new InvalidDataException("Only 32 bit RGB(A) PNGs are supported by PNGDecrusher");
            }
        }

        private static void ReversePremultipliedAlpha(byte[] pixelData, uint startOffset)
        {
            // premultipliedValue = originalValue * (alpha / 255)
            //                    = (originalValue * alpha) / 255
            // therefore
            //
            // oldValue = (premultipliedValue * 255) / alpha

            byte alpha = pixelData[startOffset + 3];

            if (alpha == 0)
            {
                return;
            }

            pixelData[startOffset + 0] = (byte)((pixelData[startOffset + 0] * 255) / alpha);
            pixelData[startOffset + 1] = (byte)((pixelData[startOffset + 1] * 255) / alpha);
            pixelData[startOffset + 2] = (byte)((pixelData[startOffset + 2] * 255) / alpha);
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

        private static IEnumerable<PNGChunk> RecompressIDATChunksFromDeflateToZlib(IEnumerable<PNGChunk> inputChunks)
        {
            // need to combine the data for multiple IDAT chunks and deflate it together
            // then split again at similar byte offsets... arg

            IEnumerable<PNGChunk> idatChunks = inputChunks.Where(c => c.Type == PNGChunk.ChunkType.IDAT);

            byte[] combinedChunkData = CombinedChunkData(idatChunks);
            byte[] zlibFixed = RecompressDeflateDataAsZlib(combinedChunkData);

            IEnumerable<int> offsetsWeNeedToSplitAt = idatChunks.Select(c => c.Data.Length).Take(idatChunks.Count() - 1);
            IEnumerable<byte[]> newIDATDataChunks = SplitBufferAtOffsets(zlibFixed, offsetsWeNeedToSplitAt);

            IEnumerable<PNGChunk> newIDATChunks = newIDATDataChunks.Select(bytes => IDATChunkFrombytes(bytes));

            int indexOfFirstOriginalIdat = inputChunks.Select((c, i) => new { index = i, chunk = c }).First(o => o.chunk.Type == PNGChunk.ChunkType.IDAT).index;

            List<PNGChunk> result = inputChunks.Where(c => c.Type != PNGChunk.ChunkType.IDAT).ToList();
            result.InsertRange(indexOfFirstOriginalIdat, newIDATChunks);
            return result;
        }

        private static PNGChunk IDATChunkFrombytes(byte[] bytes)
        {
            string type = PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT);
            return new PNGChunk(type, bytes, CalculateCRCForChunk(type, bytes));
        }

        public static IEnumerable<byte[]> SplitBufferAtOffsets(byte[] input, IEnumerable<int> offsets)
        {
            List<int> offsetsIncludingEndOfBuffer = offsets.ToList();
            offsetsIncludingEndOfBuffer.Add(input.Length);

            List<byte[]> result = new List<byte[]>();
            int nextPositionToCopyFrom = 0;
            int lastOffset = 0;

            foreach (int i in offsetsIncludingEndOfBuffer)
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