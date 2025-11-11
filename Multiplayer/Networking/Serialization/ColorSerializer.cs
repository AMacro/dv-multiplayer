using LiteNetLib.Utils;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Networking.Serialization
{
    public class ColorSerializer
    {
        public static void Serialize(NetDataWriter writer, Color colour)
        {
            writer.Put(colour.ColorToUInt32());
        }

        public static Color Deserialize(NetDataReader reader)
        {
            var colour = reader.GetUInt();

            return colour.UInt32ToColor();
        }
    }

}
