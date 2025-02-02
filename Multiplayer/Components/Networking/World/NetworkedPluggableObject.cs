using DV.CabControls;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedPluggableObject : IdMonoBehaviour<ushort, NetworkedPluggableObject>
{
    #region Lookup Cache
    private static readonly Dictionary<NetworkedPluggableObject, NetworkedPitStopStation> plugToStation = [];
    public static bool Get(ushort netId, out NetworkedPluggableObject obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedPluggableObject> rawObj);
        obj = (NetworkedPluggableObject)rawObj;
        return b;
    }
    #endregion

    protected override bool IsIdServerAuthoritative => true;

    public PluggableObject PluggableObject { get; private set; }
    public NetworkedPitStopStation Station { get; private set; }

    private bool handlersInitialised = false;

    private byte playerHolding = 0;
    private bool isGrabbed = false;

    #region Unity
    protected override void Awake()
    {
        base.Awake();

        PluggableObject = GetComponent<PluggableObject>();
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.Awake() {PluggableObject?.controlBase?.spec?.name}, {transform.parent.name}");
    }

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => PluggableObject?.controlBase != null);

        PluggableObject.controlBase.Grabbed += OnGrabbed;
        PluggableObject.controlBase.Ungrabbed += OnUngrabbed;
        PluggableObject.PluggedIn += OnPlugged;

        handlersInitialised = true;
    }

    protected override void OnDestroy()
    {
        if (UnloadWatcher.isUnloading)
            plugToStation.Clear();
        else
            plugToStation.Remove(this);

        if (PluggableObject?.controlBase != null && handlersInitialised)
        {
            PluggableObject.controlBase.Grabbed -= OnGrabbed;
            PluggableObject.controlBase.Ungrabbed -= OnUngrabbed;
            PluggableObject.PluggedIn -= OnPlugged;
        }

        base.OnDestroy();
    }
    #endregion

    #region Server

    public bool ValidateInteraction(CommonPitStopPlugInteractionPacket packet)
    {
        //todo: implement validation code (player distance, player interacting, etc.)
        return true;
    }

    #endregion

    #region Common

    public void ProcessPacket(CommonPitStopPlugInteractionPacket packet)
    {
        var interaction = (PlugInteractionType)packet.InteractionType;

        switch (interaction)
        {
            case PlugInteractionType.Rejected:
                //todo implement rejection
                break;

            case PlugInteractionType.PickedUp:
                isGrabbed = true;
                playerHolding = packet.PlayerId;
                PluggableObject.controlGrabbed = true;
                BlockInteraction(true);
                break;

            case PlugInteractionType.Dropped:
                isGrabbed = false;
                playerHolding = 0;
                PluggableObject.controlGrabbed = false;
                BlockInteraction(false);
                break;

            case PlugInteractionType.DockHome:
                isGrabbed = false;
                playerHolding = 0;
                PluggableObject.controlGrabbed = false;
                PluggableObject.InstantSnapTo(PluggableObject.startAttachedTo);
                BlockInteraction(false);
                break;

            case PlugInteractionType.DockSocket:
                if (NetworkedTrainCar.GetTrainCar(packet.TrainCarNetId, out var trainCar))
                {
                    isGrabbed = false;
                    playerHolding = 0;
                    PluggableObject.controlGrabbed = false;
                    BlockInteraction(false);

                    var sockets = trainCar.GetComponentsInChildren<PlugSocket>();
                    if (packet.IsLeftSide)
                        PluggableObject.InstantSnapTo(sockets[0]);
                    else
                        PluggableObject.InstantSnapTo(sockets[1]);
                }
                break;
        }
    }

    private void BlockInteraction(bool block)
    {
        var rigid = GetComponentInChildren<Rigidbody>();

        if (rigid)
            rigid.isKinematic = !block;

        if (block)
            PluggableObject.DisableColliders();
        else
            PluggableObject.EnableColliders();
    }

    public void InitPitStop(NetworkedPitStopStation netPitStop)
    {
        if(plugToStation.TryGetValue(this, out _))
        {
            Multiplayer.LogWarning($"Lookup cache 'plugToStation' already contains NetworkedPitStopStation \"{netPitStop?.StationName}\", skipping Init");
            return;
        }

        Station = netPitStop;
        plugToStation.Add(this, netPitStop);
    }
    #endregion

    #region Client
    private void OnGrabbed(ControlImplBase control)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnGrabbed() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");
        NetworkLifecycle.Instance.Client?.SendPitStopPlugInteractionPacket(NetId, PlugInteractionType.PickedUp);
    }

    private void OnUngrabbed(ControlImplBase control)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnUngrabbed() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");
        NetworkLifecycle.Instance.Client?.SendPitStopPlugInteractionPacket(NetId, PlugInteractionType.Dropped);
    }

    private void OnPlugged(PluggableObject plug, PlugSocket socket)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");

        PlugInteractionType interaction;
        bool left = false;
        ushort trainCarNetId = 0;

        if (socket == plug.startAttachedTo)
            interaction = PlugInteractionType.DockHome;
        else
        {
            var trainCar = TrainCar.Resolve(socket.gameObject);
            if(trainCar != null)
            {
                if(!NetworkedTrainCar.TryGetFromTrainCar(trainCar, out var netTrainCar))
                {
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() NetworkedTrainCar: {trainCar?.ID} Not Found! Socket: {socket.GetObjectPath()}");
                    return;
                }

                trainCarNetId = netTrainCar.NetId;

                interaction = PlugInteractionType.DockSocket;
                var sockets = trainCar.GetComponentsInChildren<PlugSocket>();
                if (socket = sockets[0])
                    left = true;
            }
            else
            {
                Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() Socket not recognised: {socket.GetObjectPath()}");
                return;
            }
        }

        NetworkLifecycle.Instance.Client?.SendPitStopPlugInteractionPacket(NetId, interaction, trainCarNetId, left);
    }
    #endregion
}
