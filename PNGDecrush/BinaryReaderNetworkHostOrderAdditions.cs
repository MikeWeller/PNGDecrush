using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace PNGDecrush
{
    public static class BinaryReaderNetworkHostOrderAdditions
    {
        public static uint ReadUInt32NetworkByteOrder(this BinaryReader reader)
        {
            return (uint)System.Net.IPAddress.NetworkToHostOrder((int)reader.ReadUInt32());
        }

        public static int ReadInt32NetworkByteOrder(this BinaryReader reader)
        {
            return System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
        }

        public static void WriteNetworkOrder(this BinaryWriter writer, uint value)
        {
            writer.Write((uint)System.Net.IPAddress.HostToNetworkOrder((int)value));
        }

        public static void WriteNetworkOrder(this BinaryWriter writer, int value)
        {
            writer.Write(System.Net.IPAddress.HostToNetworkOrder(value));
        }
    }
}