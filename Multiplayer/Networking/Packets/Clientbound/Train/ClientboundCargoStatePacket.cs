using DV.ThingTypes;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundCargoStatePacket
{
    public ushort NetId { get; set; }
    public bool IsLoading { get; set; }
    public CargoType CargoType { get; set; }
    public float CargoAmount { get; set; }
    public float CargoHealth { get; set; }
    public byte CargoModelIndex { get; set; }
    public ushort WarehouseMachineNetId { get; set; }
}
