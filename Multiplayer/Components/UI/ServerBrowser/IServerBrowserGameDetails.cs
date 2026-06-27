using Multiplayer.Networking.Data;
using System;

namespace Multiplayer.Components.UI.ServerBrowser;

public enum ServerVisibility : int
{
    Private = 0,
    Friends = 1,
    Public = 2
}

public interface IServerBrowserGameDetails : IDisposable
{
    string id { get; set; }
    string ipv6 { get; set; }
    string ipv4 { get; set; }
    string LocalIPv4 { get; set; }
    string LocalIPv6 { get; set; }
    int port { get; set; }
    string Name { get; set; }
    bool HasPassword { get; set; }
    int GameMode { get; set; }
    int Difficulty { get; set; }
    string TimePassed { get; set; }
    int CurrentPlayers { get; set; }
    int MaxPlayers { get; set; }
    ModInfo[] RequiredMods { get; set; }
    string GameVersion { get; set; }
    string MultiplayerVersion { get; set; }
    string ServerDetails { get; set; }
    int Ping {get; set; }
    ServerVisibility Visibility { get; set; }
    int LastSeen { get; set; }
}
