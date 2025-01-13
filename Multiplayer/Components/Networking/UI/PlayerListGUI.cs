using System.Collections.Generic;
using Multiplayer.Components.Networking.Player;
using UnityEngine;

namespace Multiplayer.Components.Networking.UI;

public class PlayerListGUI : MonoBehaviour
{
    private bool showPlayerList;
    private string localPlayerUsername;

    public void RegisterListeners()
    {
        ScreenspaceMouse.Instance.ValueChanged += OnToggle;
        localPlayerUsername = Multiplayer.Settings.GetUserName();
    }
    public void UnRegisterListeners()
    {
        ScreenspaceMouse.Instance.ValueChanged -= OnToggle;
        OnToggle(false);
    }

    private void OnToggle(bool status)
    {
        showPlayerList = status;
    }

    private void OnGUI()
    {
        if (!showPlayerList)
            return;

        GUILayout.Window(157031520, new Rect(Screen.width / 2.0f - 125, 25, 250, 0), DrawPlayerList, Locale.PLAYER_LIST__TITLE);
    }

    private void DrawPlayerList(int windowId)
    {
        foreach (string player in GetPlayerList())
            GUILayout.Label(player);
    }

    // todo: cache this?
    private IEnumerable<string> GetPlayerList()
    {
        if (!NetworkLifecycle.Instance.IsClientRunning)
            return new[] { "Not in game" };

        IReadOnlyCollection<NetworkedPlayer> players = NetworkLifecycle.Instance.Client.ClientPlayerManager.Players;
        string[] playerList = new string[players.Count + 1];
        int i = 0;
        foreach (NetworkedPlayer player in players)
        {
            playerList[i] = $"{player.Username} ({player.GetPing().ToString()}ms)";
            i++;
        }

        // The Player of the Client is not in the PlayerManager, so we need to add it separately
        playerList[playerList.Length - 1] = $"{localPlayerUsername} ({NetworkLifecycle.Instance.Client.Ping}ms)";
        return playerList;
    }
}
