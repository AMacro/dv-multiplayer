using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Utils;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Common.Customization;
using Multiplayer.Networking.TransportLayers;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

// Orders native customization operations; NetworkServer retains packet routing.
internal sealed class HostCustomizationSynchronization
{
    private const float ActionReadyTimeoutSeconds = 10f;
    private readonly NetworkServer server;
    private readonly List<PendingHostCustomizationAction> pendingHostCustomizationActions = [];
    private bool hostCustomizationDrainRunning;
    internal HostCustomizationSynchronization(NetworkServer server) => this.server = server;
    internal void Reset()
    {
        pendingHostCustomizationActions.Clear();
        hostCustomizationDrainRunning = false;
    }
    internal void Enqueue(ICustomizationActionPacket packet, ServerPlayer player, ITransportPeer peer,
        Action<ICustomizationActionPacket, ITransportPeer, bool> relay)
    {
        pendingHostCustomizationActions.Add(new PendingHostCustomizationAction(packet, player, peer, relay));
        StartHostCustomizationDrain();
    }

    private void StartHostCustomizationDrain()
    {
        if (hostCustomizationDrainRunning || pendingHostCustomizationActions.Count == 0)
            return;

        CoroutineManager.Instance.StartCoroutine(DrainPendingHostCustomizationActions());
    }

    private IEnumerator DrainPendingHostCustomizationActions()
    {
        hostCustomizationDrainRunning = true;
        try
        {
            while (server.IsRunning && pendingHostCustomizationActions.Count > 0)
            {
                bool appliedAny = false;
                for (int index = 0; index < pendingHostCustomizationActions.Count; index++)
                {
                    var pending = pendingHostCustomizationActions[index];
                    if (!server.TryGetServerPlayer(pending.Peer, out var connectedPlayer) ||
                        connectedPlayer != pending.Player)
                    {
                        pendingHostCustomizationActions.RemoveAt(index--);
                        continue;
                    }

                    bool blockedByEarlierDependency = pendingHostCustomizationActions.Take(index)
                        .Any(earlier => CustomizationStateManager.ActionsConflict(earlier.Packet, pending.Packet));
                    if (blockedByEarlierDependency)
                    {
                        pending.UnreadySince = -1f;
                        continue;
                    }

                    if (!CustomizationStateManager.IsActionReady(pending.Packet, out _))
                    {
                        if (pending.UnreadySince < 0f)
                            pending.UnreadySince = Time.realtimeSinceStartup;
                        else if (Time.realtimeSinceStartup - pending.UnreadySince >= ActionReadyTimeoutSeconds)
                        {
                            pendingHostCustomizationActions.RemoveAt(index--);
                            if (pending.Packet.OriginActionId != 0)
                                pending.Player.ProcessedCustomizationActionIds.Add(pending.Packet.OriginActionId);
                            server.LogWarning($"Discarding customization action {pending.Packet.GetType().Name} from " +
                                $"{pending.Player.Username} after it remained unready for {ActionReadyTimeoutSeconds:F0}s");
                            server.ResynchronizeCustomization(pending.Player, pending.Peer);
                            appliedAny = true;
                        }
                        continue;
                    }

                    pendingHostCustomizationActions.RemoveAt(index--);
                    bool applied = false;
                    try
                    {
                        applied = CustomizationStateManager.ProcessActionAsHost(pending.Packet, pending.Player);
                    }
                    catch (Exception exception)
                    {
                        server.LogError($"Failed to apply customization action {pending.Packet.GetType().Name} from " +
                            $"{pending.Player.Username}: {exception}");
                    }

                    if (pending.Packet.OriginActionId != 0)
                        pending.Player.ProcessedCustomizationActionIds.Add(pending.Packet.OriginActionId);

                    if (applied)
                    {
                        bool echoSender = pending.Packet is ReplaceSpoolPacket or ReplaceDuctTapePacket;
                        pending.Relay(pending.Packet, pending.Peer, echoSender);
                    }
                    appliedAny = true;
                }

                if (!appliedAny)
                    yield return null;
            }
        }
        finally
        {
            hostCustomizationDrainRunning = false;
        }
    }

    private sealed class PendingHostCustomizationAction
    {
        public readonly ICustomizationActionPacket Packet;
        public readonly ServerPlayer Player;
        public readonly ITransportPeer Peer;
        public float UnreadySince = -1f;

        public readonly Action<ICustomizationActionPacket, ITransportPeer, bool> Relay;

        public PendingHostCustomizationAction(ICustomizationActionPacket packet, ServerPlayer player,
            ITransportPeer peer, Action<ICustomizationActionPacket, ITransportPeer, bool> relay)
        {
            Packet = packet;
            Player = player;
            Peer = peer;
            Relay = relay;
        }
    }

}
