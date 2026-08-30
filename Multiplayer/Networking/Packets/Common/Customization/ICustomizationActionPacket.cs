namespace Multiplayer.Networking.Packets.Common.Customization;

internal interface ICustomizationActionPacket
{
    uint OriginActionId { get; set; }
    ICustomizationActionPacket Copy();
}

internal abstract class CustomizationActionPacket : ICustomizationActionPacket
{
    public uint OriginActionId { get; set; }

    public ICustomizationActionPacket Copy() =>
        (ICustomizationActionPacket)MemberwiseClone();
}
