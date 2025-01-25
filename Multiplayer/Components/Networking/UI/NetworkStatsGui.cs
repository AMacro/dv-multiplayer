using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Humanizer;
using LiteNetLib;
using UnityEngine;

namespace Multiplayer.Components.Networking.UI;

public class NetworkStatsGui : MonoBehaviour
{
    private bool showStats;
    private NetStatistics clientStats;
    private NetStatistics serverStats;

    private long bytesReceivedPerSecond;
    private long bytesSentPerSecond;
    private long packetsReceivedPerSecond;
    private long packetsSentPerSecond;
    private Dictionary<byte, ushort> packetsWrittenByType;
    private Dictionary<byte, int> bytesWrittenByType;

    private Coroutine updateCoro;

    public void Show(NetStatistics clientStats, NetStatistics serverStats)
    {
        this.clientStats = clientStats;
        clientStats.Reset();
        this.serverStats = serverStats;
        serverStats?.Reset();
        updateCoro = StartCoroutine(UpdateStats());
        showStats = true;
    }

    public void Hide()
    {
        showStats = false;
        if (updateCoro != null)
            StopCoroutine(updateCoro);
    }

    private IEnumerator UpdateStats()
    {
        while (true)
        {
            bytesReceivedPerSecond = serverStats != null ? serverStats.BytesReceived - clientStats.BytesSent : clientStats.BytesReceived;
            bytesSentPerSecond = serverStats != null ? serverStats.BytesSent - clientStats.BytesReceived : clientStats.BytesReceived;
            packetsReceivedPerSecond = serverStats != null ? serverStats.PacketsReceived - clientStats.PacketsSent : clientStats.PacketsReceived;
            packetsSentPerSecond = serverStats != null ? serverStats.PacketsSent - clientStats.PacketsReceived : clientStats.PacketsReceived;
            packetsWrittenByType = serverStats?.PacketsWrittenByType;
            bytesWrittenByType = serverStats?.BytesWrittenByType;
            serverStats?.Reset();
            clientStats?.Reset();
            yield return new WaitForSecondsRealtime(1);
        }
    }

    private void OnGUI()
    {
        if (!showStats)
            return;

        GUILayout.Window(157031519, new Rect(10, 25, 255, 0), DrawStats, "Network Statistics");
    }

    // Write clean IMGUI code challenge (impossible)
    private void DrawStats(int windowId)
    {
        int statsListSize = Multiplayer.Settings.StatsListSize;

        GUILayout.Label($"Send: {bytesSentPerSecond.Bytes().ToFullWords()}/s ({packetsSentPerSecond:N0} packets/s)");
        GUILayout.Label($"Receive: {bytesReceivedPerSecond.Bytes().ToFullWords()}/s ({packetsReceivedPerSecond:N0} packets/s)");

        if (serverStats == null)
            return;

        GUILayout.Space(5);
        GUILayout.Label($"Top {statsListSize} sent packets");
        foreach (KeyValuePair<byte, ushort> kvp in packetsWrittenByType.OrderByDescending(k => k.Value).Take(statsListSize))
            GUILayout.Label($"  • {kvp.Key}: {kvp.Value}/s");
        if (packetsWrittenByType.Count < statsListSize)
            for (int i = 0; i < statsListSize - packetsWrittenByType.Count; i++)
                GUILayout.Label(string.Empty);

        GUILayout.Label($"Top {statsListSize} sent packets by size");
        foreach (KeyValuePair<byte, int> kvp in bytesWrittenByType.OrderByDescending(k => k.Value).Take(statsListSize))
            GUILayout.Label($"  • {kvp.Key}: {kvp.Value.Bytes().ToFullWords()}/s");
    }
}
