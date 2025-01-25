namespace Multiplayer.Networking.Packets.Common.Train;

public class CommonPaintThemePacket
{
    public ushort NetId { get; set; }
    public byte TargetArea { get; set; }
    public sbyte PaintThemeId { get; set; }
}
