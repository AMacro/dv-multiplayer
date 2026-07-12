using Multiplayer.Networking.Data.Player;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerJoinedPacket
{
    public byte PlayerId { get; set; }
    public string Username { get; set; }
    public bool IsVR { get; set; }
    public string CharacterId { get; set; }
    public string CrewName { get; set; } = string.Empty;
    public ushort CarID { get; set; }
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
    public float LookPosition { get; set; }
    public float SitHeight { get; set; }
}
