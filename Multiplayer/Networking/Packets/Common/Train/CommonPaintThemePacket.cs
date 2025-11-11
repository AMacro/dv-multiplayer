using DV.Customization.Paint;

namespace Multiplayer.Networking.Packets.Common.Train;

public class CommonPaintThemePacket
{
    public ushort NetId { get; set; }
    public TrainCarPaint.Target TargetArea { get; set; }
    public uint PaintThemeId { get; set; }
}
