using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundPlayerPositionPacket
{
    public Vector3 Position { get; set; }
    public Vector2 MoveDir { get; set; }
    public float RotationY { get; set; }
    public float LookPosition { get; set; }
    public float SitHeight { get; set; }
    public bool IsOnCar { get; set; }
    public PlayerPostureFlags Posture { get; set; }
    public ushort CarID { get; set; }
}
