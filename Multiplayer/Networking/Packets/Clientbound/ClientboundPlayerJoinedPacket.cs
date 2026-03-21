using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerJoinedPacket
{
    public byte PlayerId { get; set; }
    public string Username { get; set; }
    public string CrewName { get; set; } = string.Empty;
    //public byte[] Guid { get; set; }
    public ushort CarID { get; set; }
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
}
