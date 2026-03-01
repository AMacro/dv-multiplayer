using Multiplayer.Networking.Data.World;

namespace Multiplayer.Networking.Packets.Common;

public class CommonPitStopInteractionPacket
{
    public uint Tick { get; set; }
    public ushort NetId { get; set; }
    public PitStopStationInteractionType InteractionType { get; set; }
    public int ResourceType { get; set; }
    public float Value { get; set; }
}
