namespace Multiplayer.Networking.Packets.Serverbound.World;

// Client -> host request to ignite a terrain tile (a lighter thrown into a spill, sparks, an explosion).
// Clients don't run the hazmat simulation, so every local ignition attempt is blocked and forwarded here
// instead; the host ignites its real tile and the result comes back as a ClientboundHazmatTilesPacket.
//
// GridPosition is HazmatTileManager's packed grid coordinate. It's derived from an origin-shift-corrected
// world position, so it means the same tile on every machine.
public class ServerboundHazmatIgnitePacket
{
    public int GridPosition { get; set; }
    public float IgnitionStrength { get; set; }
}
