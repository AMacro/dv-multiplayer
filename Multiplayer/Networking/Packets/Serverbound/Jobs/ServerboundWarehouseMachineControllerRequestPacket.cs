using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Serverbound.Jobs;

public class ServerboundWarehouseMachineControllerRequestPacket
{
    public ushort NetId { get; set; }
    public WarehouseAction WarehouseAction { get; set; }
}
