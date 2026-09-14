namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundShopStockPacket
{
    public string[] PrefabNames { get; set; }
    public int[] PurchasedCounts { get; set; }
    public int[] AllowedCounts { get; set; }
}
