using System;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Listeners;
using Newtonsoft.Json;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Multiplayer.Components.Networking;
using DV.WeatherSystem;

namespace Multiplayer.Networking.Managers.Server;
public class LobbyServerManager : MonoBehaviour
{
    //API endpoints
    private const string ENDPOINT_ADD_SERVER    = "add_game_server";
    private const string ENDPOINT_UPDATE_SERVER = "update_game_server";
    private const string ENDPOINT_REMOVE_SERVER = "remove_game_server";

    private const int REDIRECT_MAX = 5;

    private const int UPDATE_TIME_BUFFER = 10;                  //We don't want to miss our update, let's phone in just a little early
    private const int UPDATE_TIME = 120 - UPDATE_TIME_BUFFER;   //How often to update the lobby server - this should match the lobby server's time-out period
    private const int PLAYER_CHANGE_TIME = 5;                   //Update server early if the number of players has changed in this time frame

    private NetworkServer server;
    public string server_id { get; set; }
    public string private_key { get; set; }

    private bool sendUpdates = false;
    private float timePassed = 0f;

    private void Awake()
    {
        server = NetworkLifecycle.Instance.Server;

        Multiplayer.Log($"LobbyServerManager New({server != null})");
        Multiplayer.Log($"StartingCoroutine {Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_ADD_SERVER}");
        StartCoroutine(RegisterWithLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_ADD_SERVER}"));
    }

    private void OnDestroy()
    {
        Multiplayer.Log($"LobbyServerManager OnDestroy()");
        sendUpdates = false;
        StopAllCoroutines();
        StartCoroutine(RemoveFromLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_REMOVE_SERVER}"));
    }

    private void Update()
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
        }
    }

    public void RemoveFromLobbyServer()
    {
        Multiplayer.Log($"RemoveFromLobbyServer OnDestroy()");
        sendUpdates = false;
        StopAllCoroutines();
        StartCoroutine(RemoveFromLobbyServer($"{Multiplayer.Settings.LobbyServerAddress}/{ENDPOINT_REMOVE_SERVER}"));
    }

    private IEnumerator RegisterWithLobbyServer(string uri)
    {
        JsonSerializerSettings jsonSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
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
        JsonSerializerSettings jsonSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
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
        JsonSerializerSettings jsonSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        DateTime start = AStartGameData.BaseTimeAndDate;
        DateTime current = WeatherDriver.Instance.manager.DateTime;
        TimeSpan inGame = current - start;

        string json = JsonConvert.SerializeObject(new LobbyServerUpdateData(
                                                                                server_id,
                                                                                private_key,
                                                                                inGame.ToString("d\\d\\ hh\\h\\ mm\\m\\ ss\\s"),
                                                                                server.serverData.CurrentPlayers),
                                                                                jsonSettings
                                                                            );
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
    private IEnumerator SendWebRequest(string uri, string json, Action<UnityWebRequest> onSuccess, Action<UnityWebRequest> onError, int depth=0)
    {
        if (depth > REDIRECT_MAX)
        {
            Multiplayer.LogError($"Reached maximum redirects: {uri}");
            yield break;
        }

        using (UnityWebRequest webRequest = UnityWebRequest.Post(uri, json))
        {
            webRequest.redirectLimit = 0;

            webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)){contentType = "application/json"};
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
                    Multiplayer.LogError($"Error: {webRequest.error}\r\n{webRequest.downloadHandler.text}");
                    onError?.Invoke(webRequest);
                }
                else
                {
                    Multiplayer.Log($"Received: {webRequest.downloadHandler.text}");
                    onSuccess?.Invoke(webRequest);
                }
            }
        }
    }
}
