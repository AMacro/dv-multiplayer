
namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ClientboundWarehouseControllerUpdatePacket
{
    public ushort NetId { get; set; }
    public bool IsLoading { get; set; }
    public ushort JobNetId { get; set; }    
    public ushort CarNetId { get; set; }
    public ushort CargoType { get; set; }
    public ushort Preset { get; set; }
}
