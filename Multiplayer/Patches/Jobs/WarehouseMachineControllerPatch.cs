using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using static WarehouseMachineController;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(WarehouseMachineController))]
public class WarehouseMachineControllerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(WarehouseMachineController.Awake))]
    public static void Awake(WarehouseMachineController __instance)
    {
        __instance.gameObject.AddComponent<NetworkedWarehouseMachineController>();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(WarehouseMachineController.SetScreen))]
    public static void SetScreen(WarehouseMachineController __instance, TextPreset preset, bool isLoading, string jobId, Car car, CargoType_v2 cargoType)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        //Multiplayer.LogDebug(() => $"WarehouseMachineControllerPatch.SetScreen() is host");

        bool skip = preset switch
        {
            TextPreset.Idle => true,
            TextPreset.TrainInRange => true,
            TextPreset.ClearTrainInRange => true,
            _ => false
        };

        //Multiplayer.LogDebug(() => $"WarehouseMachineControllerPatch.SetScreen() skipping: {skip}");
        if (skip)
            return;

        if (!NetworkedWarehouseMachineController.GetFromWarehouseMachineController(__instance, out var netMachine) || netMachine == null)
        {
            Multiplayer.LogError($"WarehouseMachineControllerPatch.SetScreen(): Failed to get NetworkedWarehouseMachineController for {__instance.warehouseTrackName}");
            return;
        }

        //Multiplayer.LogDebug(() => $"WarehouseMachineControllerPatch.SetScreen() NetMachine found");

        //obtain serialisable info
        ushort carNetId = 0;
        ushort jobNetId = 0;
        CargoType cargoTypeV1 = CargoType.None;

        if (car != null)
        {
            //Multiplayer.LogDebug(() => $"WarehouseMachineControllerPatch.SetScreen() car not null");
            var tc = car.TrainCar();
            if (tc == null || !NetworkedTrainCar.TryGetFromTrainCar(tc, out var netTC))
            {
                //Multiplayer.LogWarning($"WarehouseMachineControllerPatch.SetScreen() Failed to get NetworkedTrainCar for {car?.ID}");
                return;
            }

            //Multiplayer.LogDebug(() => $"WarehouseMachineControllerPatch.SetScreen() NetCar found");
            carNetId = netTC.NetId;
        }

        if (!string.IsNullOrEmpty(jobId))
        {
            if(!NetworkedJob.TryGetFromJobId(jobId, out var netJob))
            {
                Multiplayer.LogWarning($"WarehouseMachineControllerPatch.SetScreen() Failed to get NetworkedJob for {jobId}");
                return;
            }

            //Multiplayer.LogDebug(() => $"WarehouseMachineControllerPatch.SetScreen() NetJob found");
            jobNetId = netJob.NetId;
        }

        if (cargoType != null)
                cargoTypeV1 = cargoType.v1;

        NetworkLifecycle.Instance.Server.SendWarehouseControllerUpdate(netMachine.NetId, isLoading, jobNetId, carNetId, cargoTypeV1, preset);
    }

    [HarmonyPrefix]
    [HarmonyPatch("StartUnloadSequence")]
    public static bool StartUnloadSequence_Prefix(WarehouseMachineController __instance)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        SendValidationRequest(__instance, WarehouseAction.Unload);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("StartLoadSequence")]
    public static bool StartLoadSequence_Prefix(WarehouseMachineController __instance)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        SendValidationRequest(__instance, WarehouseAction.Load);
        return false;
    }

    private static void SendValidationRequest(WarehouseMachineController machine, WarehouseAction action)
    {
        string id = machine?.warehouseMachine?.ID;

        if (string.IsNullOrEmpty(id))
        {
            NetworkLifecycle.Instance.Client.LogError($"Failed to validate {action} for {machine?.name} at {machine?.warehouseTrackName}. Warehouse not found!");
            return;
        }

        if (!NetworkedWarehouseMachineController.GetFromWarehouseMachineController(machine, out var netController) || netController == null)
        {
            NetworkLifecycle.Instance.Client.LogError($"Failed to find NetworkedWarehouseMachineController {machine?.warehouseTrackName}. Warehouse not found!");
            return;
        }

        NetworkLifecycle.Instance.Client.SendWarehouseRequest(action, netController.NetId);
    }
}
