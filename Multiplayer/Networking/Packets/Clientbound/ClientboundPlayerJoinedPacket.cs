using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerJoinedPacket
{
    public byte Id { get; set; }
    public string Username { get; set; }
    //public byte[] Guid { get; set; }
    public ushort CarID { get; set; }
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
}
