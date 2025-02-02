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
using System.Linq;


namespace Multiplayer.Components.Networking.World;

/// <summary>
/// Handles networked interactions with pit stop stations, including vehicle selection and resource management.
/// </summary>
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

    public static NetworkedPitStopStation[] GetAll()
    {
        return netPitStopStationToLocation.Values.ToArray();
    }

    public static Tuple<ushort, Vector3, int>[] GetAllPitStopStations()
    {
        if (netPitStopStationToLocation.Count == 0)
            InitialisePitStops();

        List <Tuple<ushort, Vector3, int>> result = [];

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

        var stations = Resources.FindObjectsOfTypeAll<PitStopStation>().Where(p => p.transform.parent != null).ToArray();

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

    const float MAX_DELTA = 0.2f;
    const float MIN_UPDATE_TIME = 0.1f;

    public PitStopStation Station { get; set; }
    public string StationName { get; private set; }

    private readonly GrabHandlerHingeJoint carSelectorGrab;
    private readonly Dictionary<GrabHandlerHingeJoint, (LocoResourceModule module, Action grabbedHandler, Action ungrabbedHandler)> grabberLookup = [];
    private readonly Dictionary<ResourceType, GrabHandlerHingeJoint> grabbedHandlerLookup = [];
    private readonly Dictionary<ResourceType, NetworkedPluggableObject> resourceToPluggableObject = [];

    private bool isGrabbed = false;
    private bool wasGrabbed = false;
    private bool isRemoteGrabbed = false;
    private bool wasRemoteGrabbed = false;
    private float lastRemoteValue = 0.0f;
    private float lastUpdateTime = 0.0f;

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
        if (UnloadWatcher.isUnloading)
            netPitStopStationToLocation.Clear();
        else
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

    protected void LateUpdate()
    {
        if (grabbedModule == null && grabbedAmplitudeChecker == null)
            return;

        //Handle local grab interactions
        if (isGrabbed || (wasGrabbed && lastUnitsToBuy != grabbedModule.Data.unitsToBuy))
        {
            //ensure the delta is big enough to be worth sending or we have reached a limit
            var delta = Math.Abs(lastUnitsToBuy - grabbedModule.Data.unitsToBuy);
            var deltaTime = Time.time - lastUpdateTime;

            //Check if the units to buy have reached a limit (0 or AbsoluteMaxValue), as this overrides a delta below minimum
            var unitsToBuyChanged =
                   (grabbedModule.Data.unitsToBuy == grabbedModule.AbsoluteMinValue && lastUnitsToBuy != grabbedModule.AbsoluteMinValue)
                || (grabbedModule.Data.unitsToBuy == grabbedModule.AbsoluteMaxValue && lastUnitsToBuy != grabbedModule.AbsoluteMaxValue);

            //Send the update if we've passed the time threshold AND we have a big enough change or hit a limit
            if (deltaTime > MIN_UPDATE_TIME && (delta > MAX_DELTA || unitsToBuyChanged))
            {
                lastUnitsToBuy = grabbedModule.Data.unitsToBuy;
                lastUpdateTime = Time.time;

                NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(
                        NetId,
                        PitStopStationInteractionType.StateUpdate,
                        grabbedModule.resourceType,
                        lastUnitsToBuy
                    );
            }
        }
        //Local grab has ended, but needs to be finalised
        else if (wasGrabbed) 
        {
            Multiplayer.LogDebug(() => $"NetworkedPitStopStation.LateUpdate() wasGrabbed: {wasGrabbed}, previous: {lastUnitsToBuy}, new: {grabbedModule.Data.unitsToBuy}");
            lastUnitsToBuy = grabbedModule.Data.unitsToBuy;

            NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(
                    NetId,
                    PitStopStationInteractionType.Ungrab,
                    grabbedModule.resourceType,
                    lastUnitsToBuy
                );

            //Reset grab states
            wasGrabbed = false;
            grabbedModule = null;
            grabbedAmplitudeChecker = null;
        }

        //allow things to settle after remote grab released
        if (!isRemoteGrabbed && wasRemoteGrabbed)
        {
            float previous = grabbedModule.Data.unitsToBuy;
            //grabbedModule.Data.unitsToBuy = lastRemoteValue; 
            grabbedModule.SetUnitsToBuy(lastRemoteValue);

            Multiplayer.LogDebug(() => $"NetworkedPitStopStation.LateUpdate() wasRemoteGrabbed: {wasRemoteGrabbed}, previous: {previous}, new: {lastRemoteValue}");

            if (previous == lastRemoteValue)
            {
                //settled, stop tracking remote
                wasRemoteGrabbed = false;
                grabbedModule = null;
            }
        }
    }
    #endregion

    #region Server

    public Dictionary<ResourceType, ushort> GetPluggables()
    {
        Dictionary<ResourceType, ushort> keyValuePairs = [];
        foreach (var kvp in resourceToPluggableObject)
            keyValuePairs.Add(kvp.Key, kvp.Value.NetId);

        return keyValuePairs;
    }

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
    /// <summary>
    /// Looks up Pluggable object by resource type
    /// </summary>
    public bool TryGetPluggable(ResourceType type, out NetworkedPluggableObject netPluggable)
    {
        return resourceToPluggableObject.TryGetValue(type, out netPluggable);
    }

    /// <summary>
    /// Initializes the pit stop station and sets up event handlers for grab interactions.
    /// </summary>
    private IEnumerator Init()
    {
        Multiplayer.LogDebug(() => $"NetworkedPitStopStation.Init() station: {Station == null}, pitstop: {Station?.pitstop == null}");

        while (Station?.pitstop == null)
            yield return new WaitForEndOfFrame();

        var resourceModules = Station?.locoResourceModules?.resourceModules;

        //Wait for levers an knobs to load
        yield return new WaitUntil(() => GetComponentInChildren<GrabHandlerHingeJoint>(true) != null);
        GrabHandlerHingeJoint carSelectorGrab = GetComponentInChildren<GrabHandlerHingeJoint>(true);

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

                var plug = resourceModule.resourceHose;
                if (plug != null)
                {
                    var netPlug = plug.GetOrAddComponent<NetworkedPluggableObject>();
                    resourceToPluggableObject[resourceModule.resourceType] = netPlug;
                    netPlug.InitPitStop(this);
                }
            }
        }
        else
        {
            sb.AppendLine($"ERROR Station is Null {Station == null}, resource modules: {Station?.locoResourceModules}");
        }

        Multiplayer.LogDebug(() => sb.ToString());
    }

    /// <summary>
    /// Processes incoming network packets for pit stop interactions.
    /// </summary>
    /// <param name="packet">The packet containing interaction data.</param>
    public void ProcessPacket(CommonPitStopInteractionPacket packet)
    {
        PitStopStationInteractionType interactionType = (PitStopStationInteractionType)packet.InteractionType;
        ResourceType? resourceType = (ResourceType)packet.ResourceType;

        GrabHandlerHingeJoint grab = null;
        LocoResourceModule resourceModule = null;

        Multiplayer.LogDebug(() => $"NetworkedPitStopStation.ProcessPacket() [{StationName}, {NetId}] {interactionType}, resource type: {resourceType}, state: {packet.State}");

        if (resourceType != null && resourceType != 0)
        {
            if(!grabbedHandlerLookup.TryGetValue((ResourceType)resourceType, out grab))
                Multiplayer.LogError($"Could not find ResourceType in grabbedHandlerLookup for Pit Stop station {StationName}, resource type: {resourceType}");
            else
                if(!grabberLookup.TryGetValue(grab, out var tup))
                    Multiplayer.LogError($"Could not find GrabHandler in grabberLookup for Pit Stop station {StationName}, resource type: {resourceType}");
                else
                    (resourceModule, _, _) = tup;

            if (packet.State < resourceModule.AbsoluteMinValue || packet.State > resourceModule.AbsoluteMaxValue)
            {
                Multiplayer.LogError($"Invalid Pit Stop state value: {packet.State} for resource {resourceModule.resourceType}");
                return;
            }
        }

        switch (interactionType)
        {
            case PitStopStationInteractionType.Reject:
                //todo: implement rejection
                break;

            case PitStopStationInteractionType.Grab:
                //block interaction
                grab?.SetMovingDisabled(false);

                //set direction
                if (resourceType != null && resourceType != 0 && resourceModule != null)
                {
                    grabbedModule = resourceModule;
                    lastRemoteValue = packet.State;
                }

                isRemoteGrabbed = true;
                wasRemoteGrabbed = true;
                break;

            case PitStopStationInteractionType.Ungrab:
                //allow interaction
                grab?.SetMovingDisabled(true);

                if (resourceType != null && resourceType != 0 && resourceModule != null)
                {
                    lastRemoteValue = packet.State;
                    //resourceModule.Data.unitsToBuy = lastRemoteValue;
                    resourceModule.SetUnitsToBuy(lastRemoteValue);
                }

                isRemoteGrabbed = false;

                break;

            case PitStopStationInteractionType.StateUpdate:

                if (resourceType != null && resourceType != 0 && resourceModule != null)
                {
                    if (isRemoteGrabbed || wasRemoteGrabbed)
                    {
                        lastRemoteValue = packet.State;
                        //resourceModule.Data.unitsToBuy = lastRemoteValue;
                        resourceModule.SetUnitsToBuy(lastRemoteValue);
                    }
                }
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
    /// <summary>
    /// Handles grab interactions for the car selector knob.
    /// </summary>
    private void CarSelectorGrabbed()
    {
        Multiplayer.LogDebug(() => $"CarSelectorGrabbed() {StationName}");
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Grab, null, 0);
    }

    /// <summary>
    /// Handles end of grab (release) interactions for the car selector knob.
    /// </summary>
    private void CarSelectorUnGrabbed()
    {
        Multiplayer.LogDebug(() => $"CarSelectorUnGrabbed() {StationName}");
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Ungrab, null, Station.pitstop.SelectedIndex);
    }

    /// <summary>
    /// Handles change of selected car events.
    /// </summary>
    private void CarSelected()
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        Multiplayer.LogDebug(() => $"CarSelected() selected: {Station.pitstop.SelectedIndex}");

        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.SelectCar, null, Station.pitstop.SelectedIndex);
    }

    /// <summary>
    /// Handles grab interactions for resource module levers.
    /// </summary>
    /// <param name="module">The resource module being grabbed.</param>
    private void LeverGrabbed(LocoResourceModule module)
    {
        Multiplayer.LogDebug(() => $"LeverGrabbed() {StationName}, module: {module.resourceType}");
        isGrabbed = true;
        wasGrabbed = true;
        grabbedModule = module;
        grabbedAmplitudeChecker = module.GetComponentInChildren<RotaryAmplitudeChecker>();
        lastUnitsToBuy = module.Data.unitsToBuy;
        NetworkLifecycle.Instance?.Client.SendPitStopInteractionPacket(NetId, PitStopStationInteractionType.Grab, module.resourceType, lastUnitsToBuy);
    }

    /// <summary>
    /// Handles end of grab (release) interactions for resource module levers.
    /// </summary>
    /// <param name="module">The resource module being grabbed.</param>
    private void LeverUnGrabbed(LocoResourceModule module)
    {
        Multiplayer.LogDebug(() => $"LeverUnGrabbed() {StationName}, module: {module.resourceType}");
        isGrabbed = false;
    }
    #endregion
}
