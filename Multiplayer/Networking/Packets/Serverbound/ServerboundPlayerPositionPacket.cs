using UnityEngine;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundPlayerPositionPacket
{
    public Vector3 Position { get; set; }
    public Vector2 MoveDir { get; set; }
    public float RotationY { get; set; }
    public byte IsJumpingIsOnCar { get; set; }
}
