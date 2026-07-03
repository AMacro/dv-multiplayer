using DV.JObjectExtstensions;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
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
    internal PlayerLoadingState LoadingState { get; set; } = PlayerLoadingState.None;
    public DateTime LastLogin { get; set; }
    private float PreviousPlayTime { get; set; }
    public float TotalPlaytime => PreviousPlayTime + (DateTime.UtcNow - LastLogin).Minutes;
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
    private float _lastWorldRotationY;

    public ServerPlayer(ITransportPeer peer, string username, string originalUsername, Guid guid)
    {
        PlayerId = idPool.NextId;

        Peer = peer;
        LastLogin = DateTime.UtcNow;

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
                if (CarId == 0)
                {
                    // Off-car: RawPosition is already a world-absolute position.
                    pos = RawPosition;
                    _lastAbsoluteWorldPosition = pos;
                }
                else if (NetworkedTrainCar.TryGet(CarId, out NetworkedTrainCar car))
                {
                    pos = car.transform.TransformPoint(RawPosition) - WorldMover.currentMove;
                    _lastAbsoluteWorldPosition = pos;
                }
                else
                {
                    // On a car we can't resolve yet (NetId assignment races with load, or the car just
                    // despawned). RawPosition is car-LOCAL here, so it can't be turned into a world
                    // position — return the last known-good value instead of interpreting a local
                    // offset as a world coordinate (which would place the player kilometres away and
                    // break distance/authority/reach checks).
                    Multiplayer.LogDebug(() => $"AbsoluteWorldPosition() noID {Username}: CarId: {CarId}");
                    pos = _lastAbsoluteWorldPosition;
                }
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
                if (CarId == 0)
                {
                    // Off-car: RawPosition is a world-absolute position; shift it into the moved frame.
                    pos = RawPosition + WorldMover.currentMove;
                    _lastWorldPos = pos;
                }
                else if (NetworkedTrainCar.TryGet(CarId, out NetworkedTrainCar car))
                {
                    pos = car.transform.TransformPoint(RawPosition);
                    _lastWorldPos = pos;
                }
                else
                {
                    // On a car we can't resolve yet (NetId assignment races with load, or the car just
                    // despawned). RawPosition is car-LOCAL here, so return the last known-good value
                    // rather than treating a local offset as a world coordinate (see AbsoluteWorldPosition).
                    Multiplayer.LogDebug(() => $"WorldPosition() noID {Username}: CarId: {CarId}");
                    pos = _lastWorldPos;
                }
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

    public float WorldRotationY
    {
        get
        {
            float rot;
            if (CarId == 0)
            {
                // Off-car: RawRotationY is already a world-space heading.
                rot = RawRotationY;
                _lastWorldRotationY = rot;
            }
            else if (NetworkedTrainCar.TryGet(CarId, out NetworkedTrainCar car))
            {
                rot = (Quaternion.Euler(0, RawRotationY, 0) * car.transform.rotation).eulerAngles.y;
                _lastWorldRotationY = rot;
            }
            else
            {
                // On a car we can't resolve yet (NetId race / just despawned). RawRotationY is car-LOCAL
                // here, so return the last known-good heading rather than a local rotation as world.
                rot = _lastWorldRotationY;
            }

            return rot;
        }
    }
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
