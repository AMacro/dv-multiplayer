namespace Multiplayer.Networking.Packets.Common;

public enum ShopAction : byte
{
    Purchase,
    Approved,
    RejectGeneric,
    RejectFunds,
    RejectNoItems,
    StockChanged,
    StockSnapshot,
}

public sealed class CommonShopPacket
{
    public ushort RegisterNetId { get; set; }
    public uint RequestId { get; set; }
    public ShopAction Action { get; set; }
    public double Amount { get; set; }
    public byte BuyerPlayerId { get; set; }
    public string[] ItemPrefabNames { get; set; } = [];
    public int[] ItemAmounts { get; set; } = [];
    public float[] ItemUnitPrices { get; set; } = [];
}
