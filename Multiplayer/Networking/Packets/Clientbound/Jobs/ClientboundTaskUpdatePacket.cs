using DV.Logic.Job;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ClientboundTaskUpdatePacket
{
    public ushort JobNetId { get; set; }
    public ushort TaskNetId { get; set; }
    public TaskState NewState { get; set; }
    public float TaskStartTime { get; set; }
    public float TaskFinishTime { get; set; }
    public bool TaskStateUpdate { get; set; }
    public ushort CarNetID { get; set; }
    public bool ReplaceCar { get; set; }
    public ushort DestTrackId { get; set; }
    public bool ReplaceDestTrack { get; set; }

    public ClientboundTaskUpdatePacket Clone() => new()
    {
        TaskNetId = this.TaskNetId,
        JobNetId = this.JobNetId,
        TaskStateUpdate = this.TaskStateUpdate,
        NewState = this.NewState,
        TaskStartTime = this.TaskStartTime,
        TaskFinishTime = this.TaskFinishTime,
        ReplaceDestTrack = this.ReplaceDestTrack,
        DestTrackId = this.DestTrackId,
        ReplaceCar = this.ReplaceCar,
        CarNetID = this.CarNetID
    };
}
