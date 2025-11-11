using DV.Logic.Job;
using DV.Utils;
using JetBrains.Annotations;
using System.Collections.Generic;

namespace Multiplayer.Components.Networking.Jobs;

public class WarehouseMachineLookup : SingletonBehaviour<WarehouseMachineLookup>
{
    private static readonly Dictionary<ushort, WarehouseMachine> netIdToWarehouseMachine = [];

    public void RegisterWarehouseMachine(WarehouseMachine machine)
    {
        Multiplayer.LogDebug(() => $"RegisterWarehouseMachine() {machine.WarehouseTrack.ID}, machineID: {machine.ID}");

        if (machine == null)
            return;

        if (string.IsNullOrEmpty(machine.ID))
        {
            Multiplayer.LogDebug(() => $"Attempted to register WarehouseMachine with null or empty ID for track {machine.WarehouseTrack.ID}");
            return;
        }

        ushort netId = GenerateNetId(machine.ID);

        if (netIdToWarehouseMachine.ContainsKey(netId))
        {
            var existing = netIdToWarehouseMachine[netId];
            Multiplayer.LogWarning(() => $"Registering WarehouseMachine for track {machine.WarehouseTrack.ID}, machineID: {machine.ID} failed! More than one WarehouseMachine with the same ID!");
            return;
        }

        Multiplayer.LogDebug(() => $"Registered WarehouseMachine for track {machine.WarehouseTrack.ID}, machineID: {machine.ID}, netId: {netId}");
        netIdToWarehouseMachine[netId] = machine;
    }

    public static bool TryGet(ushort netId, out WarehouseMachine machine)
    {
        var result = netIdToWarehouseMachine.TryGetValue(netId, out machine);

        if (result && machine == null)
        {
            netIdToWarehouseMachine.Remove(netId);
            return false;
        }

        return result;
    }

    public static bool TryGetNetId(WarehouseMachine machine, out ushort netId)
    {
        //Multiplayer.LogDebug(() => $"Trying to get NetID for WarehouseMachine on track {machine?.WarehouseTrack?.ID}, machineID: {machine?.ID}");

        if (machine != null && !string.IsNullOrEmpty(machine.ID))
        {
            netId = GenerateNetId(machine.ID);
            var temp = netId;
            Multiplayer.LogDebug(() => $"Trying to get NetID for WarehouseMachine on track {machine?.WarehouseTrack?.ID}, machineID: {machine?.ID}, netId: {temp}");

            if (netIdToWarehouseMachine.ContainsKey(netId))
                return true;
        }

        netId = 0;
        return false;
    }

    private static ushort GenerateNetId(string id)
    {
        unchecked
        {
            int hash = id.GetHashCode();
            ushort result = (ushort)((hash & 0xFFFF) ^ ((hash >> 16) & 0xFFFF));
            return result == 0 ? (ushort)1 : result;
        }
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(WarehouseMachineLookup)}]";
    }

    protected override void Awake()
    {
        base.Awake();
        netIdToWarehouseMachine.Clear();
        Multiplayer.LogDebug(() => $"{nameof(WarehouseMachineLookup)} Awake, cleared existing lookup dictionary.");
    }
}
