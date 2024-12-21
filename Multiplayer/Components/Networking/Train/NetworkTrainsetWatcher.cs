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
            Server_TickSet(set);
    }

    private void Server_TickSet(Trainset set)
    {
        bool anyCarMoving = false;
        bool maxTicksReached = false;
        bool anyTracksDirty = false;

        if (set == null)
        {
            Multiplayer.LogError($"Server_TickSet(): Received null set!");
            return;
        }

        cachedSendPacket.NetId = set.firstCar.GetNetId();
        //car may not be initialised, missing a valid NetID
        if (cachedSendPacket.NetId == 0)
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
                maxTicksReached |=  netTC.TicksSinceSync >= MAX_UNSYNC_TICKS;
                anyTracksDirty |= netTC.BogieTracksDirty;
            }

            //Even if the car is stationary, if the max ticks has been exceeded we will still sync
            if (!trainCar.isStationary)
                anyCarMoving = true;

            //we can finish checking early if we have BOTH a dirty and a max ticks
            if (anyCarMoving && maxTicksReached)
                break;
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
                trainsetParts[i] = new TrainsetMovementPart(RigidbodySnapshot.From(trainCar.rb));
            }
            else
            {
                Vector3? position = null;
                Quaternion? rotation = null;

                //Have we exceeded the max ticks?
                if (maxTicksReached)
                {
                    //Multiplayer.Log($"Max Ticks Reached for TrainSet with cars {set.firstCar.ID}, {set.lastCar.ID}");

                    position = trainCar.transform.position - WorldMover.currentMove;
                    rotation = trainCar.transform.rotation;
                    networkedTrainCar.TicksSinceSync = 0;   //reset this car's tick count
                }

                trainsetParts[i] = new TrainsetMovementPart(
                    trainCar.GetForwardSpeed(),
                    trainCar.stress.slowBuildUpStress,
                    BogieData.FromBogie(trainCar.Bogies[0], networkedTrainCar.BogieTracksDirty, networkedTrainCar.Bogie1TrackDirection),
                    BogieData.FromBogie(trainCar.Bogies[1], networkedTrainCar.BogieTracksDirty, networkedTrainCar.Bogie2TrackDirection),
                    position,   //only used in full sync
                    rotation    //only used in full sync
                );
            }
        }

        //Multiplayer.Log($"Server_TickSet({set.firstCar.ID}): SendTrainsetPhysicsUpdate, tick: {cachedSendPacket.Tick}");
        cachedSendPacket.TrainsetParts = trainsetParts;
        NetworkLifecycle.Instance.Server.SendTrainsetPhysicsUpdate(cachedSendPacket, anyTracksDirty);
    }

    #endregion

    #region Client

    public void Client_HandleTrainsetPhysicsUpdate(ClientboundTrainsetPhysicsPacket packet)
    {
        Trainset set = Trainset.allSets.Find(set => set.firstCar.GetNetId() == packet.NetId || set.lastCar.GetNetId() == packet.NetId);
        if (set == null)
        {
            Multiplayer.LogDebug(() => $"Received {nameof(ClientboundTrainsetPhysicsPacket)} for unknown trainset with netId {packet.NetId}");
            return;
        }

        if (set.cars.Count != packet.TrainsetParts.Length)
        {
            Multiplayer.LogDebug(() =>
                $"Received {nameof(ClientboundTrainsetPhysicsPacket)} for trainset with netId {packet.NetId} with {packet.TrainsetParts.Length} parts, but trainset has {set.cars.Count} parts");
            return;
        }

        //Multiplayer.Log($"Client_HandleTrainsetPhysicsUpdate({set.firstCar.ID}):, tick: {packet.Tick}");

        for (int i = 0; i < packet.TrainsetParts.Length; i++)
        {
            if(set.cars[i].TryNetworked(out NetworkedTrainCar networkedTrainCar))
                networkedTrainCar.Client_ReceiveTrainPhysicsUpdate(in packet.TrainsetParts[i], packet.Tick);
        }
    }
     
    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkTrainsetWatcher)}]";
    }
}
