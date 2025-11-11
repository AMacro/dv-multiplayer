using MPAPI.Interfaces.Packets;
using MultiplayerAPITest.Enums;
using UnityEngine;

namespace MultiplayerAPITest.Packets
{
    internal class SimplePacket : IPacket
    {
        //Public properties are automatically serialised
        //acceptable types are:
        //    Primitives (bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, string, char, IPEndPoint, Guid)
        //    Arrays of primitives
        //    Enums derived from primitives e.g. `enum MyEnum : byte`
        //    UnityEngine: Vector2, Vector3, Quaternion

        //Be mindful of the amount of data per packet.
        //  Avoid sending long strings or large structures
        //  Consider using a numeric Id system to represent objects.
        //      The MP API provides Net Ids for common objects (e.g. TrainCars, Jobs, Switches, Turntables and RailTrack),
        //      see `TryGetNetId<T>(T obj, out ushort netId)`
        public string CarId { get; set; }   //It's better to use ushort. See SimplePacketWithNetId for an example
        public Vector3 Position {  get; set; }
        public WheelArrangement WheelArrangement { get; set; }

    }
}
