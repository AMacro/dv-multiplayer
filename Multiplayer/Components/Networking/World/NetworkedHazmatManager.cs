using System;
using System.Collections.Generic;
using System.IO;
using DV.ThingTypes;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.World;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

/// <summary>
/// Replicates DV's hazmat terrain grid (spilled liquids, terrain fires, corrosion, biohazard,
/// radiation) from the host to every client.
///
/// Why replicate instead of letting each client simulate:
/// HazmatTileManager is a cellular automaton stepped one phase per frame, and both ignition (random
/// chance + random delay) and reaction decay (frame delta time) are non-deterministic. Two machines
/// starting from identical state diverge within seconds, so fires would spread differently or not at
/// all. Instead the host is authoritative and clients only render: HazmatSimulationPatch neuters the
/// client's simulation phases and its grid-mutating entry points, and this manager streams the host's
/// tile state to them.
///
/// What makes this cheap: a tile's key (HazmatGridTile.gridPosition) is a packed grid coordinate derived
/// from an origin-shift-corrected world position, so it names the same patch of terrain on every machine
/// despite each client shifting its origin independently. Tiles can therefore be addressed by id, and the
/// payload is DV's own per-tile serializer (HazmatGridTile.SerializeData) — the same bytes the game
/// writes into a save.
///
/// Liquid spills are not forwarded from clients: the leaking car exists on the host too (cargo health is
/// synced), so the host's own particles produce the spill. Only ignition is forwarded, because a client
/// can ignite terrain with something the host cannot infer (a lighter).
/// </summary>
public class NetworkedHazmatManager : SingletonBehaviour<NetworkedHazmatManager>
{
    // Fires and spills evolve slowly by network standards; 4 Hz is plenty and keeps the diff scan off the
    // hot tick.
    private const int SYNC_INTERVAL_TICKS = NetworkLifecycle.TICK_RATE / 4;

    // Tiles per packet. A large spill can touch hundreds of tiles; chunking keeps a single packet at a
    // sane size and spreads the join backfill over several sends.
    private const int MAX_TILES_PER_PACKET = 128;

    // How often a client retries tiles it couldn't place because their terrain wasn't streamed in yet.
    private const float DEFERRED_RETRY_INTERVAL = 2f;

    /*
     * Server state
     */

    // Last state we told clients about, per tile. Also serves as the "tiles clients know about" set:
    // dropping out of it is what produces a removal.
    private readonly Dictionary<int, int> sentSignatures = new Dictionary<int, int>();

    private readonly List<HazmatGridTile> dirtyTiles = new List<HazmatGridTile>();
    private readonly List<int> removedTiles = new List<int>();

    /*
     * Client state
     */

    // Tiles whose terrain chunk isn't loaded yet (a fire on the far side of the map). Their blob is held
    // here — the host has already marked them sent and won't repeat itself — and retried until the
    // terrain streams in. Latest blob per tile wins.
    private readonly Dictionary<int, byte[]> deferredTiles = new Dictionary<int, byte[]>();
    private float nextDeferredRetry;

    protected override void Awake()
    {
        base.Awake();
        NetworkLifecycle.Instance.OnTick += Common_OnTick;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isQuitting)
            return;

