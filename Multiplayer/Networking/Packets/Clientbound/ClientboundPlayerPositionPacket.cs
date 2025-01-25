using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerPositionPacket
{
    public byte Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector2 MoveDir { get; set; }
    public float RotationY { get; set; }
    public byte IsJumpingIsOnCar { get; set; }

    public bool IsJumping => (IsJumpingIsOnCar & 1) != 0;
    public bool IsOnCar => (IsJumpingIsOnCar & 2) != 0;
}
