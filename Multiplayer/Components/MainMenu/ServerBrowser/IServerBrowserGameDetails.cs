using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Newtonsoft.Json;

namespace Multiplayer.Components.MainMenu
{
    // 
    public interface IServerBrowserGameDetails : IDisposable
    {
        string id { get; set; }
        string ip { get; set; }
        int port { get; set; }
        string Name { get; set; }
        bool HasPassword { get; set; }
        int GameMode { get; set; }
        int Difficulty { get; set; }
        string TimePassed { get; set; }
        int CurrentPlayers { get; set; }
        int MaxPlayers { get; set; }
        string RequiredMods { get; set; }
        string GameVersion { get; set; }
        string MultiplayerVersion { get; set; }
        string ServerDetails { get; set; }
        int Ping { get; set; }
        
    }
}
