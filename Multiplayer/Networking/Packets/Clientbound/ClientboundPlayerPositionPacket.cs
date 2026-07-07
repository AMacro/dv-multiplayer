using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerPositionPacket
{
    public byte PlayerId { get; set; }
    public Vector3 Position { get; set; }
    public Vector2 MoveDir { get; set; }
    public float RotationY { get; set; }
    public float LookPosition { get; set; }
    public PlayerPostureFlags Posture { get; set; }
    public bool IsOnCar { get; set; }
    public ushort CarID { get; set; }
}
