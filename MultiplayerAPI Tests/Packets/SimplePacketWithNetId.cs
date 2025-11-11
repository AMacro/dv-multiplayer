using MPAPI.Interfaces.Packets;
using MultiplayerAPITest.Enums;
using UnityEngine;


namespace MultiplayerAPITest.Packets
{
    //Public properties are automatically serialised
    //acceptable types are:
    //    Primitives and inbuilt structs (bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, string, char, IPEndPoint, Guid)
    //    Arrays of primitives (e.g bool[], byte[], etc.
    //    Enums derived from primitives e.g. `enum MyEnum : byte`
    //    UnityEngine: Vector2, Vector3, Quarternion

    //Be mindful of the amount of data per packet.
    //  Avoid sending long strings or large structures
    //  Consider using a numeric Id system to identify objects
    //      The MP API provides Net Ids for common objects (e.g. TrainCars, Jobs, Switches, Turntables and RailTrack),
    //      see `TryGetNetId<T>(T obj, out ushort netId)`
    internal class SimplePacketWithNetId : IPacket
    {
        public ushort CarNetId { get; set; } // example use of a Net Id used to identify a TrainCar
        public Vector3 Position { get; set; }
        public WheelArrangement WheelArrangement { get; set; }
    }
}
