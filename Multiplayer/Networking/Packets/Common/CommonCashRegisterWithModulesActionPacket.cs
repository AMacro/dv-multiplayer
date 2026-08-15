
namespace Multiplayer.Networking.Packets.Common;

public enum CashRegisterAction : byte
{
    Cancel,
    Buy,
    AddCash,
    SetFunds,
    RejectGeneric,
    RejectFunds,
    RejectedNoItems,
    Approve,
    ShopStockChanged
}
public class CommonCashRegisterWithModulesActionPacket
{
    public ushort NetId { get; set; }
    public CashRegisterAction Action { get; set; }
    public double Amount { get; set; }
    public byte BuyerPlayerId { get; set; }
    public string[] ItemPrefabNames { get; set; } = [];
    public int[] ItemAmounts { get; set; } = [];
    public float[] ItemUnitPrices { get; set; } = [];
}
