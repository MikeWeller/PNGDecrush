using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace OverTheAir.PNGDecrusher
{
    public static class BinaryReaderNetworkHostOrderAdditions
    {
        public static uint ReadUInt32NetworkByteOrder(this BinaryReader reader)
        {
            return (uint)System.Net.IPAddress.NetworkToHostOrder((int)reader.ReadUInt32());
        }

        public static void WriteNetworkOrder(this BinaryWriter writer, uint value)
        {
            writer.Write((uint)System.Net.IPAddress.HostToNetworkOrder((int)value));
        }
    }
}