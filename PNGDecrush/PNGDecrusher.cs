using Ionic.Crc;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace PNGDecrush
{
    public class PNGDecrusher
    {
        public static void Decrush(Stream input, Stream output)
        {
            using (MemoryStream fixedChunksOutput = new MemoryStream())
            {
                DecrushAtChunkLevel(input, fixedChunksOutput);
                DecrushAtPixelLevel(fixedChunksOutput, output);
            }
        }

        private static void DecrushAtChunkLevel(Stream input, Stream output)
        {
            IEnumerable<PNGChunk> fixedChunks = DecrushChunks(PNGChunkParser.ChunksFromStream(input));
            PNGChunkParser.WriteChunksAsPNG(fixedChunks, output);
        }

        public static IEnumerable<PNGChunk> DecrushChunks(IEnumerable<PNGChunk> chunks)
        {
            IEnumerable<PNGChunk> chunksWithoutAppleChunk = ChunksByRemovingAppleCgBIChunks(chunks);

            if (chunksWithoutAppleChunk.Count() == chunks.Count())
            {
                throw new InvalidDataException("Could not find a CgBI chunk. Image wasn't crushed with Apple's -iohone option.");
            }

            return ConvertIDATChunksFromDeflateToZlib(chunksWithoutAppleChunk); ;
        }

        private static IEnumerable<PNGChunk> ConvertIDATChunksFromDeflateToZlib(IEnumerable<PNGChunk> inputChunks)
        {
            // Multiple IDAT chunks must be combined together to form a single DEFLATE payload.
            // This payload is recompressed with zlib headers intact, and then split up into chunks again

            IEnumerable<PNGChunk> idatChunks = inputChunks.Where(c => c.Type == PNGChunk.ChunkType.IDAT);
            byte[] zlibData = RecompressedZlibDataFromChunks(idatChunks);

            IEnumerable<PNGChunk> newIDATChunks = CreateIdatChunksFromData(zlibData, idatChunks.Count());

            return ReplaceOldIdatChunksWithNewChunks(inputChunks, newIDATChunks);
        }

        private static IEnumerable<PNGChunk> CreateIdatChunksFromData(byte[] data, int numberOfChunks)
        {
            IEnumerable<byte[]> dataChunks = SplitBufferIntoChunks(data, numberOfChunks);
            return dataChunks.Select(chunkData => IDATChunkWithBytes(chunkData));
        }

        private static byte[] RecompressedZlibDataFromChunks(IEnumerable<PNGChunk> idatChunks)
        {
            byte[] deflateData = CombinedDataFromChunks(idatChunks);
            return ConvertDeflateToZlib(deflateData);
        }

        public static IEnumerable<byte[]> SplitBufferIntoChunks(byte[] input, int numberOfChunks)
        {
            List<byte[]> result = new List<byte[]>();

            int bytesLeft = input.Length;
            int currentInputIndex = 0;

            for (int chunksLeft = numberOfChunks; chunksLeft > 0; chunksLeft--)
            {
                int maxChunkSize = (int)Math.Ceiling((double)bytesLeft / (double)chunksLeft);
                int thisChunkSize = Math.Min(maxChunkSize, bytesLeft);

                byte[] chunkData = SubArray(input, currentInputIndex, thisChunkSize);

                currentInputIndex += thisChunkSize;
                bytesLeft -= thisChunkSize;

                result.Add(chunkData);
            }

            if (currentInputIndex != input.Length || bytesLeft != 0)
            {
                throw new InvalidOperationException();
            }

            return result;
        }

        private static TType[] SubArray<TType>(TType[] input, int startIndex, int count)
        {
            TType[] result = new TType[count];
            Array.Copy(input, startIndex, result, 0, result.Length);
            return result;
        }

        private static IEnumerable<PNGChunk> ReplaceOldIdatChunksWithNewChunks(IEnumerable<PNGChunk> chunks, IEnumerable<PNGChunk> newIdatChunks)
        {
            int indexOfFirstIdat = chunks.Select((c, i) => new { index = i, chunk = c })
                                         .First(o => o.chunk.Type == PNGChunk.ChunkType.IDAT)
                                         .index;

            List<PNGChunk> result = chunks.Where(c => c.Type != PNGChunk.ChunkType.IDAT).ToList();
            result.InsertRange(indexOfFirstIdat, newIdatChunks);
            return result;
        }

        private static PNGChunk IDATChunkWithBytes(byte[] bytes)
        {
            string type = PNGChunk.StringFromType(PNGChunk.ChunkType.IDAT);
            return new PNGChunk(type, bytes, CalculateCRCForChunk(type, bytes));
        }

        private static byte[] CombinedDataFromChunks(IEnumerable<PNGChunk> chunks)
        {
            int totalLength = chunks.Select(c => c.Data.Length).Sum();

            byte[] result = new byte[totalLength];
            int bytesWritten = 0;

            foreach (PNGChunk chunk in chunks)
            {
                chunk.Data.CopyTo(result, bytesWritten);
                bytesWritten += chunk.Data.Length;
            }

            return result;
        }

        private static byte[] ConvertDeflateToZlib(byte[] input)
        {
            // Basically, we wrap the deflate stram in a zlib format.
            // Because zlib includes a checksum of the decompressed data,
            // we need to decompress all data.

            // The zlib format is as follows:
            // zlib format (wrapper around the deflate format):
            // +---+---+
            // |CMF|FLG| (2 bytes)
            // +---+---+
            // +---+---+---+---+
            // |     DICTID    | (4 bytes. Present only when FLG.FDICT is set.) - Mostly not set
            // +---+---+---+---+
            // +=====================+
            // |...compressed data...| (variable size of data)
            // +=====================+
            // +---+---+---+---+
            // |     ADLER32   |  (4 bytes of checksum)
            // +---+---+---+---+
            //
            // +---+---+
            // |CMF|FLG|
            // +---+---+
            //
            // 78 01 - No Compression/low
            // 78 9C - Default Compression
            // 78 DA - Best Compression 

            byte[] bytes;

            using (MemoryStream compressedData = new MemoryStream(input))
            using (DeflateStream deflateStream = new DeflateStream(compressedData, CompressionMode.Decompress))
            using (MemoryStream decompressedData = new MemoryStream())
            {
                // Decompress all data
                deflateStream.CopyTo(decompressedData);
                bytes = decompressedData.ToArray();
            }

            using (MemoryStream recompressedData = new MemoryStream())
            {
                recompressedData.WriteByte(0x78);
                recompressedData.WriteByte(0x9C);
                using (var compressor = new DeflateStream(recompressedData, CompressionMode.Compress, true))
                {
                    compressor.Write(bytes, 0, bytes.Length);
                    compressor.Flush();
                }

                recompressedData.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(Adler32(bytes))), 0, sizeof(uint));

                return recompressedData.ToArray();
            }
        }


        // naive implementation of adler-32 checksum
        static int Adler32(byte[] bytes)
        {
            const uint a32mod = 65521;
            uint s1 = 1, s2 = 0;
            foreach (byte b in bytes)
            {
                s1 = (s1 + b) % a32mod;
                s2 = (s2 + s1) % a32mod;
            }
            return unchecked((int)((s2 << 16) + s1));
        }

        private static IEnumerable<PNGChunk> ChunksByRemovingAppleCgBIChunks(IEnumerable<PNGChunk> chunks)
        {
            return chunks.Where(c => c.Type != PNGChunk.ChunkType.CgBI);
        }

        public static uint CalculateCRCForChunk(string chunkType, byte[] chunkData)
        {
            byte[] chunkTypeBytes = Encoding.UTF8.GetBytes(chunkType);

            CRC32 crc32calculator = new CRC32();
            crc32calculator.SlurpBlock(chunkTypeBytes, 0, chunkTypeBytes.Length);
            crc32calculator.SlurpBlock(chunkData, 0, chunkData.Length);

            return (uint)crc32calculator.Crc32Result;
        }

        public static void DecrushAtPixelLevel(Stream pngInputStream, Stream outputStream)
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
            {
                FixPixelsInBuffer(pixelData, hasAlpha, bytesPerPixel);
            }
            System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bitmapData.Scan0, pixelData.Length);
        }

        private static void FixPixelsInBuffer(byte[] pixelData, bool hasAlpha, uint bytesPerPixel)
        {
            for (uint i = 0; i < pixelData.Length; i += bytesPerPixel)
            {
                ReverseRGBtoBGRByteSwap(pixelData, i);

                if (hasAlpha)
                {
                    ReversePremultipliedAlpha(pixelData, i);
                }
            }
        }

        private static void ReverseRGBtoBGRByteSwap(byte[] pixelData, uint i)
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
    }
}
