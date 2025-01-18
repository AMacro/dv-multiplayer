using System;
using Multiplayer.Networking.Data;
using Newtonsoft.Json;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Multiplayer.Components.Networking;
using DV.WeatherSystem;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplayer.Networking.Packets.Unconnected;
using System.Net;
using System.Linq;
using Steamworks;
using Steamworks.Data;

namespace Multiplayer.Networking.Managers.Server;
public class LobbyServerManager : MonoBehaviour
{
    //API endpoints
    private const string ENDPOINT_ADD_SERVER    = "add_game_server";
    private const string ENDPOINT_UPDATE_SERVER = "update_game_server";
    private const string ENDPOINT_REMOVE_SERVER = "remove_game_server";

    //RegEx
    private readonly Regex IPv4Match = new(@"(\b25[0-5]|\b2[0-4][0-9]|\b[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}");

    private const int REDIRECT_MAX = 5;

    private const int UPDATE_TIME_BUFFER = 10;                  //We don't want to miss our update, let's phone in just a little early
    private const int UPDATE_TIME = 120 - UPDATE_TIME_BUFFER;   //How often to update the lobby server - this should match the lobby server's time-out period
    private const int PLAYER_CHANGE_TIME = 5;                   //Update server early if the number of players has changed in this time frame

    private NetworkServer server;
    private string server_id;
    private string private_key;

    private Lobby lobby;

    private bool initialised = false;
    private bool sendUpdates = false;
    private float timePassed = 0f;

    //LAN discovery
    private NetManager discoveryManager;
    private NetPacketProcessor packetProcessor;
    private EventBasedNetListener discoveryListener;
    private readonly NetDataWriter cachedWriter = new();
    public static int[] discoveryPorts = [8888, 8889, 8890];

    #region
    public void Awake()
    {
        server = NetworkLifecycle.Instance.Server;

        Multiplayer.Log($"LobbyServerManager New({server != null})");

        if (DVSteamworks.Success)
        {
            CreateLobby();
        }
    }

    public async void CreateLobby()
    {
        // Check if the user is a legitimate Steam user
        if (!IsLegitimateSteamUser())
        {
            server.Log("User is suspected to be using a pirated copy. Lobby creation aborted.");
            return;
        }

        // Specify the lobby type (public, private, etc.)
        var result = await SteamMatchmaking.CreateLobbyAsync(server.serverData.MaxPlayers);

        if (result.HasValue)
        {
            // Lobby was created successfully
            server.Log("Steam Lobby created successfully!");
            lobby = result.Value;
            lobby.SetPublic();
            lobby.SetJoinable(true);
            lobby.SetData("Server Name", server.serverData.Name);
            lobby.SetData("Difficulty", server.serverData.Difficulty.ToString());
        }
        else
        {
            // Handle failure
            server.Log("Failed to create lobby.");
        }
    }

    private bool IsLegitimateSteamUser()
    {
        // Check if the Steam client is valid
        if (SteamClient.IsValid)
        {
            // Verify the Steam ID is valid
            if (SteamClient.SteamId.IsValid)
            {

                // Check if the game is installed using the App ID
                bool isInstalled = SteamApps.IsAppInstalled(DVSteamworks.APP_ID);

                if (isInstalled)
                {
                    System.Console.WriteLine($"Steam ID {SteamClient.SteamId} is valid and the game is installed.");
                    return true;
                }
                else
                {
                    // Log the piracy suspicion
                    server.Log($"Suspicion: Steam ID {SteamClient.SteamId} does not have the game installed. Potential piracy detected.");
                }
            }
        }

        // If Steam client or Steam ID is not valid, log as suspicious
        System.Console.WriteLine("Steam client is invalid or pirated Steam account detected.");
        server.Log("Suspicion: Invalid Steam client or pirated Steam account detected.");

        return false;
    }

