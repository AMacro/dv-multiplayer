using DV;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

public class NetworkedMapMarkersController : MonoBehaviour
{
    private MapMarkersController markersController;
    private GameObject textPrefab;
    private readonly Dictionary<byte, WorldMapIndicatorRefs> playerIndicators = [];

    private void Awake()
    {
        markersController = GetComponent<MapMarkersController>();
        textPrefab = markersController.GetComponentInChildren<TMP_Text>().gameObject;
        foreach (NetworkedPlayer networkedPlayer in NetworkLifecycle.Instance.Client.ClientPlayerManager.Players)
            OnPlayerConnected(networkedPlayer.Id, networkedPlayer);
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerConnected += OnPlayerConnected;
        NetworkLifecycle.Instance.Client.ClientPlayerManager.OnPlayerDisconnected += OnPlayerDisconnected;
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
    }

    private void OnPlayerConnected(byte id, NetworkedPlayer player)
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
        refs.text = textGo.GetComponent<RectTransform>();
        TMP_Text text = textGo.GetComponent<TMP_Text>();

        text.name = "Player Name";
        text.text = player.Username;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize /= 1.25f;
        text.fontSizeMin = text.fontSize / 2.0f;
        text.fontSizeMax = text.fontSize;
        text.enableAutoSizing = true;

        playerIndicators[id] = refs;
    }

    private void OnPlayerDisconnected(byte id, NetworkedPlayer player)
    {
        if (!playerIndicators.TryGetValue(id, out WorldMapIndicatorRefs refs))
            return;
        Destroy(refs.gameObject);
        playerIndicators.Remove(id);
    }

    private void OnTick(uint obj)
    {
        if (markersController == null || UnloadWatcher.isUnloading)
            return;
        UpdatePlayers();
    }

    public void UpdatePlayers()
    {
        Multiplayer.LogDebug(() => $"NetworkedWorldMap.UpdatePlayers() playerIndicators: {playerIndicators != null}, count: {playerIndicators?.Count}");
        foreach (KeyValuePair<byte, WorldMapIndicatorRefs> kvp in playerIndicators)
        {
            Multiplayer.LogDebug(() => $"NetworkedWorldMap.UpdatePlayers() key: {kvp.Key}, value is null: {kvp.Value == null}");
            if (!NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(kvp.Key, out NetworkedPlayer networkedPlayer))
            {
                Multiplayer.LogWarning($"Player indicator for {kvp.Key} exists but {nameof(NetworkedPlayer)} does not!");
                OnPlayerDisconnected(kvp.Key, null);
                continue;
            }

            if(kvp.Value == null)
            {
                Multiplayer.LogWarning($"NetworkedWorldMap.UpdatePlayers() key: {kvp.Key}, value is null skipping");
                continue;
            }

            WorldMapIndicatorRefs refs = kvp.Value;

            bool active = Globals.G.gameParams.PlayerMarkerDisplayed;
            if (refs.gameObject.activeSelf != active)
                refs.gameObject.SetActive(active);
            if (!active)
                return;

            Multiplayer.LogDebug(() => $"NetworkedWorldMap.UpdatePlayers() key: {kvp.Key}, Is active");

            Transform playerTransform = networkedPlayer.transform;

            Vector3 normalized = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            if (normalized != Vector3.zero)
                refs.indicator.localRotation = Quaternion.LookRotation(normalized);

            Vector3 position = markersController.GetMapPosition(playerTransform.position - WorldMover.currentMove, true);
            refs.indicator.localPosition = position;
            refs.text.localPosition = position with { y = position.y + 0.025f };
        }
    }
}