        if (NetworkLifecycle.Instance != null)
            NetworkLifecycle.Instance.OnTick -= Common_OnTick;
    }

    private static HazmatTileManager TileManager => SingletonBehaviour<HazmatTileManager>.Instance;

    private void Common_OnTick(uint tick)
    {
        if (NetworkLifecycle.Instance.IsSinglePlayer)
            return;

        if (NetworkLifecycle.Instance.IsHost())
        {
            if (tick % SYNC_INTERVAL_TICKS == 0)
                Server_SendDirtyTiles();

            return;
        }

        if (deferredTiles.Count > 0 && Time.time >= nextDeferredRetry)
        {
            nextDeferredRetry = Time.time + DEFERRED_RETRY_INTERVAL;
            Client_RetryDeferredTiles();
        }
    }

    /*
     * Server
     */

    private void Server_SendDirtyTiles()
    {
        HazmatTileManager manager = TileManager;
        if (manager == null || manager.TileDictionary == null)
            return;

        dirtyTiles.Clear();
        removedTiles.Clear();

        foreach (KeyValuePair<int, HazmatGridTile> entry in manager.TileDictionary)
        {
            int signature = Signature(entry.Value);
            if (sentSignatures.TryGetValue(entry.Key, out int sent) && sent == signature)
                continue;

            sentSignatures[entry.Key] = signature;
            dirtyTiles.Add(entry.Value);
        }

        // Anything we've sent before that the host's CleanUp has since dropped.
        foreach (int coords in sentSignatures.Keys)
            if (!manager.TileDictionary.ContainsKey(coords))
                removedTiles.Add(coords);

        foreach (int coords in removedTiles)
            sentSignatures.Remove(coords);

        if (dirtyTiles.Count == 0 && removedTiles.Count == 0)
            return;

        // Removals ride with the first chunk; a removed tile is by definition not in the dirty set, so
        // there's no ordering hazard between the two.
        if (dirtyTiles.Count == 0)
        {
            NetworkLifecycle.Instance.Server.SendHazmatTiles(null, removedTiles.ToArray(), null);
            return;
        }

        for (int i = 0; i < dirtyTiles.Count; i += MAX_TILES_PER_PACKET)
        {
            int count = Mathf.Min(MAX_TILES_PER_PACKET, dirtyTiles.Count - i);
            NetworkLifecycle.Instance.Server.SendHazmatTiles(
                Serialize(dirtyTiles, i, count),
                i == 0 ? removedTiles.ToArray() : null,
                null);
        }
    }

    /// <summary>
    /// Send the whole grid to a joining player. Called on PlayerLoadingState.ReadyForTiles.
    /// </summary>
    public void Server_SendFullSnapshot(ServerPlayer player)
    {
        HazmatTileManager manager = TileManager;
        if (manager == null || manager.TileDictionary == null || manager.TileDictionary.Count == 0)
            return;

        var all = new List<HazmatGridTile>(manager.TileDictionary.Values);
        NetworkLifecycle.Instance.Server.LogDebug(() => $"Sending hazmat backfill of {all.Count} tiles to {player.Username}");

        for (int i = 0; i < all.Count; i += MAX_TILES_PER_PACKET)
        {
            int count = Mathf.Min(MAX_TILES_PER_PACKET, all.Count - i);
            NetworkLifecycle.Instance.Server.SendHazmatTiles(Serialize(all, i, count), null, player);
        }
    }

    /// <summary>
    /// Apply a client's ignition request to the host's real grid. The resulting tile state reaches
    /// everyone (including the requester) through the normal dirty-tile stream.
    /// </summary>
    public void Server_ReceiveIgnite(int gridPosition, float ignitionStrength)
    {
        HazmatTileManager manager = TileManager;
        if (manager == null)
            return;

        manager.IgniteTile(gridPosition, ignitionStrength);
    }

    /// <summary>
    /// Quantised fingerprint of everything we replicate, so float jitter in an effectively unchanged tile
    /// doesn't re-send it on every scan.
    /// </summary>
    private static int Signature(HazmatGridTile tile)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Mathf.RoundToInt(tile.currentWeight * 100f);
            hash = hash * 31 + Mathf.RoundToInt(tile.burnTime * 10f);
            hash = hash * 31 + Flags(tile);

            foreach (KeyValuePair<CargoType, float> liquid in tile.liquidContent)
                hash ^= ((int)liquid.Key * 397) ^ Mathf.RoundToInt(liquid.Value * 100f);

            return hash;
        }
    }

    private static int Flags(HazmatGridTile tile)
    {
        int flags = 0;
        if (tile.IsIgnited) flags |= 1;
        if (tile.IsCorroded) flags |= 2;
        if (tile.IsDefiled) flags |= 4;
        if (tile.IsRadiated) flags |= 8;
        return flags;
    }

    /*
     * Wire format
     *
     * int count, then per tile: int gridPosition, ushort blobLength, blob.
     *
     * The blob is HazmatGridTile.SerializeData's output. It's length-prefixed rather than written back to
     * back like DV does in a save file, because a client may fail to place an individual tile (its
     * terrain isn't loaded) and must be able to skip or stash it without losing its place in the stream.
     */

    private static byte[] Serialize(List<HazmatGridTile> tiles, int offset, int count)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        using (var tileStream = new MemoryStream())
        using (var tileWriter = new BinaryWriter(tileStream))
        {
            writer.Write(count);

            for (int i = offset; i < offset + count; i++)
            {
                HazmatGridTile tile = tiles[i];

                tileStream.SetLength(0);
                tile.SerializeData(tileWriter);
                tileWriter.Flush();

                writer.Write(tile.gridPosition);
                writer.Write((ushort)tileStream.Length);
                writer.Write(tileStream.GetBuffer(), 0, (int)tileStream.Length);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }

    /*
     * Client
     */

    public void Client_ReceiveTiles(ClientboundHazmatTilesPacket packet)
    {
        HazmatTileManager manager = TileManager;
        if (manager == null)
            return;

        if (packet.TileData != null && packet.TileData.Length > 0)
            Client_ApplyTiles(manager, packet.TileData);

        if (packet.RemovedTiles == null)
            return;

        foreach (int coords in packet.RemovedTiles)
        {
            deferredTiles.Remove(coords);
            Client_RemoveTile(manager, coords);
        }
    }

    private void Client_ApplyTiles(HazmatTileManager manager, byte[] data)
    {
        using (var stream = new MemoryStream(data))
        using (var reader = new BinaryReader(stream))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int coords = reader.ReadInt32();
                byte[] blob = reader.ReadBytes(reader.ReadUInt16());

                if (!Client_ApplyTile(manager, coords, blob))
                    deferredTiles[coords] = blob;
            }
        }
    }

    /// <summary>
    /// Place one tile. Returns false if it couldn't be placed *yet* — the terrain it sits on isn't
    /// streamed in, so the caller should hold onto it and retry.
    /// </summary>
    private static bool Client_ApplyTile(HazmatTileManager manager, int coords, byte[] blob)
    {
        HazmatGridTile tile;
        try
        {
            // autoCreate: the tile won't exist locally — the client never runs the simulation that would
            // have created it. This reads the terrain heightmap, so it throws (or produces nothing) when
            // the terrain chunk isn't loaded.
            tile = manager.GetTileFromCoords(coords, true);
        }
        catch (Exception)
        {
            return false;
        }

        if (tile == null)
            return false;

        // DeserializeData does liquidContent.Add(), which throws on a key that's already there. DV only
        // ever deserialises into a freshly created tile; we deserialise into the same tile repeatedly.
        tile.liquidContent.Clear();

        using (var stream = new MemoryStream(blob))
        using (var reader = new BinaryReader(stream))
            tile.DeserializeData(reader, HazmatTileManager.HAZMAT_DATA_VERSION);

        Client_ReconcileEffects(manager, tile);
        return true;
    }

    private void Client_RetryDeferredTiles()
    {
        HazmatTileManager manager = TileManager;
        if (manager == null)
            return;

        var placed = new List<int>();
        foreach (KeyValuePair<int, byte[]> deferred in deferredTiles)
            if (Client_ApplyTile(manager, deferred.Key, deferred.Value))
                placed.Add(deferred.Key);

        foreach (int coords in placed)
            deferredTiles.Remove(coords);
    }

    /// <summary>
    /// Bring a tile's visuals in line with the state just deserialised into it. This mirrors what
    /// HazmatTileManager.DeserializeFromStream does when loading a save, plus the teardown half that the
    /// load path never needs: a tile can stop burning, and the client's own CleanUp is disabled.
    /// </summary>
    private static void Client_ReconcileEffects(HazmatTileManager manager, HazmatGridTile tile)
    {
        if (tile.IsIgnited)
        {
            // Idempotent — restarts the existing effect rather than spawning a second one.
            manager.BurnTerrainTile(tile);
        }
        else if (tile.terrainFireEffects != null)
        {
            tile.terrainFireEffects.RemoveEffects();
            tile.terrainFireEffects = null;
            manager.IgnitedTileCoords.Remove(tile.gridPosition);
        }

        if (tile.IsCorroded)
        {
            manager.CorrodeTerrainTile(tile);
        }
        else if (tile.terrainCorrosiveEffects != null)
        {
            tile.terrainCorrosiveEffects.RemoveEffects();
            tile.terrainCorrosiveEffects = null;
        }

        if (tile.IsDefiled)
        {
            manager.DefileTile(tile);
        }
        else if (tile.terrainBiohazardEffects != null)
        {
            tile.terrainBiohazardEffects.RemoveEffects();
            tile.terrainBiohazardEffects = null;
        }

        // Queue the terrain splat (the wet/scorched texture blend). PrepareAllTerrainSplats and
        // ApplyTerrainSplats still run on the client — only the state-evolving phases are disabled.
        manager.PrepareTerrainPaintData(HazmatTileManager.LIQUID_TEXTURE_INDEX, tile.currentWeight, tile.gridPosition);
    }

    private static void Client_RemoveTile(HazmatTileManager manager, int coords)
    {
        if (!manager.TileDictionary.TryGetValue(coords, out HazmatGridTile tile))
            return;

        if (tile.terrainFireEffects != null)
            tile.terrainFireEffects.RemoveEffects();
        if (tile.terrainCorrosiveEffects != null)
            tile.terrainCorrosiveEffects.RemoveEffects();
        if (tile.terrainBiohazardEffects != null)
            tile.terrainBiohazardEffects.RemoveEffects();

        manager.IgnitedTileCoords.Remove(coords);
        manager.TileDictionary.Remove(coords);
        manager.tileList.RemoveAll(kvp => kvp.Key == coords);

        // Wash the liquid texture back off the terrain.
        manager.PrepareTerrainPaintData(HazmatTileManager.LIQUID_TEXTURE_INDEX, 0f, coords);
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedHazmatManager)}]";
    }
}
