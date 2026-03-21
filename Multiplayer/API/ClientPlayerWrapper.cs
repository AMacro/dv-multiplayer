using MPAPI.Interfaces;
using Multiplayer.Components.Networking.Player;
using UnityEngine;

namespace Multiplayer.API;

public class ClientPlayerWrapper : IPlayer
{
    private readonly NetworkedPlayer _networkedPlayer;
    private readonly bool _isHost;

    public ClientPlayerWrapper(NetworkedPlayer networkedPlayer, bool isHost = false)
    {
        _networkedPlayer = networkedPlayer;
        _isHost = isHost;
    }

    public byte PlayerId => _networkedPlayer.PlayerId;
    public string Username
    {
        get => _networkedPlayer.Username;
        set => _networkedPlayer.Username = value;
    }
    public string CrewName
    {
        get => _networkedPlayer.CrewName;
        set => _networkedPlayer.CrewName = value;
    }
    public string DisplayName => _networkedPlayer.DisplayName;

    public Vector3 Position => _networkedPlayer.transform.position;
    public float RotationY => _networkedPlayer.transform.rotation.eulerAngles.y;
    public bool IsLoaded => true; // If we have the object, it's loaded
    public bool IsHost => _isHost;
    public int Ping => _networkedPlayer.GetPing();
    public bool IsOnCar => _networkedPlayer.IsOnCar;
    public TrainCar OccupiedCar => _networkedPlayer.OccupiedCar;
}