    public IEnumerator Start()
    {
        server.serverData.ipv6 = GetStaticIPv6Address();
        server.serverData.LocalIPv4 = GetLocalIPv4Address();

        StartCoroutine(GetIPv4(Multiplayer.Settings.Ipv4AddressCheck));

        while(!initialised)
            yield return null;

        server.Log("Public IPv4: " + server.serverData.ipv4);
        server.Log("Public IPv6: " + server.serverData.ipv6);
        server.Log("Private IPv4: " + server.serverData.LocalIPv4);

        if (server.serverData.isPublic)
        {
            Multiplayer.Log($"Registering server at: {Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_ADD_SERVER}");
            StartCoroutine(RegisterWithLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_ADD_SERVER}"));

            //allow the server some time to register (should take less than a second)
            float timeout = 5f;
            while (server_id == null || server_id == string.Empty  || (timeout -= Time.deltaTime) <= 0)
                yield return null;


        }

        if(server_id == null || server_id == string.Empty)
        {
            server_id = Guid.NewGuid().ToString();
        }

        server.serverData.id = server_id;

        StartDiscoveryServer();
    }

    public void OnDestroy()
    {
        Multiplayer.Log($"LobbyServerManager OnDestroy()");
        sendUpdates = false;
        StopAllCoroutines();
        StartCoroutine(RemoveFromLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_REMOVE_SERVER}"));

        if (lobby.Id.IsValid)
        {
            lobby.SetJoinable(false);
            lobby.Leave();
        }

        discoveryManager?.Stop();
    }

    public void Update()
    {
        if (sendUpdates)
        {
            timePassed += Time.deltaTime;

            if (timePassed > UPDATE_TIME || (server.serverData.CurrentPlayers != server.PlayerCount && timePassed > PLAYER_CHANGE_TIME))
            {
                timePassed = 0f;
                server.serverData.CurrentPlayers = server.PlayerCount;
                StartCoroutine(UpdateLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_UPDATE_SERVER}"));
            }
        }else if (!server.serverData.isPublic || !sendUpdates)
        {
            server.serverData.CurrentPlayers = server.PlayerCount;
        }

        //Keep LAN discovery running
        discoveryManager?.PollEvents();
    }

    #endregion

    #region Lobby Server
    public void RemoveFromLobbyServer()
    {
        Multiplayer.Log($"RemoveFromLobbyServer OnDestroy()");
        sendUpdates = false;
        StopAllCoroutines();
        StartCoroutine(RemoveFromLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_REMOVE_SERVER}"));
    }

    private IEnumerator RegisterWithLobbyServer(string uri)
    {
        JsonSerializerSettings jsonSettings = new() { NullValueHandling = NullValueHandling.Ignore };
        string json = JsonConvert.SerializeObject(server.serverData, jsonSettings);
        Multiplayer.LogDebug(()=>$"JsonRequest: {json}");

        yield return SendWebRequest(
            uri,
            json,
            webRequest =>
            {
                LobbyServerResponseData response = JsonConvert.DeserializeObject<LobbyServerResponseData>(webRequest.downloadHandler.text);
                if (response != null)
                {
                    private_key = response.private_key;
                    server_id = response.game_server_id;

                    sendUpdates = true;
                }
            },
            webRequest => Multiplayer.LogError("Failed to register with lobby server")
        );
    }

    private IEnumerator RemoveFromLobbyServer(string uri)
    {
        JsonSerializerSettings jsonSettings = new() { NullValueHandling = NullValueHandling.Ignore };
        string json = JsonConvert.SerializeObject(new LobbyServerResponseData(server_id, private_key), jsonSettings);
        Multiplayer.LogDebug(() => $"JsonRequest: {json}");

        yield return SendWebRequest(
            uri,
            json,
            webRequest => Multiplayer.Log("Successfully removed from lobby server"),
            webRequest => Multiplayer.LogError("Failed to remove from lobby server")
        );
    }

