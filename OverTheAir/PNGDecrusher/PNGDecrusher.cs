using Ionic.Zlib;
using System;
using System.Collections.Generic;
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
            PNGChunkParser.WriteChunksWithHeader(decrushed, output);
        }

        public static IEnumerable<PNGChunk> DecrushChunks(IEnumerable<PNGChunk> chunks)
        {
            IEnumerable<PNGChunk> result = FixZlibHeadersForIdatChunks(ChunksByRemovingAppleCgBIChunks(chunks));
            if (result.Count() == chunks.Count())
            {
                // there wasn't an Apple CgBI removed, throw an exception
                throw new InvalidDataException("Could not find a CgBI chunk");
            }

            return result;
        }

        private static IEnumerable<PNGChunk> FixZlibHeadersForIdatChunks(IEnumerable<PNGChunk> chunks)
        {
            return chunks.Select(c =>
                {
                    if (c.Type == PNGChunk.ChunkType.IDAT)
                    {
                        return ChunkByFixingZlibHeader(c);
                    }
                    else
                    {
                        return c;
                    }
                });
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