using DV;
using DV.Interaction;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DV.ThingTypes;
using System.Collections;
using System.Collections.ObjectModel;


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

    public static Tuple<ushort, Vector3, int>[] GetAllPitStopStations()
    {
        if (netPitStopStationToLocation.Count == 0)
            InitialisePitStops();

        List <Tuple<ushort, Vector3, int>> result = [];

        int i = 0;
        foreach (var kvp in netPitStopStationToLocation)
        {
            var selection = kvp.Value?.Station?.pitstop?.SelectedIndex ?? 0;
            result.Add(new (kvp.Value.NetId, kvp.Key, selection));
        }

        return result.ToArray();
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
            CoroutineManager.Instance.StartCoroutine(netStation.Init());

            Multiplayer.LogDebug(() => $"InitialisePitStops() Parent: {station?.transform?.parent?.name}, parent-parent: {station?.transform?.parent?.parent?.name}, position global: {station?.transform?.position - WorldMover.currentMove}");
            netPitStopStationToLocation[station.transform.position - WorldMover.currentMove] = netStation;

        }
    }
    #endregion

    protected override bool IsIdServerAuthoritative => true;

    public PitStopStation Station { get; set; }
    public string StationName { get; private set; }

    private readonly GrabHandlerHingeJoint carSelectorGrab;
    private readonly Dictionary<GrabHandlerHingeJoint, (LocoResourceModule module, Action grabbedHandler, Action ungrabbedHandler)> grabberLookup = [];
    private readonly Dictionary<ResourceType, GrabHandlerHingeJoint> grabbedHandlerLookup = [];

    private bool isGrabbed = false;
    private LocoResourceModule grabbedModule;
    private RotaryAmplitudeChecker grabbedAmplitudeChecker;
    private float lastUnitsToBuy;

    #region Unity
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
        grabbedHandlerLookup.Clear();
        base.OnDestroy();
    }

    protected void Update()
    {
        if (isGrabbed && grabbedModule != null && grabbedAmplitudeChecker != null)
        {
            if(grabbedModule.Data.unitsToBuy != lastUnitsToBuy)
            {
                lastUnitsToBuy = grabbedModule.Data.unitsToBuy;
                NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.StateUpdate, grabbedModule.resourceType, lastUnitsToBuy);
            }
        }
    }
    #endregion

    #region Server

    public bool ValidateInteraction(CommonPitStopInteractionPacket packet)
    {
        //todo: implement validation code (player distance, player interacting, etc.)
        return true;
    }

    public void OnPlayerDisconnect(ITransportPeer peer)
    {
        //todo: when a player disconnects, if they are interacting with a lever, cancel the interaction
        //Multiplayer.LogWarning($"OnPlayerDisconnect()");
    }

    #endregion


    #region Common
    public IEnumerator Init()
    {
        Multiplayer.LogDebug(() => $"NetworkedPitStopStation.Init() station: {Station == null}, pitstop: {Station?.pitstop == null}");

        while (Station?.pitstop == null)
            yield return new WaitForEndOfFrame();

        var resourceModules = Station?.locoResourceModules?.resourceModules;

        var carSelectorGrab = GetComponentInChildren<GrabHandlerHingeJoint>();
        if (carSelectorGrab != null)
        {
            Multiplayer.LogDebug(() => $"NetworkedPitStopStation.Init() Grab Handler found: {carSelectorGrab != null}, Name: {carSelectorGrab.name}");
            carSelectorGrab.Grabbed += CarSelectorGrabbed;
            carSelectorGrab.UnGrabbed += CarSelectorUnGrabbed;

            Station.pitstop.CarSelected += CarSelected;
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
                        grabbedHandlerLookup[resourceModule.resourceType] = grab;

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

    public void ProcessPacket(CommonPitStopInteractionPacket packet)
    {
        PitStopStationInteractionType interactionType = (PitStopStationInteractionType)packet.InteractionType;
        ResourceType? resourceType = (ResourceType)packet.ResourceType;

        GrabHandlerHingeJoint grab = null;
        LocoResourceModule resourceModule = null;

        Multiplayer.LogDebug(() => $"ProcessPacket() [{StationName}, {NetId}] {interactionType}, resource type: {resourceType}, state: {packet.State}");

        if (resourceType != null && resourceType != 0)
        {
            if(!grabbedHandlerLookup.TryGetValue((ResourceType)resourceType, out grab))
                Multiplayer.LogError($"Could not find ResourceType in grabbedHandlerLookup for station {StationName}, resource type: {resourceType}");
            else
                if(!grabberLookup.TryGetValue(grab, out var tup))
                    Multiplayer.LogError($"Could not find GrabHandler in grabberLookup for station {StationName}, resource type: {resourceType}");
                else
                    (resourceModule, _, _) = tup;
        }

        switch (interactionType)
        {
            case PitStopStationInteractionType.Reject:

                break;

            case PitStopStationInteractionType.Grab:
                //block interaction
                if (grab != null)
                    grab.interactionAllowed = false;

                //set direction
                if (resourceType != null && resourceType != 0 && resourceModule != null)
                    resourceModule.Data.unitsToBuy = (int)packet.State;

                break;

            case PitStopStationInteractionType.Ungrab:
                //allow interaction
                if (grab != null)
                    grab.interactionAllowed = true;

                //set direction
                if (resourceType != null && resourceType != 0 && resourceModule != null)
                    resourceModule.Data.unitsToBuy = (int)packet.State;

                break;

            case PitStopStationInteractionType.StateUpdate:

                if (resourceType != null && resourceType != 0 && resourceModule != null)
                    resourceModule.Data.unitsToBuy = (int)packet.State;
                break;

            case PitStopStationInteractionType.SelectCar:
                Station.pitstop.currentCarIndex = (int)packet.State;
                Station.pitstop.OnCarSelectionChanged();

                break;
            case PitStopStationInteractionType.PayOrder:
                break;
            case PitStopStationInteractionType.CancelOrder:
                break;
            case PitStopStationInteractionType.ProcessOrder:
                break;
        }
    }
    #endregion

    #region Client

    private void CarSelectorGrabbed()
    {
        Multiplayer.LogDebug(() => $"CarSelectorGrabbed() {StationName}");
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Grab, null, 0);
    }

    private void CarSelectorUnGrabbed()
    {
        Multiplayer.LogDebug(() => $"CarSelectorUnGrabbed() {StationName}");
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Ungrab, null, Station.pitstop.SelectedIndex);
    }

    private void CarSelected()
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        Multiplayer.LogDebug(() => $"CarSelected() selected: {Station.pitstop.SelectedIndex}");

        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.SelectCar, null, Station.pitstop.SelectedIndex);
    }

    private void LeverGrabbed(LocoResourceModule module)
    {
        Multiplayer.LogDebug(() => $"LeverGrabbed() {StationName}, module: {module.resourceType}");
        isGrabbed = true;
        grabbedModule = module;
        grabbedAmplitudeChecker = module.GetComponentInChildren<RotaryAmplitudeChecker>();
        lastUnitsToBuy = module.Data.unitsToBuy;
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Grab, module.resourceType, lastUnitsToBuy);
    }

    private void LeverUnGrabbed(LocoResourceModule module)
    {
        Multiplayer.LogDebug(() => $"LeverUnGrabbed() {StationName}, module: {module.resourceType}");
        isGrabbed = false;
        grabbedModule = null;
        grabbedAmplitudeChecker = null;
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Ungrab, module.resourceType, lastUnitsToBuy);
    }
    #endregion
}
