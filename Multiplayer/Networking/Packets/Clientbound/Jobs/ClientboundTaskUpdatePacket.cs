using DV.Logic.Job;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

internal class ClientboundTaskUpdatePacket
{
    public ushort TaskNetId { get; set; }
    public TaskState NewState { get; set; }
    public float TaskStartTime { get; set; }
    public float TaskFinishTime { get; set; }
}
