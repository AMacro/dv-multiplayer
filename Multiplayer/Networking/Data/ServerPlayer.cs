using DV.JObjectExtstensions;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data.Player;
using Multiplayer.Networking.TransportLayers;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public ulong SteamId { get; }
    public string CharacterId { get; set; }
    public bool IsVR { get; }
    public uint LastHighPingTickLogged { get; set; }
    public uint LastTrackingTick { get; set; }
    public bool HasTrackingTick { get; set; }
    public bool CustomizationSnapshotSent { get; set; }
    internal bool InventoryReconciliationComplete { get; set; }
    internal bool InventoryReconciliationFailed { get; set; }
    public HashSet<uint> ProcessedCustomizationActionIds { get; } = [];

    public PlayerTrackingData TrackingData { get; set; }
    public PlayerPostureFlags Posture { get; set; }        // already exists — keep
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

            Dictionary<PlayerPreference, string> preferences = new()
            {
                { PlayerPreference.CrewName, _crewName }
            };

            NetworkLifecycle.Instance.Server.SendPlayerPreferencesUpdate(this, preferences);
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
    public IEnumerable<ushort> OwnedItems => NetworkedItem.GetAll()
        .Where(item => item != null && item.OwnerPlayerId == PlayerId)
        .Select(item => item.NetId);

    private Vector3 _lastWorldPos = Vector3.zero;
    private Vector3 _lastAbsoluteWorldPosition = Vector3.zero;

    public ServerPlayer(ITransportPeer peer, string username, string originalUsername, Guid guid, ulong steamId,
        string characterId, bool isVr)
    {
        PlayerId = idPool.NextId;

        Peer = peer;
        LastLogin = DateTime.UtcNow;

        Username = username;
        OriginalUsername = originalUsername;
        Guid = guid;
        SteamId = steamId;
        CharacterId = characterId;

        IsVR = isVr;
    }

    #region Positioning
    public Vector3 RawPosition => TrackingData.Position ?? Vector3.zero;
    public float RawRotationY => TrackingData.RotationY ?? 0f;

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
    public bool OwnsItem(ushort itemNetId) =>
        NetworkedItem.TryGet(itemNetId, out var item) && item.OwnerPlayerId == PlayerId;

    public bool TryGetOwnedItem(ushort itemNetId, out NetworkedItem item)
    {
        if (NetworkedItem.TryGet(itemNetId, out item) && item.OwnerPlayerId == PlayerId)
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
