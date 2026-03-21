namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerPreferencesUpdatePacket
{
    public byte PlayerId { get; set; }
    public string CrewName { get; set; } = string.Empty;
}
