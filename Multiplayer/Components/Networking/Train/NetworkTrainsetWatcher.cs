using System.Linq;
using DV.Utils;
using UnityEngine;
using JetBrains.Annotations;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Utils;
using Multiplayer.Networking.Data.Train;

namespace Multiplayer.Components.Networking.Train;

public class NetworkTrainsetWatcher : SingletonBehaviour<NetworkTrainsetWatcher>
{
    private ClientboundTrainsetPhysicsPacket cachedSendPacket;

    const float DESIRED_FULL_SYNC_INTERVAL = 2f; // in seconds
    const int MAX_UNSYNC_TICKS = (int)(NetworkLifecycle.TICK_RATE * DESIRED_FULL_SYNC_INTERVAL);
    public const float VELOCITY_THRESHOLD = 0.01f;

    protected override void Awake()
    {
        base.Awake();
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        cachedSendPacket = new ClientboundTrainsetPhysicsPacket();
        NetworkLifecycle.Instance.OnTick += Server_OnTick;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isQuitting)
            return;
        if (NetworkLifecycle.Instance.IsHost())
            NetworkLifecycle.Instance.OnTick -= Server_OnTick;
    }

    #region Server

    private void Server_OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        cachedSendPacket.Tick = tick;
        foreach (Trainset set in Trainset.allSets)
            Server_TickSet(set, tick);
    }
    private void Server_TickSet(Trainset set, uint tick)
    {
        bool anyCarMoving = false;
        bool maxTicksReached = false;
        bool anyTracksDirty = false;

        if (set == null)
        {
            Multiplayer.LogError($"Server_TickSet(): Received null set!");
            return;
        }

        cachedSendPacket.FirstNetId = set.firstCar.GetNetId();
        cachedSendPacket.LastNetId = set.lastCar.GetNetId();

        //car may not be initialised, missing a valid NetID
        if (cachedSendPacket.FirstNetId == 0 || cachedSendPacket.LastNetId == 0)
            return;

        foreach (TrainCar trainCar in set.cars)
        {
            if (trainCar == null || !trainCar.gameObject.activeSelf)
            {
                Multiplayer.LogError($"Trainset {set.id} ({set.firstCar?.GetNetId()} has a null or inactive ({trainCar?.gameObject.activeSelf}) car!");
                return;
            }

            //If we can locate the networked car, we'll add to the ticks counter and check if any tracks are dirty
            if (NetworkedTrainCar.TryGetFromTrainCar(trainCar, out NetworkedTrainCar netTC))
            {
                maxTicksReached |= netTC.TicksSinceSync >= MAX_UNSYNC_TICKS; //Even if the car is stationary, if the max ticks has been exceeded we will still sync
                anyTracksDirty |= netTC.BogieTracksDirty;
            }
            
            if (trainCar.derailed)
            {
                // Check if derailed car is actually moving
                float velocityMagnitude = trainCar.rb.velocity.magnitude;
                if (velocityMagnitude > VELOCITY_THRESHOLD)
                {
                    anyCarMoving = true;
                }
            }
            else if (!trainCar.isStationary)
                anyCarMoving = true;

            // We can finish checking early if we have either a car moving or a car not sync'd within the max-tick threshold
            if (anyCarMoving || maxTicksReached)
            {
                //Multiplayer.LogDebug(() => $"Server_TickSet() TrainCar {trainCar.ID} ({netTC?.NetId}) from set: {cachedSendPacket.FirstNetId} is moving or due for sync! stationary: {trainCar.isStationary}, RB velocity: {trainCar.rb.velocity} {trainCar.rb.velocity.magnitude}, tracks dirty: {netTC?.BogieTracksDirty} sync: {netTC?.TicksSinceSync >= MAX_UNSYNC_TICKS}");
                break;
            }
        }

        //if any car is dirty or exceeded its max ticks we will re-sync the entire train
        if (!anyCarMoving && !maxTicksReached)
            return;

        TrainsetMovementPart[] trainsetParts = new TrainsetMovementPart[set.cars.Count];
        
        for (int i = 0; i < set.cars.Count; i++)
        {
            TrainCar trainCar = set.cars[i];
            if (!trainCar.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            {
                Multiplayer.LogDebug(() => $"TrainCar {trainCar.ID} is not networked! Is active? {trainCar.gameObject.activeInHierarchy}");
                continue;
            }

            if (trainCar.derailed)
            {
                trainsetParts[i] = new TrainsetMovementPart(networkedTrainCar.NetId, RigidbodySnapshot.From(trainCar.rb));
            }
            else
            {
                Vector3? position = null;
                Quaternion? rotation = null;

                //Have we exceeded the max ticks?
                if (maxTicksReached)
                {
                    position = trainCar.transform.position - WorldMover.currentMove;
                    rotation = trainCar.transform.rotation;
                }

                trainsetParts[i] = new TrainsetMovementPart(
                    networkedTrainCar.NetId,
                    trainCar.GetForwardSpeed(),
                    trainCar.stress.slowBuildUpStress,
                    BogieData.FromBogie(trainCar.Bogies[0], networkedTrainCar.BogieTracksDirty),
                    BogieData.FromBogie(trainCar.Bogies[1], networkedTrainCar.BogieTracksDirty),
                    position,   //only used in full sync
                    rotation    //only used in full sync
                );
            }

            //reset this car's states
            networkedTrainCar.TicksSinceSync = 0;
            networkedTrainCar.BogieTracksDirty = false;
        }

        cachedSendPacket.TrainsetParts = trainsetParts;
        NetworkLifecycle.Instance.Server.SendTrainsetPhysicsUpdate(cachedSendPacket, anyTracksDirty);
    }
    #endregion

    #region Client

    public void Client_HandleTrainsetPhysicsUpdate(ClientboundTrainsetPhysicsPacket packet)
    {
        Trainset set = Trainset.allSets.Find(set => set.firstCar.GetNetId() == packet.FirstNetId || set.lastCar.GetNetId() == packet.FirstNetId ||
                                                    set.firstCar.GetNetId() == packet.LastNetId || set.lastCar.GetNetId() == packet.LastNetId);

        if (set == null)
        {
            Multiplayer.LogWarning($"Received {nameof(ClientboundTrainsetPhysicsPacket)} for unknown trainset with FirstNetId: {packet.FirstNetId} and LastNetId: {packet.LastNetId}");
            return;
        }

        if (set.cars.Count != packet.TrainsetParts.Length)
        {
            //log the discrepancies
            Multiplayer.LogWarning(
                $"Received {nameof(ClientboundTrainsetPhysicsPacket)} for trainset with FirstNetId: {packet.FirstNetId} and LastNetId: {packet.LastNetId} with {packet.TrainsetParts.Length} parts, but trainset has {set.cars.Count} parts");

            for (int i = 0; i < packet.TrainsetParts.Length; i++)
            {
                if (NetworkedTrainCar.Get(packet.TrainsetParts[i].NetId ,out NetworkedTrainCar networkedTrainCar))
                {
                    Multiplayer.LogDebug(()=>$"Applying TrainPhysicsUpdate to {packet.TrainsetParts[i].NetId}");
                    networkedTrainCar.Client_ReceiveTrainPhysicsUpdate(in packet.TrainsetParts[i], packet.Tick);
                }
                else
                {
                    Multiplayer.LogWarning($"Unable to apply TrainPhysicsUpdate to {packet.TrainsetParts[i].NetId}, NetworkedTrainCar not found!");
                }
            }
            return;
        }

        //Check direction of trainset vs packet
        if(set.firstCar.GetNetId() == packet.LastNetId)
            packet.TrainsetParts = packet.TrainsetParts.Reverse().ToArray();

        //Multiplayer.Log($"Client_HandleTrainsetPhysicsUpdate({set.firstCar.ID}):, tick: {packet.Tick}");

        for (int i = 0; i < packet.TrainsetParts.Length; i++)
        {
            if(set.cars[i].TryNetworked(out NetworkedTrainCar networkedTrainCar))
                networkedTrainCar.Client_ReceiveTrainPhysicsUpdate(in packet.TrainsetParts[i], packet.Tick);
            else
                Multiplayer.LogWarning($"Unable to apply TrainPhysicsUpdate to TrainSet with FirstNetId: {packet.FirstNetId}, NetworkedTrainCar not found!");
        }
    }
     
    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkTrainsetWatcher)}]";
    }
}
