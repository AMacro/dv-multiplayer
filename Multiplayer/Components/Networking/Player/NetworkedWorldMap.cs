using DV;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

public class NetworkedMapMarkersController : MonoBehaviour
{
    private MapMarkersController markersController;
    private GameObject textPrefab;
    private readonly Dictionary<NetworkedPlayer, WorldMapIndicatorRefs> playerIndicators = [];

    private void Awake()
    {
        markersController = GetComponent<MapMarkersController>();
        textPrefab = markersController.GetComponentInChildren<TMP_Text>().gameObject;
        foreach (NetworkedPlayer networkedPlayer in NetworkLifecycle.Instance.Client.ClientPlayerManager.Players)
            OnPlayerConnected(networkedPlayer);
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerConnected += OnPlayerConnected;
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerDisconnected += OnPlayerDisconnected;
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerPrefsUpdated += OnPlayerPrefsUpdated;

        NetworkLifecycle.Instance.OnTick += OnTick;
    }

    private void OnDestroy()
    {
        if (UnloadWatcher.isQuitting)
            return;
        NetworkLifecycle.Instance.OnTick -= OnTick;
        if (UnloadWatcher.isUnloading)
            return;
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerConnected -= OnPlayerConnected;
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerDisconnected -= OnPlayerDisconnected;
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerPrefsUpdated -= OnPlayerPrefsUpdated;
    }

    private void OnPlayerConnected(NetworkedPlayer player)
    {
        Transform root = new GameObject($"MapMarkerPlayer({player.Username})") {
            transform = {
                parent = this.transform,
                localPosition = Vector3.zero,
                localEulerAngles = Vector3.zero
            }
        }.transform;
        WorldMapIndicatorRefs refs = root.gameObject.AddComponent<WorldMapIndicatorRefs>();

        GameObject indicator = Instantiate(markersController.playerMarkerPrefab.gameObject, root);
        indicator.transform.localPosition = Vector3.zero;
        refs.indicator = indicator.transform;

        GameObject textGo = Instantiate(textPrefab, root);
        textGo.transform.localPosition = new Vector3(0, 0.001f, 0);
        textGo.transform.localEulerAngles = new Vector3(90f, 0, 0);
        refs.textRect = textGo.GetComponent<RectTransform>();
        refs.text = textGo.GetComponent<TMP_Text>();

        refs.text.name = "Player Name";
        refs.text.text = player.DisplayName;
        refs.text.alignment = TextAlignmentOptions.Center;
        refs.text.fontSize /= 1.25f;
        refs.text.fontSizeMin = refs.text.fontSize / 2.0f;
        refs.text.fontSizeMax = refs.text.fontSize;
        refs.text.enableAutoSizing = true;

        playerIndicators[player] = refs;
    }

    private void OnPlayerPrefsUpdated(NetworkedPlayer player)
    {
        Multiplayer.LogDebug(() => $"NetworkedMapMarkersController.OnPlayerPrefsUpdated() player: {player}, playerIndicators contains player: {playerIndicators.ContainsKey(player)}");

        if (!playerIndicators.TryGetValue(player, out WorldMapIndicatorRefs refs))
            return;

        if (refs.text == null)
            return;

        refs.text.SetText(player.DisplayName);
    }

    private void OnPlayerDisconnected(NetworkedPlayer player)
    {
        if (!playerIndicators.TryGetValue(player, out WorldMapIndicatorRefs refs))
            return;
        Destroy(refs.gameObject);
        playerIndicators.Remove(player);
    }

    private void OnTick(uint obj)
    {
        if (markersController == null || UnloadWatcher.isUnloading)
            return;
        UpdatePlayers();
    }

    public void UpdatePlayers()
    {
        if (playerIndicators == null)
        {
            Multiplayer.LogDebug(() => $"NetworkedWorldMap.UpdatePlayers() playerIndicators: {playerIndicators != null}, count: {playerIndicators?.Count}");
            return;
        }

        foreach (KeyValuePair<NetworkedPlayer, WorldMapIndicatorRefs> kvp in playerIndicators)
        {
            if(kvp.Value == null)
                Multiplayer.LogDebug(() => $"NetworkedWorldMap.UpdatePlayers() key: {kvp.Key}, value is null: {kvp.Value == null}");

            if (!NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(kvp.Key.PlayerId, out NetworkedPlayer networkedPlayer))
            {
                Multiplayer.LogWarning($"Player indicator for {kvp.Key} exists but {nameof(NetworkedPlayer)} does not!");
                OnPlayerDisconnected(kvp.Key);
                continue;
            }

            if(kvp.Value == null)
            {
                Multiplayer.LogWarning($"NetworkedWorldMap.UpdatePlayers() key: {kvp.Key}, value is null skipping");
                continue;
            }

            WorldMapIndicatorRefs refs = kvp.Value;

            bool active = Globals.G.GameParams.PlayerMarkerDisplayed;
            if (refs.gameObject.activeSelf != active)
                refs.gameObject.SetActive(active);
            if (!active)
            {
                //Multiplayer.LogDebug(() => $"NetworkedWorldMap.UpdatePlayers() key: {kvp.Key}, is NOT active");
                return;
            }

            Transform playerTransform = networkedPlayer.transform;

            Vector3 normalized = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            if (normalized != Vector3.zero)
                refs.indicator.localRotation = Quaternion.LookRotation(normalized);

            Vector3 position = markersController.GetMapPosition(playerTransform.position - WorldMover.currentMove, true);
            refs.indicator.localPosition = position;
            refs.textRect.localPosition = position with { y = position.y + 0.025f };
        }
    }
}
