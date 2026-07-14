using System.Collections.Generic;
using DV.ThingTypes;
using HarmonyLib;
using Multiplayer.Components.Networking;
using UnityEngine;

namespace Multiplayer.Patches.World.Hazmat;

/// <summary>
/// Makes the hazmat terrain grid host-authoritative.
///
/// DV's HazmatTileManager is a cellular automaton with random ignition chances and frame-delta-driven
/// decay, so it cannot be run independently on each machine and stay in agreement — two clients would
/// grow different fires from the same spill. The host therefore keeps simulating, and on clients this
/// patch disables both the simulation phases and every entry point that mutates the grid.
/// NetworkedHazmatManager streams the host's tile state in instead.
///
/// Deliberately left running on the client: PrepareAllTerrainSplats and ApplyTerrainSplats. Those only
/// paint the terrain from tile state, and NetworkedHazmatManager depends on them to render what it
/// receives.
///
/// Ignition is the one thing forwarded upstream — a client can set terrain alight with a lighter, which
/// the host has no other way of knowing about.
/// </summary>
[HarmonyPatch(typeof(HazmatTileManager))]
public static class HazmatSimulationPatch
{
    // A client's own sparks/explosions try to ignite tiles the host is igniting anyway. Ignition is
    // idempotent, but forwarding every attempt during e.g. sustained wheelslip would be a packet storm,
    // so the same tile is only requested once per cooldown.
    private const float IGNITE_REQUEST_COOLDOWN = 1f;
    private static readonly Dictionary<int, float> lastIgniteRequest = new Dictionary<int, float>();

    /// <summary>
    /// True when we're a client that must not touch the grid. False for the host (which includes single
    /// player, where the host is the only player) so vanilla behaviour is completely untouched there.
    /// </summary>
    private static bool IsRemoteClient =>
        NetworkLifecycle.Instance != null &&
        NetworkLifecycle.Instance.IsClientRunning &&
        !NetworkLifecycle.Instance.IsHost();

    /*
     * Simulation phases — the cellular automaton itself.
     */

    [HarmonyPatch(nameof(HazmatTileManager.EvolveLiquidCellularAutomaton))]
    [HarmonyPrefix]
    private static bool EvolveLiquidCellularAutomaton() => !IsRemoteClient;

    [HarmonyPatch(nameof(HazmatTileManager.PrepareReaction))]
    [HarmonyPrefix]
    private static bool PrepareReaction() => !IsRemoteClient;

    [HarmonyPatch(nameof(HazmatTileManager.ProcessReaction))]
    [HarmonyPrefix]
    private static bool ProcessReaction() => !IsRemoteClient;

    // The host tells us which tiles to drop (ClientboundHazmatTilesPacket.RemovedTiles); removing them
    // locally on our own schedule would delete tiles the host still considers alive.
    [HarmonyPatch(nameof(HazmatTileManager.CleanUp))]
    [HarmonyPrefix]
    private static bool CleanUp() => !IsRemoteClient;

    /*
     * Grid mutation entry points.
     */

    // Drains the queue that OnParticleCollision fills. Blocked together with the queue itself below, so
    // nothing accumulates.
    [HarmonyPatch(nameof(HazmatTileManager.UpdateTileLiquidContentExternal))]
    [HarmonyPrefix]
    private static bool UpdateTileLiquidContentExternal() => !IsRemoteClient;

    // Leaking cargo spraying particles onto the terrain. Not forwarded to the host: the leaking car
    // exists there too (cargo health is synced), so the host's own particles produce the same spill and
    // send it back to us as tile state.
    [HarmonyPatch(nameof(HazmatTileManager.UpdateTileLiquidToBeAddedDictionary))]
    [HarmonyPrefix]
    private static bool UpdateTileLiquidToBeAddedDictionary() => !IsRemoteClient;

    // Both IgniteTile overloads and every Igniter path funnel through here, so it's the one place to
    // intercept ignition.
    [HarmonyPatch(nameof(HazmatTileManager.IgniteTerrainTile))]
    [HarmonyPrefix]
    private static bool IgniteTerrainTile(HazmatGridTile tile, float ignitionStrength, ref bool __result)
    {
        if (!IsRemoteClient)
            return true;

        __result = false;

        if (tile == null)
            return false;

        if (lastIgniteRequest.TryGetValue(tile.gridPosition, out float last) && Time.time - last < IGNITE_REQUEST_COOLDOWN)
            return false;

        lastIgniteRequest[tile.gridPosition] = Time.time;
        NetworkLifecycle.Instance.Client.SendHazmatIgnite(tile.gridPosition, ignitionStrength);
        return false;
    }
}

/// <summary>
/// The remaining grid mutations, which CargoReaction*.PostExplosionBehavior and CargoReactionRadioactive
/// apply straight to a tile without going through HazmatTileManager. Same reasoning as above: on a client
/// the host's tile state is the truth, so local writes are dropped.
/// </summary>
[HarmonyPatch(typeof(HazmatGridTile))]
public static class HazmatGridTilePatch
{
    private static bool IsRemoteClient =>
        NetworkLifecycle.Instance != null &&
        NetworkLifecycle.Instance.IsClientRunning &&
        !NetworkLifecycle.Instance.IsHost();

    [HarmonyPatch(nameof(HazmatGridTile.AddLiquidAmount))]
    [HarmonyPrefix]
    private static bool AddLiquidAmount(CargoType cargoType, float amountToAdd) => !IsRemoteClient;

    [HarmonyPatch(nameof(HazmatGridTile.AddRadiation))]
    [HarmonyPrefix]
    private static bool AddRadiation(float amountToAdd) => !IsRemoteClient;
}
