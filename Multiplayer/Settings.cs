using System;
using Humanizer;
using UnityEngine;
using UnityModManagerNet;
using Console = DV.Console;

namespace Multiplayer;

[Serializable]
[DrawFields(DrawFieldMask.OnlyDrawAttr)]
public class Settings : UnityModManager.ModSettings, IDrawable
{
    public const byte MAX_USERNAME_LENGTH = 24;

    public static Action<Settings> OnSettingsUpdated;

    [Header("Player")]
    [Draw("Username", Tooltip = "Your username in-game")]
    public string Username = "Player";
    public string Guid = System.Guid.NewGuid().ToString();

    [Space(10)]
    [Header("Server")]
    [Draw("Server Name", Tooltip = "Name of your server in the lobby browser.")]
    public string ServerName = "";
    [Draw("Password", Tooltip = "The password required to join your server. Leave blank for no password.")]
    public string Password = "";
    [Draw("Public Game", Tooltip = "Public servers are listed in the lobby browser")]
    public bool PublicGame = true;
    [Draw("Max Players", Tooltip = "The maximum number of players that can join your server, including yourself.")]
    public int MaxPlayers = 4;
    [Draw("Port", Tooltip = "The port that your server will listen on. You generally don't need to change this.")]
    public int Port = 7777;
    [Draw("Details", Tooltip = "Details shown in the server browser")]
    public string Details = "";


    [Space(10)]
    [Header("Lobby Server")]
    [Draw("Lobby Server address", Tooltip = "Address of lobby server for finding multiplayer games")]
    public string LobbyServerAddress = "https://dv.mineit.space";//"http://localhost:8080";
    [Header("Last Server Connected to by IP")]
    [Draw("Last Remote IP", Tooltip = "The IP for the last server connected to by IP.")]
    public string LastRemoteIP = "";
    [Draw("Last Remote Port", Tooltip = "The port for the last server connected to by IP.")]
    public int LastRemotePort = 7777;
    [Draw("Last Remote Password", Tooltip = "The password for the last server connected to by IP.")]
    public string LastRemotePassword = "";


    [Space(10)]
    [Header("Last Server Connected to by IP")]
    [Draw("Last Remote IP", Tooltip = "The IP for the last server connected to by IP.")]
    public string LastRemoteIP = "";
    [Draw("Last Remote Port", Tooltip = "The port for the last server connected to by IP.")]
    public int LastRemotePort = 7777;
    [Draw("Last Remote Password", Tooltip = "The password for the last server connected to by IP.")]
    public string LastRemotePassword = "";
    

    [Space(10)]
    [Header("Preferences")]
    [Draw("Show Name Tags", Tooltip = "Whether to show player names above their heads.")]
    public bool ShowNameTags = true;
    [Draw("Show Ping In Name Tag", Tooltip = "Whether to show player pings above their heads.", VisibleOn = "ShowNameTags|true")]
    public bool ShowPingInNameTags;

    [Space(10)]
    [Header("Advanced Settings")]
    [Draw("Show Advanced Settings", Tooltip = "You probably don't need to change these.")]
    public bool ShowAdvancedSettings;
    [Draw("Show Stats", Tooltip = "Whether to show network statistics.", VisibleOn = "ShowAdvancedSettings|true")]
    public bool ShowStats;
    [Draw("Stats List Size", Tooltip = "How many packets to list in the network statistics gui.", VisibleOn = "ShowStats|true")]
    public int StatsListSize = 3;
    [Draw("Debug Logging", Tooltip = "Whether to log extra information. This is useful for debugging, but should otherwise be kept off.", VisibleOn = "ShowAdvancedSettings|true")]
    public bool DebugLogging;
    [Draw("Enable Log File", Tooltip = "Whether to create a separate file for logs. This is useful for debugging, but should otherwise be kept off.", VisibleOn = "ShowAdvancedSettings|true")]
    public bool EnableLogFile;
    [Draw("Enable NAT Punch", VisibleOn = "ShowAdvancedSettings|true")]
    public bool EnableNatPunch = true;
    [Draw("Reuse NetPacketReaders", VisibleOn = "ShowAdvancedSettings|true")]
    public bool ReuseNetPacketReaders = true;
    [Draw("Use Native Sockets", VisibleOn = "ShowAdvancedSettings|true")]
    public bool UseNativeSockets = true;
    [Draw("Log Full IPs", Tooltip = "Whether to log the full IP address of clients. This is useful for debugging, but should otherwise be kept off.", VisibleOn = "ShowAdvancedSettings|true")]
    public bool LogIps;
    [Draw("Simulate Packet Loss", VisibleOn = "ShowAdvancedSettings|true")]
    public bool SimulatePacketLoss;
    [Draw("Packet Loss Chance", VisibleOn = "SimulatePacketLoss|true")]
    public int SimulationPacketLossChance = 10;
    [Draw("Simulate Latency", VisibleOn = "ShowAdvancedSettings|true")]
    public bool SimulateLatency;
    [Draw("Minimum Latency (ms)", VisibleOn = "SimulateLatency|true")]
    public int SimulationMinLatency = 30;
    [Draw("Maximum Latency (ms)", VisibleOn = "SimulateLatency|true")]
    public int SimulationMaxLatency = 100;

    public void Draw(UnityModManager.ModEntry modEntry)
    {
        Settings self = this;
        UnityModManager.UI.DrawFields(ref self, modEntry, DrawFieldMask.OnlyDrawAttr, OnChange);
        if (ShowAdvancedSettings && GUILayout.Button("Enable Developer Commands"))
            Console.RegisterDevCommands();
    }

    public override void Save(UnityModManager.ModEntry modEntry)
    {
        Username = Username.Trim().Truncate(MAX_USERNAME_LENGTH);
        Port = Mathf.Clamp(Port, 1024, 49151);
        MaxPlayers = Mathf.Clamp(MaxPlayers, 1, byte.MaxValue);
        Password = Password?.Trim();
        if (!UnloadWatcher.isQuitting)
            OnSettingsUpdated?.Invoke(this);
        Save(this, modEntry);
    }

    public void OnChange()
    {
        // yup
    }

    public Guid GetGuid()
    {
        if (System.Guid.TryParse(Guid, out Guid guid))
            return guid;
        guid = System.Guid.NewGuid();
        Guid = guid.ToString();
        return guid;
    }
}
