using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.Jobs;
using System.Collections.Generic;
using static WarehouseMachineController;



namespace Multiplayer.Components.Networking.Jobs;

public class NetworkedWarehouseMachineController : IdMonoBehaviour<ushort, NetworkedWarehouseMachineController>
{
    #region Lookup Cache
    private static readonly Dictionary<WarehouseMachineController, NetworkedWarehouseMachineController> warehouseMachineControllerToNetworked = [];
    private static readonly Dictionary<WarehouseMachine, NetworkedWarehouseMachineController> warehouseMachineToNetworked = [];

    public static bool Get(ushort netId, out NetworkedWarehouseMachineController obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedWarehouseMachineController> rawObj);
        obj = (NetworkedWarehouseMachineController)rawObj;
        return b;
    }

    public static NetworkedWarehouseMachineController GetFromWarehouseMachineController(WarehouseMachineController warehouseMachineController)
    {
        warehouseMachineControllerToNetworked.TryGetValue(warehouseMachineController, out var netWarehouseMachineController);
        return netWarehouseMachineController;
    }

    public static NetworkedWarehouseMachineController GetFromWarehouseMachine(WarehouseMachine warehouseMachine)
    {
        //fast path lookup
        if (warehouseMachineToNetworked.TryGetValue(warehouseMachine, out NetworkedWarehouseMachineController networkedWarehouseMachineController))
            return networkedWarehouseMachineController;

        //cache miss, try to find parent WarehouseMachineController
        var warehouseMachineController = GetFomId(warehouseMachine.ID);
        if (warehouseMachineController != null)
        {
            //Warehouse Machine Controller found, check for NetworkedWarehouseMachineController
            networkedWarehouseMachineController = GetFromWarehouseMachineController(warehouseMachineController);
            if (networkedWarehouseMachineController != null)
                warehouseMachineToNetworked[warehouseMachine] = GetFromWarehouseMachineController(warehouseMachineController);
        }

        return networkedWarehouseMachineController;
    }

    private static WarehouseMachineController GetFomId(string ID)
    {
        foreach (var warehouse in WarehouseMachineController.allControllers)
        {
            if (warehouse.warehouseMachine.ID == ID)
            {
                return warehouse;
            }
        }
        return null;
    }

    #endregion
    protected override bool IsIdServerAuthoritative => false;

    public string Id => WarehouseMachine?.ID;
    public WarehouseMachineController WarehouseMachineController { get; private set; }
    public WarehouseMachine WarehouseMachine => WarehouseMachineController?.warehouseMachine;

    protected override void Awake()
    {
        base.Awake();
        WarehouseMachineController = GetComponent<WarehouseMachineController>();
        warehouseMachineControllerToNetworked[WarehouseMachineController] = this;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        warehouseMachineControllerToNetworked.Remove(WarehouseMachineController);

        if (WarehouseMachineController.warehouseMachine != null)
            warehouseMachineToNetworked.Remove(WarehouseMachineController.warehouseMachine);
    }

    public void ServerProcessWarehouseAction(WarehouseAction action)
    {
        Multiplayer.LogDebug(() => $"ServerProcessWarehouseAction() {Id}, Action Type: {action}");
        switch (action)
        {
            case WarehouseAction.Load:
                WarehouseMachineController.StartLoadSequence();
                break;

            case WarehouseAction.Unload:
                WarehouseMachineController.StartUnloadSequence();
                break;
        }
    }

    public void ClientProcessUpdate(ClientboundWarehouseControllerUpdatePacket packet)
    {
        TextPreset preset = (TextPreset)packet.Preset;
        bool isLoading = packet.IsLoading;
        string jobId = null;
        Car car = null;
        CargoType_v2 cargoType_V2 = null;
        string extra = null;

        if (WarehouseMachineController == null)
            return;

        if (packet.CarNetId != 0)
        {
            if (!NetworkedTrainCar.Get(packet.CarNetId, out var networkedCar))
            {
                Multiplayer.LogWarning($"NetworkedWarehouseMachineController failed to find TrainCar with NetId: {packet.NetId}");
                return;
            }

            car = networkedCar.TrainCar.logicCar;
        }

        if (packet.JobNetId != 0)
        {
            if (!NetworkedJob.Get(packet.JobNetId, out var networkedJob))
            {
                Multiplayer.LogWarning($"NetworkedWarehouseMachineController failed to find Job with NetId: {packet.JobNetId}");
                return;
            }

            jobId = networkedJob.Job.ID;
        }

        if (car != null && jobId != null)
        {
            cargoType_V2 = ((CargoType)packet.CargoType).ToV2();
        }

        WarehouseMachineController?.SetScreen(preset, isLoading, jobId, car, cargoType_V2, extra);

        //special case for car updated - remove task from machine
        if (preset == TextPreset.CarUpdated && WarehouseMachine != null)
        {
            CleanupTask(isLoading, car);
        }

        //special case for clearing - play sound
        if (preset == TextPreset.ClearDesc)
            WarehouseMachineController?.machineSound?.Play(WarehouseMachineController.transform.position, 1f, 1f, 0f, 1f, 500f, default, null, base.transform, false, 0f, null);

    }

    private void CleanupTask(bool isLoading, Car car)
    {
        List<WarehouseMachine.WarehouseLoadUnloadDataPerJob> currentLoadUnloadData = WarehouseMachine.GetCurrentLoadUnloadData(isLoading ? WarehouseTaskType.Loading : WarehouseTaskType.Unloading);

        foreach (var data in currentLoadUnloadData)
        {
            if (data.tasksAvailableToProcess == null)
                continue;

            foreach (var task in data.tasksAvailableToProcess)
                WarehouseMachine.RemoveWarehouseTask(task);
        }
    }
}
