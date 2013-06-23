using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace PNGDecrush
{
    public class PNGChunk
    {
        public enum ChunkType
        {
            Unknown,
            IDAT,
            CgBI // apple's pngcrush -iphone chunk
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

        public static string StringFromType(ChunkType type)
        {
            return type.ToString();
        }

        public static PNGChunk.ChunkType TypeFromString(string type)
        {
            switch (type)
            {
                case "IDAT":
                    return ChunkType.IDAT;

                case "CgBI":
                    return ChunkType.CgBI;

                default:
                    return PNGChunk.ChunkType.Unknown;
            }
        }
    }
}