using DV;
using DV.Interaction;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEngine;


namespace Multiplayer.Components.Networking.World;

public class NetworkedPitStopStation : IdMonoBehaviour<ushort, NetworkedPitStopStation>
{
    #region Lookup Cache
    private static readonly Dictionary<Vector3, NetworkedPitStopStation> netPitStopStationToLocation = [];
    public static bool Get(ushort netId, out NetworkedPitStopStation obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedPitStopStation> rawObj);
        obj = (NetworkedPitStopStation)rawObj;
        return b;
    }

    public static bool GetFromVector(Vector3 position, out NetworkedPitStopStation networkedPitStopStation)
    {
        return netPitStopStationToLocation.TryGetValue(position, out networkedPitStopStation);
    }

    public static Dictionary<ushort, Vector3> GetAllPitStopStations()
    {
        if (netPitStopStationToLocation.Count == 0)
            InitialisePitStops();

        Dictionary<ushort, Vector3> result = [];

        foreach (var kvp in netPitStopStationToLocation)
            result.Add(kvp.Value.NetId, kvp.Key);

        return new Dictionary<ushort, Vector3>(result);
    }

    public static void InitialisePitStops()
    {
        if (netPitStopStationToLocation.Count != 0)
            return;

        var stations = Resources.FindObjectsOfTypeAll<PitStopStation>();

        Multiplayer.LogDebug(() => $"InitialisePitStops() Found: {stations?.Length}");

        foreach (var station in stations)
        {
            Multiplayer.LogDebug(() => $"InitialisePitStops() Station: {station?.transform?.parent?.parent?.name}");

            var netStation = station.GetOrAddComponent<NetworkedPitStopStation>();
            netStation.Station = station;
            netStation.Init();

            Multiplayer.LogDebug(() => $"InitialisePitStops() Parent: {station?.transform?.parent?.name}, parent-parent: {station?.transform?.parent?.parent?.name}, position global: {station?.transform?.position - WorldMover.currentMove}");
            netPitStopStationToLocation[station.transform.position - WorldMover.currentMove] = netStation;

        }
    }
    #endregion

    protected override bool IsIdServerAuthoritative => true;

    public PitStopStation Station { get; set; }
    public string StationName { get; private set; }

    private GrabHandlerHingeJoint carSelectorGrab;
    private Dictionary<GrabHandlerHingeJoint, (LocoResourceModule module, Action grabbedHandler, Action ungrabbedHandler)> grabberLookup = [];


    protected override void Awake()
    {
        base.Awake();

        StationName = $"{transform.parent.parent.name} - {transform.parent.name}";
    }

    protected override void OnDestroy()
    {
        netPitStopStationToLocation.Remove(transform.position);

        if (carSelectorGrab != null)
        {
            carSelectorGrab.Grabbed -= CarSelectorGrabbed;
            carSelectorGrab.UnGrabbed -= CarSelectorUnGrabbed;
        }

        foreach (var kvp in grabberLookup)
        {
            var grab = kvp.Key;
            var (_, grabbedHandler, ungrabbedHandler) = kvp.Value;
            grab.Grabbed -= grabbedHandler;
            grab.UnGrabbed -= ungrabbedHandler;
        }

        grabberLookup.Clear();
        base.OnDestroy();
    }

    public void Init()
    {
        var resourceModules = Station?.locoResourceModules?.resourceModules;

        var carSelectorGrab = GetComponentInChildren<GrabHandlerHingeJoint>();
        if (carSelectorGrab != null)
        {
            Multiplayer.LogDebug(() => $"NetworkedPitStopStation.Init() Grab Handler found: {carSelectorGrab != null}, Name: {carSelectorGrab.name}");
            carSelectorGrab.Grabbed += CarSelectorGrabbed;
            carSelectorGrab.UnGrabbed += CarSelectorUnGrabbed;
        }

        StringBuilder sb = new();
        sb.AppendLine($"NetworkedPitStopStation.Awake() {StationName} resources:");

        if (resourceModules != null)
        {
            foreach (var resourceModule in resourceModules)
            {
                var grabHandlers = resourceModule.GetComponentsInChildren<GrabHandlerHingeJoint>();
                foreach (var grab in grabHandlers)
                {
                    if (grab != null)
                    {
                        //Delegates for handlers
                        void GrabbedHandler() => LeverGrabbed(resourceModule);
                        void UnGrabbedHandler() => LeverUnGrabbed(resourceModule);

                        //Subscribe
                        grab.Grabbed += GrabbedHandler;
                        grab.UnGrabbed += UnGrabbedHandler;

                        //Store delegates
                        grabberLookup[grab] = (resourceModule, GrabbedHandler, UnGrabbedHandler);

                        sb.AppendLine($"\t{resourceModule.resourceType}, Grab Handler found: {grab != null}, Name: {grab.name}");
                    }
                }
            }
        }
        else
        {
            sb.AppendLine($"ERROR Station is Null {Station == null}, resource modules: {Station?.locoResourceModules}");
        }

        Multiplayer.LogDebug(() => sb.ToString());
    }

    private void CarSelectorGrabbed()
    {
        Multiplayer.LogDebug(() => $"CarSelectorGrabbed() {StationName}");
    }

    private void CarSelectorUnGrabbed()
    {
        Multiplayer.LogDebug(() => $"CarSelectorUnGrabbed() {StationName}");
    }

    private void LeverGrabbed(LocoResourceModule module)
    {
        Multiplayer.LogDebug(() => $"LeverGrabbed() {StationName}, module: {module.resourceType}");
    }

    private void LeverUnGrabbed(LocoResourceModule module)
    {
        Multiplayer.LogDebug(() => $"LeverUnGrabbed() {StationName}, module: {module.resourceType}");
    }
}
