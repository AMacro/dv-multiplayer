using DV.CabControls;
using DV.Interaction;
using Multiplayer.Components.Networking.Player;
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

    private GrabHandlerGizmoItem grabHandler;

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

        grabHandler = this.GetComponent<GrabHandlerGizmoItem>();

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
        bool result;

        NetworkedPlayer player = null;

        switch (interaction)
        {
            case PlugInteractionType.Rejected:
                //todo implement rejection
                break;

            case PlugInteractionType.PickedUp:
                //Handle the picked up state
                isGrabbed = true;
                playerHolding = packet.PlayerId;
                PluggableObject.controlGrabbed = true;
                BlockInteraction(true);

                PluggableObject.Unplug();

                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, Picked Up, player: {playerHolding}");

                //attach to a player
                if (NetworkLifecycle.Instance.IsClientRunning &&
                    NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerHolding, out player))
                {
                    var target = grabHandler?.customGrabAnchor?.GetGrabAnchor();
                    player.HoldItem(gameObject, target?.localPos, target?.localRot);
                }
                break;

            case PlugInteractionType.Dropped:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, Dropped");

                if (isGrabbed)
                {
                    if (NetworkLifecycle.Instance.IsClientRunning &&
                    NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerHolding, out player))
                    {
                        player.DropItem();
                    }
                }

                isGrabbed = false;
                playerHolding = 0;
                PluggableObject.controlGrabbed = false;
                BlockInteraction(false);

                PluggableObject.Unplug();
 
                break;

            case PlugInteractionType.DockHome:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockHome");

                if (isGrabbed)
                {
                    if (NetworkLifecycle.Instance.IsClientRunning &&
                    NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerHolding, out player))
                    {
                        player.DropItem();
                    }
                }

                isGrabbed = false;
                playerHolding = 0;
                PluggableObject.controlGrabbed = false;
                BlockInteraction(false);

                PluggableObject.Unplug();

                result = PluggableObject.InstantSnapTo(PluggableObject.startAttachedTo);
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockHome, result: {result}");
                break;

            case PlugInteractionType.DockSocket:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {packet.TrainCarNetId}, isLeft: {packet.IsLeftSide}");

                if (isGrabbed)
                {
                    if (NetworkLifecycle.Instance.IsClientRunning &&
                    NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerHolding, out player))
                    {
                        player.DropItem();
                    }
                }

                if (NetworkedTrainCar.GetTrainCar(packet.TrainCarNetId, out var trainCar))
                {
                    isGrabbed = false;
                    playerHolding = 0;
                    PluggableObject.controlGrabbed = false;
                    BlockInteraction(false);

                    PluggableObject.Unplug();

                    var sockets = trainCar.GetComponentsInChildren<PlugSocket>();
                    if (packet.IsLeftSide)
                    {
                        result = PluggableObject.InstantSnapTo(sockets[0]);
                        Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {packet.TrainCarNetId}, isLeft: {packet.IsLeftSide}, result: {result}");
                    }
                    else
                    {
                        result = PluggableObject.InstantSnapTo(sockets[1]);
                        Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {packet.TrainCarNetId}, isLeft: {packet.IsLeftSide}, result: {result}");
                    }
                }
                else
                {
                    Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {packet.TrainCarNetId}. TrainCar not found!");
                }
                break;
        }
    }

    private void BlockInteraction(bool block)
    {
        if (block)
        {
            PluggableObject.DisableStandaloneComponents();
            PluggableObject.DisableColliders();
        }
        else
            PluggableObject.EnableStandaloneComponents();
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
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnGrabbed() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");
        NetworkLifecycle.Instance.Client?.SendPitStopPlugInteractionPacket(NetId, PlugInteractionType.PickedUp);
    }

    private void OnUngrabbed(ControlImplBase control)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnUngrabbed() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");
        NetworkLifecycle.Instance.Client?.SendPitStopPlugInteractionPacket(NetId, PlugInteractionType.Dropped);
    }

    private void OnPlugged(PluggableObject plug, PlugSocket socket)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

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
                if (socket == sockets[0])
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
