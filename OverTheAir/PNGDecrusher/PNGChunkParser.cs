using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace OverTheAir.PNGDecrusher
{
    public class PNGChunkParser
    {
        private static byte[] _PNGHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static IEnumerable<PNGChunk> ChunksFromStream(Stream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (!TryReadPNGHeaderFromReader(reader))
                {
                    throw new InvalidDataException("Could not find the PNG header");
                }

                List<PNGChunk> result = new List<PNGChunk>();
                PNGChunk chunk;
                while ((chunk = ReadChunk(reader)) != null)
                {
                    result.Add(chunk);
                }

                return result;
            }
        }

        private static PNGChunk ReadChunk(BinaryReader reader)
        {
            if (ReaderIsAtEndOfFile(reader))
            {
                return null;
            }

            uint length = reader.ReadUInt32NetworkByteOrder();
            string type = Encoding.UTF8.GetString(reader.ReadBytes(4));
            byte[] data = reader.ReadBytes((int)length);
            uint crc = reader.ReadUInt32NetworkByteOrder();

            return new PNGChunk(type, data, crc);
        }

        private static bool ReaderIsAtEndOfFile(BinaryReader reader)
        {
            return reader.PeekChar() == -1;
        }

        private static bool TryReadPNGHeaderFromReader(BinaryReader reader)
        {
            byte[] actualHeader = reader.ReadBytes(_PNGHeader.Length);

            return ((IStructuralEquatable)actualHeader).Equals(_PNGHeader, StructuralComparisons.StructuralEqualityComparer);
        }

        public static void WriteChunksAsPNG(IEnumerable<PNGChunk> chunks, Stream output)
        {
            using (BinaryWriter writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                writer.Write(_PNGHeader);

                foreach (PNGChunk chunk in chunks)
                {
                    WriteChunkToWriter(chunk, writer);
                }
            }
        }

        private static void WriteChunkToWriter(PNGChunk chunk, BinaryWriter writer)
        {
            writer.WriteNetworkOrder((uint)chunk.Data.Length);
            
            byte[] typeString = Encoding.UTF8.GetBytes(chunk.TypeString);
            if (typeString.Length != 4)
            {
                throw new InvalidDataException("PNG chunk type must be a 4 character string");
            }

            writer.Write(typeString);
            writer.Write(chunk.Data);
            writer.WriteNetworkOrder(chunk.DataCRC);
        }
    }
}