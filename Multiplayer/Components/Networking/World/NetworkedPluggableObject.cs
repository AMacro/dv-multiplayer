using DV.CabControls;
using DV.Interaction;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedPluggableObject : IdMonoBehaviour<ushort, NetworkedPluggableObject>
{
    private const float DISTANCE_TOLERANCE = 1.05f; //allow 5% tolerance for interactions coming from clients
    private const float GRAB_SQR_DISTANCE = GrabberRaycaster.SPHERE_CAST_MAX_DIST * GrabberRaycaster.SPHERE_CAST_MAX_DIST * DISTANCE_TOLERANCE;
    private const float DOCK_SQR_DISTANCE = 2f * 2f * DISTANCE_TOLERANCE; //no accessible constant available, hardcoded in to `PluggableObject.ScanForHit()`

    private const sbyte INVALID_SOCKET = -1;
    private const ushort INVALID_NETID = 0;

    #region Lookup Cache
    private static readonly Dictionary<NetworkedPluggableObject, NetworkedPitStopStation> plugToStation = [];
    private static readonly Dictionary<PluggableObject, NetworkedPluggableObject> plugToNetworkedPluggable = [];

    public static bool Get(ushort netId, out NetworkedPluggableObject obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedPluggableObject> rawObj);
        obj = (NetworkedPluggableObject)rawObj;
        return b;
    }

    public static bool Get(PluggableObject pluggableObject, out NetworkedPluggableObject obj)
    {
        bool b = plugToNetworkedPluggable.TryGetValue(pluggableObject, out obj);
        return b;
    }
    #endregion

    protected override bool IsIdServerAuthoritative => false;

    #region Server Variables
    public ServerPlayer HeldBy { get; private set; }
    #endregion

    #region Common Variables
    public PluggableObject PluggableObject { get; private set; }
    public Rigidbody PlugRB { get; private set; }
    public PropHose Hose { get; private set; }
    public NetworkedPitStopStation Station { get; private set; }
    public bool IsConnecting { get; set; } = false;

    public bool IsHeld => playerHolding != 0 || HeldBy != null || PluggableObject.controlGrabbed;

    private GrabHandlerGizmoItem grabHandler;

    private bool handlersInitialised = false;

    public ushort TrainCarNetId { get; private set; } = INVALID_NETID;  //initialise to invalid TrainCar
    public sbyte SocketIndex { get; private set; } = INVALID_SOCKET;    //initialise to invalid socket

    private PlugInteractionType currentInteraction = PlugInteractionType.Rejected;

    private bool processingAsHost = false;
    #endregion

    #region Client Variables
    private bool Refreshed = false;
    private byte playerHolding;
    #endregion

    #region Unity
    protected override void Awake()
    {
        if (NetId == INVALID_NETID)
            base.Awake();

        PluggableObject = GetComponent<PluggableObject>();
        Hose = transform.parent.GetComponentInChildren<PropHose>();
        PlugRB = PluggableObject.GetComponent<Rigidbody>();


        //Multiplayer.LogDebug(() => $"NetworkedPluggableObject.Awake() {PluggableObject?.controlBase?.spec?.name}, {transform.parent.name}");
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.Awake() {this.GetObjectPath()}, netId: {NetId}, PluggableObject found: {PluggableObject != null}, RB Found: {PlugRB != null}, Hose found: {Hose != null}");

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.Server.PlayerDisconnected += OnPlayerDisconnected;

            Refreshed = true;
        }
    }

    protected IEnumerator Start()
    {
        //Multiplayer.LogDebug(() => $"NetworkedPluggableObject.Start() {PluggableObject?.controlBase?.spec?.name}, {transform.parent.name}");
        yield return new WaitUntil(() => PluggableObject?.controlBase != null);

        //Multiplayer.LogDebug(() => $"NetworkedPluggableObject.Start() Controlbase {PluggableObject?.controlBase?.spec?.name}, {transform.parent.name}");

        grabHandler = this.GetComponent<GrabHandlerGizmoItem>();

        PluggableObject.controlBase.Grabbed += OnGrabbed;
        PluggableObject.controlBase.Ungrabbed += OnUngrabbed;
        PluggableObject.PluggedIn += OnPluggedIn;

        handlersInitialised = true;
    }

    protected void OnDisable()
    {
        if (!NetworkLifecycle.Instance.IsHost())
            Refreshed = false;
    }

    protected override void OnDestroy()
    {
        if (UnloadWatcher.isUnloading)
        {
            plugToStation.Clear();
            plugToNetworkedPluggable.Clear();
        }
        else
        {
            plugToStation.Remove(this);
            plugToNetworkedPluggable.Remove(PluggableObject);
        }

        if (PluggableObject?.controlBase != null && handlersInitialised)
        {
            PluggableObject.controlBase.Grabbed -= OnGrabbed;
            PluggableObject.controlBase.Ungrabbed -= OnUngrabbed;
            PluggableObject.PluggedIn -= OnPluggedIn;
        }

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.Server.PlayerDisconnected -= OnPlayerDisconnected;
        }

        base.OnDestroy();
    }

    protected void LateUpdate()
    {
        if (currentInteraction == PlugInteractionType.Rejected)
            return;

        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.LateUpdate()station: {Station?.StationName}, processing: {NetworkLifecycle.Instance.IsProcessingPacket}, processing as Host: {processingAsHost}, refreshed: {Refreshed}, isConnecting: {IsConnecting}");
        if (!processingAsHost)
        {
            NetworkLifecycle.Instance.Client?.SendPitStopPlugInteractionPacket(NetId, currentInteraction, transform.position - WorldMover.currentMove, transform.rotation, TrainCarNetId, SocketIndex);
        }
        else
        {
            //this will only trigger when there's a valid state to be sent (current interaction is not rejected)
            //and the host is processing a packet
            //this should be the end of the processing, even for docking plugs, so we can clear the connecting and processing flags
            IsConnecting = false;
            processingAsHost = false;
        }

        currentInteraction = PlugInteractionType.Rejected;
    }
    #endregion

    #region Server

    public void ProcessInteractionPacketAsHost(CommonPitStopPlugInteractionPacket packet, ServerPlayer senderPlayer)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() NetId: {NetId}, InteractionType: {packet.InteractionType}, from player: {senderPlayer.Username}");

        if (ValidateInteraction(packet, senderPlayer))
        {
            //passed validation, set server states
            Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() VALIDATION PASSED for NetId: {NetId}");

            switch (packet.InteractionType)
            {
                case PlugInteractionType.PickedUp:
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() Processing {packet.InteractionType} for NetId: {NetId}");
                    HeldBy = senderPlayer;
                    TrainCarNetId = INVALID_NETID;
                    SocketIndex = INVALID_SOCKET;

                    break;

                case PlugInteractionType.Dropped:
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() Processing {packet.InteractionType} for NetId: {NetId}");
                    HeldBy = null;
                    TrainCarNetId = INVALID_NETID;
                    SocketIndex = INVALID_SOCKET;

                    break;

                case PlugInteractionType.Yanked:
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() Processing {packet.InteractionType} for NetId: {NetId}");
                    //we should never reach this as Yanked is only sent by the server and should be rejected by validation
                    HeldBy = null;
                    TrainCarNetId = INVALID_NETID;
                    SocketIndex = INVALID_SOCKET;

                    break;

                case PlugInteractionType.DockHome:
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() Processing {packet.InteractionType} for NetId: {NetId}");
                    HeldBy = null;
                    TrainCarNetId = INVALID_NETID;
                    SocketIndex = INVALID_SOCKET;

                    break;

                case PlugInteractionType.DockSocket:
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() Processing {packet.InteractionType} for NetId: {NetId}");
                    HeldBy = null;
                    TrainCarNetId = packet.TrainCarNetId;
                    SocketIndex = packet.SocketIndex;

                    break;
            }

            packet.PlayerId = senderPlayer.PlayerId;

            //Allow host to process packet if not from a local client
            if (!NetworkLifecycle.Instance.IsClientRunning || (NetworkLifecycle.Instance.IsClientRunning && senderPlayer.PlayerId != NetworkLifecycle.Instance.Server.SelfId))
            {
                processingAsHost = true;
                ProcessPacket(packet);
            }

            //send to all players in active area, except originator
            if (Station == null || Station.CullingManager == null || Station.CullingManager.ActivePlayers.Count == 0)
                return;

            foreach (var player in Station.CullingManager.ActivePlayers)
            {
                if (player.PlayerId != senderPlayer.PlayerId)
                {
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteractionPacketAsHost() Sending interaction packet to player: {player.Username}");
                    NetworkLifecycle.Instance.Server.SendPitStopPlugInteractionPacket(player, packet);
                }
            }

        }
        else
        {
            //Failed to validate, player needs to rollback interaction
            NetworkLifecycle.Instance.Server.SendPitStopPlugInteractionPacket(senderPlayer, new CommonPitStopPlugInteractionPacket
            {
                NetId = NetId,
                InteractionType = PlugInteractionType.Rejected,
            });
        }
    }

    public bool ValidateInteraction(CommonPitStopPlugInteractionPacket packet, ServerPlayer player)
    {
        PlugInteractionType interactionType = packet.InteractionType;

        if (interactionType == PlugInteractionType.Rejected || interactionType == PlugInteractionType.Yanked)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} sent an invalid interaction type ({interactionType})!");
            return false;
        }

        //validate ownership of object
        if (HeldBy == null && interactionType != PlugInteractionType.PickedUp)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to interact with a plug that they are not holding!");
            return false;
        }

        //ensure the player is holding the object or no one is holding the object
        if (HeldBy != null && HeldBy != player)
        {
            NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to interact with a plug that is held by {HeldBy.Username}");
            return false;
        }

        if (interactionType == PlugInteractionType.DockSocket)
        {

            //verify TrainCar
            if (packet.TrainCarNetId == 0 || !NetworkedTrainCar.TryGet(packet.TrainCarNetId, out NetworkedTrainCar networkedTrainCar) || networkedTrainCar == null)
            {
                Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ValidateInteraction() NetId: {NetId}, trainCarNetId: {packet.TrainCarNetId}, NetworkedTrainCar not found!");
                return false;
            }

            //verify TrainCar is in station
            if (!(Station?.Station?.pitstop?.carList?.Contains(networkedTrainCar.TrainCar) ?? false))
            {
                Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ValidateInteraction() NetId: {NetId}, trainCarNetId: {packet.TrainCarNetId}, Not in Pitstop car list!");
                return false;
            }

            //verify socket exists (only locos have sockets)
            var socket = GetTrainCarSocket(networkedTrainCar, packet.SocketIndex);
            if (socket == null)
            {
                NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to insert plug that into a socket that doesn't exist!");
                return false;
            }

            //verify socket is compatible
            if (!socket.CanAccept(PluggableObject) && socket.Plug != PluggableObject)
            {
                NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to dock a {PluggableObject.connectionTag} plug into a {socket.connectionTag} socket, but socket is not compatible!");
                return false;
            }

            //verify distance to socket
            float sqrDistance = (socket.transform.GetWorldAbsolutePosition() - PluggableObject.transform.GetWorldAbsolutePosition()).sqrMagnitude;
            if (sqrDistance > DOCK_SQR_DISTANCE)
            {
                NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to dock a plug into {networkedTrainCar.TrainCar.ID}, but socket is too far away!");
                return false;
            }
        }
        else
        {
            if (interactionType == PlugInteractionType.DockHome)
            {
                //verify distance to socket
                var socket = PluggableObject.startAttachedTo;
                float sqrDistance = (socket.transform.GetWorldAbsolutePosition() - PluggableObject.transform.GetWorldAbsolutePosition()).sqrMagnitude;
                if (sqrDistance > DOCK_SQR_DISTANCE)
                {
                    NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to dock a plug into the stand, but socket is too far away!");
                    return false;
                }
            }
            else if (interactionType == PlugInteractionType.Dropped)
            {
                // no verifications required
            }
            else if (interactionType == PlugInteractionType.PickedUp)
            {
                float sqrDistance = (player.AbsoluteWorldPosition - PluggableObject.transform.GetWorldAbsolutePosition()).sqrMagnitude;

                Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ValidateInteraction() NetId: {NetId}, {interactionType}, player pos: {player.AbsoluteWorldPosition}, plug pos: {PluggableObject.transform.GetWorldAbsolutePosition()}, sqrDistance: {sqrDistance}, Raycast distance: {GRAB_SQR_DISTANCE}");
                if (sqrDistance > GRAB_SQR_DISTANCE)
                {
                    NetworkLifecycle.Instance.Server.LogWarning($"{player.Username} attempted to interact with a plug that is too far away!");
                    return false;
                }
            }
        }

        return true;
    }

    public void YankedByRope(Vector3 force, ForceMode mode)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.YankedByRope() [{transform.parent.name}, {NetId}] station: {Station?.StationName}, force: {force}");

        //cancel any client events
        currentInteraction = PlugInteractionType.Rejected;

        HeldBy = null;
        TrainCarNetId = INVALID_NETID;
        SocketIndex = INVALID_SOCKET;
        IsConnecting = false;

        var packet = new CommonPitStopPlugInteractionPacket
        {
            NetId = NetId,
            InteractionType = PlugInteractionType.Yanked,
            Position = transform.position - WorldMover.currentMove,
            Rotation = transform.rotation,
            YankForce = force,
            YankMode = mode,
        };

        //Allow host to process packet
        processingAsHost = true;
        ProcessPacket(packet);
        processingAsHost = false;

        //send to all players in active area, except originator and self client
        if (Station == null || Station.CullingManager == null || Station.CullingManager.ActivePlayers.Count == 0)
            return;

        foreach (var player in Station.CullingManager.ActivePlayers)
            NetworkLifecycle.Instance.Server.SendPitStopPlugInteractionPacket(player, packet);
    }

    public void SnappedByRope()
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.SnappedByRope() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");

        //cancel any client events
        currentInteraction = PlugInteractionType.Rejected;

        HeldBy = null;
        TrainCarNetId = INVALID_NETID;
        SocketIndex = INVALID_SOCKET;
        IsConnecting = false;

        var packet = new CommonPitStopPlugInteractionPacket
        {
            NetId = NetId,
            InteractionType = PlugInteractionType.DockHome,
        };

        //Allow host to process packet
        processingAsHost = true;
        ProcessPacket(packet);
        processingAsHost = false;

        //send to all players in active area, except originator and self client
        if (Station == null || Station.CullingManager == null || Station.CullingManager.ActivePlayers.Count == 0)
            return;

        foreach (var player in Station.CullingManager.ActivePlayers)
            NetworkLifecycle.Instance.Server.SendPitStopPlugInteractionPacket(player, packet);
    }

    private void OnPlayerDisconnected(ServerPlayer disconnectedPlayer)
    {
        if (HeldBy == null || HeldBy != disconnectedPlayer)
            return;

        HeldBy = null;
        DropPlug();

        if (Station == null || Station.CullingManager == null || Station.CullingManager.ActivePlayers.Count == 0)
            return;

        //cache packet
        var packet = new CommonPitStopPlugInteractionPacket
        {
            NetId = NetId,
            InteractionType = PlugInteractionType.Dropped,
        };

        foreach (var player in Station.CullingManager.ActivePlayers)
        {
            if (player != disconnectedPlayer && player.PlayerId != NetworkLifecycle.Instance.Server.SelfId)
                NetworkLifecycle.Instance.Server.SendPitStopPlugInteractionPacket(player, packet);
        }
    }

    #endregion

    #region Common

    public void ProcessPacket(CommonPitStopPlugInteractionPacket packet)
    {
        ProcessInteraction(packet.InteractionType, packet.PlayerId, packet.TrainCarNetId, packet.SocketIndex, packet.Position, packet.Rotation, packet.YankForce, packet.YankMode);
    }

    public void ProcessBulkUpdate(PitStopPlugData data)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessBulkUpdate() netId: {NetId}");
        CoroutineManager.Instance.StartCoroutine(WaitForInit(data));
    }

    private IEnumerator WaitForInit(PitStopPlugData data)
    {
        yield return new WaitUntil(() => PluggableObject != null && PluggableObject.initialized);

        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.WaitForInit() netId: {NetId} Complete");

        var interaction = data.State;
        ProcessInteraction(interaction, data.PlayerId, data.TrainCarNetId, data.SocketIndex, data.Position, data.Rotation);

        //wait 1 frame for plugs that are docking
        yield return null;
        //clear the docking flag
        if (interaction == PlugInteractionType.DockSocket || interaction == PlugInteractionType.DockHome)
            IsConnecting = false;

        //allow the player to interact
        Refreshed = true;
    }

    public void ProcessInteraction(PlugInteractionType interaction, byte playerId, ushort trainNetId, sbyte socketIndex, Vector3? newPosition, Quaternion? newRotation, Vector3? yankForce = null, ForceMode yankMode = ForceMode.Impulse)
    {
        bool result;
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.ProcessInteraction({interaction}, {playerId}, {trainNetId}, {socketIndex}, {newPosition?.ToString()}, {newRotation?.ToString()}, {yankForce}, {yankMode}) netId: {NetId}");

        switch (interaction)
        {
            case PlugInteractionType.Rejected:
                //todo implement rejection
                break;

            case PlugInteractionType.PickedUp:
                //Handle the picked up state
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, Picked Up, player: {playerHolding}");

                GrabPlug(playerId);

                break;

            case PlugInteractionType.Dropped:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, Dropped");

                DropPlug();

                if (newPosition == null || newRotation == null)
                    return;

                transform.position = (Vector3)newPosition + WorldMover.currentMove;
                transform.rotation = (Quaternion)newRotation;

                break;

            case PlugInteractionType.Yanked:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, Yanked");

                DropPlug();

                if (newPosition != null || newRotation != null)
                {
                    transform.position = (Vector3)newPosition + WorldMover.currentMove;
                    transform.rotation = (Quaternion)newRotation;
                }

                PlugRB?.AddForce((Vector3)yankForce, yankMode);

                CoroutineManager.Instance.StartCoroutine(WaitForYankSettle());

                break;

            case PlugInteractionType.DockHome:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockHome");

                DropPlug();

                //result = PluggableObject.InstantSnapTo(PluggableObject.startAttachedTo);
                result = PluggableObject.StartSnappingTo(PluggableObject.startAttachedTo, true);
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockHome, result: {result}");
                break;

            case PlugInteractionType.DockSocket:
                Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {trainNetId}, isLeft: {socketIndex}");

                DockTrainCar(trainNetId, socketIndex);
                break;
        }
    }

    private void BlockInteraction(bool block)
    {
        Multiplayer.LogDebug(() => $"BlockInteraction({block})");
        if (block)
        {
            PluggableObject.DisableStandaloneComponents();
            PluggableObject.DisableColliders();
        }
        else
        {
            PluggableObject.EnableStandaloneComponents();
            PluggableObject.EnableColliders();
        }
    }

    public void InitPitStop(NetworkedPitStopStation netPitStop)
    {
        if (NetId == 0)
            base.Awake();

        if (plugToStation.TryGetValue(this, out _))
        {
            Multiplayer.LogWarning($"Lookup cache 'plugToStation' already contains NetworkedPitStopStation \"{netPitStop?.StationName}\", skipping Init");
            return;
        }

        Station = netPitStop;
        plugToStation.Add(this, netPitStop);

        if (PluggableObject == null)
            PluggableObject = GetComponent<PluggableObject>();

        if (PluggableObject != null)
            plugToNetworkedPluggable.Add(PluggableObject, this);
    }

    public void DropPlug()
    {
        if (playerHolding != 0)
        {
            if (NetworkLifecycle.Instance.IsClientRunning &&
            NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerHolding, out var player))
            {
                player.DropItem();
            }
        }

        playerHolding = 0;
        PluggableObject.controlGrabbed = false;
        BlockInteraction(false);

        PluggableObject.Unplug();
        PluggableObject.controlBase?.ForceEndInteraction();

        TrainCarNetId = INVALID_NETID;
        SocketIndex = INVALID_SOCKET;
    }

    public void GrabPlug(byte playerId)
    {
        playerHolding = playerId;
        PluggableObject.controlGrabbed = true;
        BlockInteraction(true);

        PluggableObject.Unplug();

        TrainCarNetId = INVALID_NETID;
        SocketIndex = INVALID_SOCKET;

        //attach to a player
        if (NetworkLifecycle.Instance.IsClientRunning &&
            NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(playerHolding, out var player))
        {
            var target = grabHandler?.customGrabAnchor?.GetGrabAnchor();
            player.HoldItem(gameObject, target?.localPos, target?.localRot);
        }
    }

    private void DockTrainCar(ushort trainNetId, sbyte socketIndex)
    {
        DropPlug();

        if (NetworkedTrainCar.TryGet(trainNetId, out NetworkedTrainCar netTrainCar))
        {
            var socket = GetTrainCarSocket(netTrainCar, socketIndex);
            if (socket == null)
            {
                Multiplayer.LogWarning($"Failed to dock plug in loco socket, socket not found! Plug NetId: {NetId}, TrainCar: [{netTrainCar.CurrentID}, {trainNetId}]");
                return;
            }

            //bool result = PluggableObject.InstantSnapTo(socket);
            bool result = PluggableObject.StartSnappingTo(socket, true);

            if (result)
            {
                TrainCarNetId = trainNetId;
                SocketIndex = socketIndex;
            }

            Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {trainNetId}, isLeft: {socketIndex}, result: {result}");
        }
        else
        {
            Multiplayer.LogDebug(() => $"ProcessPacket() NetId: {NetId}, DockSocket, trainCar: {trainNetId}. TrainCar not found!");
        }
    }

    public PlugSocket GetTrainCarSocket(NetworkedTrainCar netTrainCar, sbyte socketIndex)
    {
        if (netTrainCar == null || netTrainCar.TrainCar == null)
            return null;

        if (socketIndex < 0 || socketIndex >= netTrainCar.TrainCar.FuelSockets.Length)
        {
            Multiplayer.LogWarning($"Failed to find socket {socketIndex} in TrainCar: [{netTrainCar.CurrentID}, {netTrainCar.NetId}], index is out of bounds!");
            return null;
        }

        return netTrainCar.TrainCar.FuelSockets[socketIndex];
    }

    private IEnumerator WaitForYankSettle()
    {
        Multiplayer.LogDebug(() => $"WaitForYankSettle() PluggableObject.yankOutOfHand: {PluggableObject.yankOutOfHand}, velocity: {PlugRB.velocity.sqrMagnitude}");
        PluggableObject.yankOutOfHand = false; //block docking

        //allow force to be applied
        yield return new WaitForFixedUpdate();
        Multiplayer.LogDebug(() => $"WaitForYankSettle() post-WaitForFixed, PluggableObject.yankOutOfHand: {PluggableObject.yankOutOfHand}, velocity: {PlugRB.velocity.sqrMagnitude}");

        float time = Time.time;

        //wait for rigid body to come to rest
        yield return new WaitUntil(() => Mathf.Approximately(PlugRB.velocity.sqrMagnitude, 0.0f) || (Time.time - time > 2.0f));

        Multiplayer.LogDebug(() => $"WaitForYankSettle() PluggableObject.yankOutOfHand: {PluggableObject.yankOutOfHand}, velocity: {PlugRB.velocity.sqrMagnitude}, delta Time: {Time.time - time}");

        //wait for plug to come to rest (prevent docking home)
        yield return new WaitForFixedUpdate();

        PluggableObject.yankOutOfHand = true; //unblock docking
    }
    #endregion

    #region Client
    private void OnGrabbed(ControlImplBase control)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnGrabbed() station: {Station?.StationName}, processing: {NetworkLifecycle.Instance.IsProcessingPacket}, processing as Host: {processingAsHost}, refreshed: {Refreshed}, isConnecting: {IsConnecting}");

        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        //Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnGrabbed() post [{transform.parent.name}, {NetId}] station: {Station?.StationName}");

        //Prevent new players/players entering the area from sending packets until initalised
        if (!Refreshed)
            return;

        TrainCarNetId = INVALID_NETID;
        SocketIndex = INVALID_SOCKET;

        currentInteraction = PlugInteractionType.PickedUp;
    }

    private void OnUngrabbed(ControlImplBase control)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnUngrabbed() station: {Station?.StationName}, processing: {NetworkLifecycle.Instance.IsProcessingPacket}, processing as Host: {processingAsHost}, refreshed: {Refreshed}, isConnecting: {IsConnecting}");

        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        //Prevent new players/players entering the area from sending packets until initalised
        if (!Refreshed)
            return;

        //Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnUngrabbed() station: {Station?.StationName}, plugging state: {PluggableObject.State}");

        // If we're snapping to a socket, don't send the Dropped packet
        if (IsConnecting)
            return;

        TrainCarNetId = INVALID_NETID;
        SocketIndex = INVALID_SOCKET;

        currentInteraction = PlugInteractionType.Dropped;
    }

    private void OnPluggedIn(PluggableObject plug, PlugSocket socket)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() station: {Station?.StationName}, processing: {NetworkLifecycle.Instance.IsProcessingPacket}, processing as Host: {processingAsHost}, refreshed: {Refreshed}, isConnecting: {IsConnecting}");

        if (NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        //Prevent new players/players entering the area from sending packets until initalised
        if (!Refreshed)
            return;

        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() [{transform.parent.name}, {NetId}] station: {Station?.StationName}");

        PlugInteractionType interaction;
        ushort carNetId = 0;

        if (socket == plug.startAttachedTo)
        {
            interaction = PlugInteractionType.DockHome;
            SocketIndex = INVALID_SOCKET;
        }
        else
        {
            var trainCar = TrainCar.Resolve(socket.gameObject);
            if (trainCar != null && trainCar.FuelSockets != null)
            {
                if (!NetworkedTrainCar.TryGetFromTrainCar(trainCar, out var netTrainCar))
                {
                    Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() NetworkedTrainCar: {trainCar?.ID} Not Found! Socket: {socket.GetObjectPath()}");
                    return;
                }

                carNetId = netTrainCar.NetId;

                interaction = PlugInteractionType.DockSocket;
                SocketIndex = (sbyte)Array.IndexOf(trainCar.FuelSockets, socket);

                if (SocketIndex < 0)
                    Multiplayer.LogWarning(() => $"Socket not recognised for TrainCar [{trainCar.ID}, {netTrainCar.NetId}], socket: {socket.GetObjectPath()}");
            }
            else
            {
                Multiplayer.LogDebug(() => $"NetworkedPluggableObject.OnPlugged() Socket not recognised: {socket.GetObjectPath()}");
                return;
            }
        }

        currentInteraction = interaction;
        TrainCarNetId = carNetId;
        IsConnecting = false;
    }
    #endregion
}
