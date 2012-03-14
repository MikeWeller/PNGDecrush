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
    }
}