    private IEnumerator UpdateLobbyServer(string uri)
    {
        JsonSerializerSettings jsonSettings = new() { NullValueHandling = NullValueHandling.Ignore };

        DateTime start = AStartGameData.BaseTimeAndDate;
        DateTime current = WeatherDriver.Instance.manager.DateTime;
        TimeSpan inGame = current - start;

        LobbyServerUpdateData reqData = new(
                                                server_id,
                                                private_key,
                                                inGame.ToString("d\\d\\ hh\\h\\ mm\\m\\ ss\\s"),
                                                server.serverData.CurrentPlayers
                                            );

        string json = JsonConvert.SerializeObject(reqData, jsonSettings);
        Multiplayer.LogDebug(() => $"UpdateLobbyServer JsonRequest: {json}");

        yield return SendWebRequest(
            uri,
            json,
            webRequest => Multiplayer.Log("Successfully updated lobby server"),
            webRequest =>
            {
                Multiplayer.LogError("Failed to update lobby server, attempting to re-register");

                //cleanup
                sendUpdates = false;
                private_key = null;
                server_id = null;

                //Attempt to re-register
                StartCoroutine(RegisterWithLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_ADD_SERVER}"));
            }
        );
    }

    private IEnumerator GetIPv4(string uri)
    {
 
        Multiplayer.Log("Preparing to get IPv4: " + uri);

        yield return SendWebRequestGET(
            uri,
            webRequest =>
            {
                Match match = IPv4Match.Match(webRequest.downloadHandler.text);
                if (match != null)
                {
                    Multiplayer.Log($"IPv4 address extracted: {match.Value}");
                    server.serverData.ipv4 = match.Value;     
                }
                else
                {
                    Multiplayer.LogError($"Failed to find IPv4 address. Server will only be available via IPv6");
                }

                initialised = true;

            },
            webRequest =>
            {
                Multiplayer.LogError("Failed to find IPv4 address. Server will only be available via IPv6");
                initialised = true;
            }
        );
    }

