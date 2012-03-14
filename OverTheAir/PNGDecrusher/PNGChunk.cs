using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OverTheAir.PNGDecrusher
{
    public class PNGChunk
    {
        public enum ChunkType
        {
            Unknown,
            IDAT
        }

        public ChunkType Type { get; private set; }
        public string TypeString { get; private set; }
        public byte[] Data { get; private set; }
        public uint DataCRC { get; private set; }

        public PNGChunk(string type, byte[] data, uint dataCRC)
        {
            this.TypeString = type;
            this.Type = TypeFromString(type);
            this.Data = data;
            this.DataCRC = dataCRC;
        }

        private static PNGChunk.ChunkType TypeFromString(string type)
        {
            switch (type)
            {
                case "IDAT":
                    return PNGChunk.ChunkType.IDAT;

                default:
                    return PNGChunk.ChunkType.Unknown;
            }
        }
    }
}