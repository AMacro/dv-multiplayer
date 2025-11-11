using MPAPI.Interfaces.Packets;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerAPITest.Packets
{
    internal class ComplexModPacket : ISerializablePacket
    {
        //Complex packets require manual serialization
        //Altenatively, implement methods to convert complex data structures to/from arrays and use the automatic serialization

        public Dictionary<string, Vector3> CarToPositionMap { get; set; }

        public void Deserialize(BinaryReader reader)
        {
            //retrieve the dictionary length
            var length = reader.ReadInt32();

            CarToPositionMap = [];

            //retrieve each key and value
            for (int i = 0; i < length; i++)
            {
                var key = reader.ReadString();
                var x = reader.ReadSingle();
                var y = reader.ReadSingle();
                var z = reader.ReadSingle();

                CarToPositionMap.Add(key, new Vector3 (x, y, z));
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            //write out the length of the dictionary
            writer.Write(CarToPositionMap.Count);

            //write out each key and value
            foreach (var kvp in CarToPositionMap)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value.x);
                writer.Write(kvp.Value.y);
                writer.Write(kvp.Value.z);
            }
        }
    }
}