    private IEnumerator SendWebRequest(string uri, string json, Action<UnityWebRequest> onSuccess, Action<UnityWebRequest> onError, int depth=0)
    {
        if (depth > REDIRECT_MAX)
        {
            Multiplayer.LogError($"Reached maximum redirects: {uri}");
            yield break;
        }

        using UnityWebRequest webRequest = UnityWebRequest.Post(uri, json);
        webRequest.redirectLimit = 0;

        if (json != null && json.Length > 0)
        {
            webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
        }
        webRequest.downloadHandler = new DownloadHandlerBuffer();

        yield return webRequest.SendWebRequest();

        //check for redirect
        if (webRequest.responseCode >= 300 && webRequest.responseCode < 400)
        {
            string redirectUrl = webRequest.GetResponseHeader("Location");
            Multiplayer.LogWarning($"Lobby Server redirected, check address is up to date: '{redirectUrl}'");

            if (redirectUrl != null && redirectUrl.StartsWith("https://") && redirectUrl.Replace("https://", "http://") == uri)
            {
                yield return SendWebRequest(redirectUrl, json, onSuccess, onError, ++depth);
            }
        }
        else
        {
            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                Multiplayer.LogError($"SendWebRequest({uri}) responseCode: {webRequest.responseCode}, Error: {webRequest.error}\r\n{webRequest.downloadHandler.text}");
                onError?.Invoke(webRequest);
            }
            else
            {
                Multiplayer.Log($"Received: {webRequest.downloadHandler.text}");
                onSuccess?.Invoke(webRequest);
            }
        }
    }

    private IEnumerator SendWebRequestGET(string uri, Action<UnityWebRequest> onSuccess, Action<UnityWebRequest> onError, int depth = 0)
    {
        if (depth > REDIRECT_MAX)
        {
            Multiplayer.LogError($"Reached maximum redirects: {uri}");
            yield break;
        }

        using UnityWebRequest webRequest = UnityWebRequest.Get(uri);
        webRequest.redirectLimit = 0;
        webRequest.downloadHandler = new DownloadHandlerBuffer();

        yield return webRequest.SendWebRequest();

        //check for redirect
        if (webRequest.responseCode >= 300 && webRequest.responseCode < 400)
        {
            string redirectUrl = webRequest.GetResponseHeader("Location");
            Multiplayer.LogWarning($"Lobby Server redirected, check address is up to date: '{redirectUrl}'");

            if (redirectUrl != null && redirectUrl.StartsWith("https://") && redirectUrl.Replace("https://", "http://") == uri)
            {
                yield return SendWebRequestGET(redirectUrl, onSuccess, onError, ++depth);
            }
        }
        else
        {
            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                Multiplayer.LogError($"SendWebRequest({uri}) responseCode: {webRequest.responseCode}, Error: {webRequest.error}\r\n{webRequest.downloadHandler.text}");
                onError?.Invoke(webRequest);
            }
            else
            {
                Multiplayer.Log($"Received: {webRequest.downloadHandler.text}");
                onSuccess?.Invoke(webRequest);
            }
        }
    }
    #endregion
    public static string GetStaticIPv6Address()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            bool flag = !networkInterface.Supports(NetworkInterfaceComponent.IPv6) || networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel;
            if (!flag)
            {
                foreach (UnicastIPAddressInformation unicastIPAddressInformation in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    bool flag2 = unicastIPAddressInformation.Address.AddressFamily == AddressFamily.InterNetworkV6;
                    if (flag2)
                    {
                        bool flag3 = !unicastIPAddressInformation.Address.IsIPv6LinkLocal && !unicastIPAddressInformation.Address.IsIPv6SiteLocal && unicastIPAddressInformation.IsDnsEligible;
                        if (flag3)
                        {
                            return unicastIPAddressInformation.Address.ToString();
                        }
                    }
                }
            }
        }
        return null;
    }

    public static string GetLocalIPv4Address()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            bool flag = !networkInterface.Supports(NetworkInterfaceComponent.IPv4) || networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback;
            if (!flag)
            {
                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                if (properties.GatewayAddresses.Count == 0)
                    continue;

                foreach (UnicastIPAddressInformation unicastIPAddressInformation in properties.UnicastAddresses)
                {
                    bool flag2 = unicastIPAddressInformation.Address.AddressFamily == AddressFamily.InterNetwork;
                    if (flag2)
                    {
                        return unicastIPAddressInformation.Address.ToString();
                    }
                }
            }
        }
        return null;
    }

    #region LAN Discovery
    public void StartDiscoveryServer()
    {
        server.Log($"StartDiscoveryServer()");
        discoveryListener = new EventBasedNetListener();
        discoveryManager = new NetManager(discoveryListener)
                            {
                                IPv6Enabled = true,
                                UnconnectedMessagesEnabled = true,
                                BroadcastReceiveEnabled = true,
                                
                            };
        packetProcessor = new NetPacketProcessor(discoveryManager);

        discoveryListener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;

        packetProcessor.RegisterNestedType(LobbyServerData.Serialize, LobbyServerData.Deserialize);
        packetProcessor.SubscribeReusable<UnconnectedDiscoveryPacket, IPEndPoint>(OnUnconnectedDiscoveryPacket);

        //start listening for discovery packets
        int successPort = discoveryPorts.FirstOrDefault(port =>
            discoveryManager.Start(IPAddress.Any, IPAddress.IPv6Any, port));

        if (successPort != 0)
            server.Log($"Discovery server started on port {successPort}");
        else
            server.LogError("Failed to start discovery server on any port");
    }
    protected NetDataWriter WritePacket<T>(T packet) where T : class, new()
    {
        cachedWriter.Reset();
        packetProcessor.Write(cachedWriter, packet);
        return cachedWriter;
    }
    protected void SendUnconnectedPacket<T>(T packet, string ipAddress, int port) where T : class, new()
    {
        discoveryManager.SendUnconnectedMessage(WritePacket(packet), ipAddress, port);
    }
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        //server.Log($"LobbyServerManager.OnNetworkReceiveUnconnected({remoteEndPoint}, {messageType})");
        try
        {
            packetProcessor.ReadAllPackets(reader, remoteEndPoint);
        }
        catch (ParseException e)
        {
            server.LogWarning($"LobbyServerManager.OnNetworkReceiveUnconnected() Failed to parse packet: {e.Message}");
        }
    }

    private void OnUnconnectedDiscoveryPacket(UnconnectedDiscoveryPacket packet, IPEndPoint endPoint)
    {
        //server.LogDebug(()=>$"OnUnconnectedDiscoveryPacket({packet.PacketType}, {endPoint.Address},{endPoint.Port})");

        if (!packet.IsResponse)
        {
            packet.IsResponse = true;
            packet.Data = server.serverData;
        }

        SendUnconnectedPacket(packet, endPoint.Address.ToString(), endPoint.Port);
    }
    #endregion
}
