using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Utils;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public class ServerPlayer : IDisposable
{
    public const byte MAX_CREW_NAME_LENGTH = 6;
    #region ID Management
    private static readonly IdPool<byte> idPool = new();

    public void Dispose()
    {
        Multiplayer.LogDebug(() => $"Disposing ServerPlayer {Username} ({PlayerId})");
        if (PlayerId != 0)
        {
            idPool.ReleaseId(PlayerId);
            PlayerId = 0;
        }
    }
    #endregion

    public ITransportPeer Peer { get; private set; }
    public byte PlayerId { get; private set; }
    public bool IsLoaded { get; set; }
    public string Username { get; set; }
    public string OriginalUsername { get; set; }
    public Guid Guid { get; set; }
    public Vector3 RawPosition { get; set; }
    public float RawRotationY { get; set; }
    public ushort CarId { get; set; }
    private string _crewName;
    public string CrewName
    {
        get
        {
            if (string.IsNullOrEmpty(_crewName))
                return string.Empty;
            return _crewName;
        }
        set
        {
            if (value != null)
            {
                if (value.Length > MAX_CREW_NAME_LENGTH)
                {
                    Multiplayer.LogWarning($"CrewName for player {Username} exceeds max length of {MAX_CREW_NAME_LENGTH}. Truncating.");
                    _crewName = value.Substring(0, MAX_CREW_NAME_LENGTH);
                }
                else
                {
                    _crewName = value;
                }
            }
            else
            {
                _crewName = string.Empty;
            }

            NetworkLifecycle.Instance.Server.SendPlayerPreferencesUpdate(this);
        }
    }
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(CrewName))
                return Username;
            return $"[{CrewName}] {Username}";
        }
    }

    public Dictionary<NetworkedItem, uint> KnownItems { get; private set; } = new Dictionary<NetworkedItem, uint>(); //NetworkedItem, last updated tick
    public Dictionary<NetworkedItem, float> NearbyItems { get; private set; } = new Dictionary<NetworkedItem, float>(); //NetworkedItem, time since near the item
    public HashSet<ushort> OwnedItems { get; private set; } = new HashSet<ushort>();
    public StorageBase Storage { get; set; } = new StorageBase();

    private Vector3 _lastWorldPos = Vector3.zero;
    private Vector3 _lastAbsoluteWorldPosition = Vector3.zero;

    public ServerPlayer(ITransportPeer peer, string username, string originalUsername, Guid guid)
    {
        PlayerId = idPool.NextId;

        Peer = peer;

        Username = username;
        OriginalUsername = originalUsername;
        Guid = guid;
    }

    #region Positioning
    public Vector3 AbsoluteWorldPosition
    {
        get
        {

            Vector3 pos;
            try
            {
                if (CarId == 0 || !NetworkedTrainCar.TryGet(CarId, out NetworkedTrainCar car))
                {
                    if (CarId != 0)
                        Multiplayer.LogDebug(() => $"AbsoluteWorldPosition() noID {Username}: CarId: {CarId}");

                    pos = RawPosition;
                }
                else
                {
                    //Multiplayer.LogDebug(() => $"AbsoluteWorldPosition() hasID {Username}: CarId: {CarId}");
                    pos = car.transform.TransformPoint(RawPosition) - WorldMover.currentMove; ;
                }

                _lastAbsoluteWorldPosition = pos;
            }
            catch (Exception e)
            {
                Multiplayer.LogWarning($"AbsoluteWorldPosition() Exception {Username}");
                Multiplayer.LogWarning(e.Message);
                Multiplayer.LogWarning(e.StackTrace);
                pos = _lastAbsoluteWorldPosition;
            }

            return pos;

        }
    }

    public Vector3 WorldPosition
    {
        get
        {
            Vector3 pos;
            try
            {
                if (CarId == 0 || !NetworkedTrainCar.TryGet(CarId, out NetworkedTrainCar car))
                {
                    if (CarId != 0)
                        Multiplayer.LogDebug(() => $"WorldPosition() noID {Username}: CarId: {CarId}");

                    pos = RawPosition + WorldMover.currentMove;
                }
                else
                {
                    //Multiplayer.LogDebug(() => $"WorldPosition() hasID {Username}: CarId: {CarId}");
                    pos = car.transform.TransformPoint(RawPosition);
                }

                _lastWorldPos = pos;
            }
            catch (Exception e)
            {
                Multiplayer.LogWarning($"WorldPosition() Exception {Username}");
                Multiplayer.LogWarning(e.Message);
                Multiplayer.LogWarning(e.StackTrace);

                pos = _lastWorldPos;
            }

            return pos;
        }
    }

    public float WorldRotationY => CarId == 0 || !NetworkedTrainCar.TryGet(CarId, out NetworkedTrainCar car)
        ? RawRotationY
        : (Quaternion.Euler(0, RawRotationY, 0) * car.transform.rotation).eulerAngles.y;
    #endregion

    #region Item Ownership
    public bool OwnsItem(ushort itemNetId) => OwnedItems.Contains(itemNetId);

    public void AddOwnedItem(ushort itemNetId)
    {
        OwnedItems.Add(itemNetId);
        NetworkLifecycle.Instance.Server.LogDebug(() => $"Player {Username} now owns item {itemNetId}");
    }

    public void AddOwnedItems(IEnumerable<ushort> itemNetIds)
    {
        OwnedItems.UnionWith(itemNetIds);
        NetworkLifecycle.Instance.Server.LogDebug(() => $"Player {Username} batch added items: {string.Join(", ", itemNetIds)}");
    }

    public void RemoveOwnedItem(ushort itemNetId)
    {
        if (OwnedItems.Remove(itemNetId))
        {
            NetworkLifecycle.Instance.Server.LogDebug(() => $"Player {Username} no longer owns item {itemNetId}");
        }
    }

    public void ClearOwnedItems()
    {
        OwnedItems.Clear();
        NetworkLifecycle.Instance.Server.LogDebug(() => $"Cleared all owned items for player {Username}");
    }

    public bool TryGetOwnedItem(ushort itemNetId, out NetworkedItem item)
    {
        if (OwnedItems.Contains(itemNetId) && NetworkedItem.TryGet(itemNetId, out item))
        {
            return true;
        }
        item = null;
        return false;
    }
    #endregion

    public override string ToString()
    {
        return $"{PlayerId} ({Username}, {Guid.ToString()})";
    }
}
