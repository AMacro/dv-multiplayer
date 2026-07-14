namespace Multiplayer.Networking.Packets.Clientbound.World;

// Host -> client snapshot of the hazmat terrain grid (spilled liquids, fires, corrosion, biohazard,
// radiation). See NetworkedHazmatManager for the format and why the grid is replicated rather than
// simulated on each client.
public class ClientboundHazmatTilesPacket
{
    // Tiles whose state changed, in NetworkedHazmatManager's wire format:
    //   int count, then per tile: int gridPosition + HazmatGridTile.SerializeData() blob.
    // Used for both the join backfill (every tile) and the periodic deltas.
    public byte[] TileData { get; set; }

    // Packed grid coords of tiles the host's simulation dropped entirely (fire burnt out and the
    // liquid is gone). The client removes their effects and forgets them.
    public int[] RemovedTiles { get; set; }
}
