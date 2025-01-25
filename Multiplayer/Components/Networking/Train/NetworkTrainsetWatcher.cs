using System.Linq;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.Train;
using Multiplayer.Utils;

namespace Multiplayer.Components.Networking.Train;

public class NetworkTrainsetWatcher : SingletonBehaviour<NetworkTrainsetWatcher>
{
    private ClientboundTrainsetPhysicsPacket cachedSendPacket;

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
        cachedSendPacket.Tick = tick;
        foreach (Trainset set in Trainset.allSets)
            Server_TickSet(set);
    }

    private void Server_TickSet(Trainset set)
    {
        bool dirty = false;
        foreach (TrainCar trainCar in set.cars)
        {
            if (trainCar.isStationary)
                continue;
            dirty = true;
            break;
        }

        if (!dirty)
            return;

        cachedSendPacket.NetId = set.firstCar.GetNetId();

        if (set.cars.Contains(null))
        {
            Multiplayer.LogError($"Trainset {set.id} ({set.firstCar.GetNetId()} has a null car!");
            return;
        }

        if (set.cars.Any(car => !car.gameObject.activeSelf))
        {
            Multiplayer.LogError($"Trainset {set.id} ({set.firstCar.GetNetId()} has a non-active car!");
            return;
        }

        TrainsetMovementPart[] trainsetParts = new TrainsetMovementPart[set.cars.Count];
        bool anyTracksDirty = false;
        for (int i = 0; i < set.cars.Count; i++)
        {
            TrainCar trainCar = set.cars[i];
            if (!trainCar.TryNetworked(out NetworkedTrainCar _))
            {
                Multiplayer.LogDebug(() => $"TrainCar {trainCar.ID} is not networked! Is active? {trainCar.gameObject.activeInHierarchy}");
                continue;
            }

            NetworkedTrainCar networkedTrainCar = trainCar.Networked();
            anyTracksDirty |= networkedTrainCar.BogieTracksDirty;

            if (trainCar.derailed)
                trainsetParts[i] = new TrainsetMovementPart(RigidbodySnapshot.From(trainCar.rb));
            else
                trainsetParts[i] = new TrainsetMovementPart(
                    trainCar.GetForwardSpeed(),
                    trainCar.stress.slowBuildUpStress,
                    BogieData.FromBogie(trainCar.Bogies[0], networkedTrainCar.BogieTracksDirty, networkedTrainCar.Bogie1TrackDirection),
                    BogieData.FromBogie(trainCar.Bogies[1], networkedTrainCar.BogieTracksDirty, networkedTrainCar.Bogie2TrackDirection)
                );
        }

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

        for (int i = 0; i < packet.TrainsetParts.Length; i++)
            set.cars[i].Networked().Client_ReceiveTrainPhysicsUpdate(in packet.TrainsetParts[i], packet.Tick);
    }

    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkTrainsetWatcher)}]";
    }
}
