using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Utils;
using LiteNetLib.Utils;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Packets.Clientbound;
using Multiplayer.Networking.Packets.Common.Customization;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

// Owns baseline/live-action replay while NetworkClient keeps transport and load-state sequencing.
internal sealed class ClientCustomizationSynchronization
{
    private readonly NetworkClient client;
    internal ClientCustomizationSynchronization(NetworkClient client) => this.client = client;
    private bool customizerStateLoaded;
    private bool customizationSnapshotApplied;
    private bool applyingCustomizationSnapshot;
    private bool customizationActionDrainRunning;
    private CustomizationStateData pendingCustomizationState;
    private CustomizationStateData applyingCustomizationState;
    private readonly List<GadgetPlacementData> pendingCustomizationSnapshotPlacements = [];
    private readonly List<ICustomizationActionPacket> pendingCustomizationSnapshotRelationships = [];
    private readonly Dictionary<string, List<CustomizationHoleData>> pendingCustomizationSnapshotHoleStates = [];
    private readonly SnapshotActionQueue<ICustomizationActionPacket> actionQueue = new(
        action => action.OriginActionId,
        action => action is ReplaceSpoolPacket or ReplaceDuctTapePacket or LoadSpoolPacket or DropEmptySpoolPacket,
        action => action.Copy());
    private List<ICustomizationActionPacket> pendingCustomizationActions => actionQueue.Pending;
    private readonly Dictionary<uint, NetworkedItem> localReplacements = [];

    internal void Reset(bool forLoading = false)
    {
        customizerStateLoaded = false;
        customizationSnapshotApplied = false;
        if (forLoading)
        {
            applyingCustomizationSnapshot = false;
            customizationActionDrainRunning = false;
        }
        pendingCustomizationState = null;
        applyingCustomizationState = null;
        pendingCustomizationSnapshotPlacements.Clear();
        pendingCustomizationSnapshotRelationships.Clear();
        pendingCustomizationSnapshotHoleStates.Clear();
        actionQueue.Reset();
        localReplacements.Clear();
        CustomizationStateManager.ClearPendingLocalState();
    }

    internal IEnumerator LoadInitialState(float timeout)
    {
        // The host may need to stream customized train-car interiors before taking
        // the complete world snapshot. Do not mistake that preparation for a lost
        // packet on low-latency connections where timeout is very small.
        float customizationDeadline = Time.time + Mathf.Max(timeout, 5f);
        while (!customizerStateLoaded)
        {
            if (pendingCustomizationState == null)
            {
                if (Time.time >= customizationDeadline)
                {
                    client.LogWarning("Timed out waiting for customization state; continuing while retaining the late-snapshot path");
                    customizationSnapshotApplied = true;
                    customizerStateLoaded = true;
                    StartCustomizationActionDrain();
                    break;
                }
                yield return null;
                continue;
            }

            yield return ApplyPendingCustomizationStates();
            customizerStateLoaded = true;
        }

    }

    internal void RecordLocalAction(ICustomizationActionPacket packet)
    {
        // Retain local edits until acknowledged by a host batch or snapshot.
        // Edits needed by an older in-progress snapshot survive until replay
        // completes. Replacement echoes separately bind authoritative item ids.
        actionQueue.RecordLocal(packet);
        // Preserve the native magazine while this edit is unacknowledged or
        // needed by an older snapshot. Replaying a replacement/drop can destroy
        // a newer reel, and the baseline can reference a reel destroyed locally.
        ushort toolId = GetSpoolToolId(packet);
        if (toolId != 0)
            PreserveLocalMagazine(toolId);
        NetworkedItem replacement = CustomizationStateManager.GetLocalReplacement(packet);
        if (replacement != null)
            localReplacements[packet.OriginActionId] = replacement;

    }

    internal void ReceiveSnapshot(ClientboundCustomizationStatePacket packet)
    {
        if (packet?.State == null)
        {
            client.LogWarning("Received an invalid customization state packet");
            return;
        }

        pendingCustomizationState = packet.State;
        actionQueue.ReceiveSnapshot(packet.State.ProcessedOriginActionIds);
        foreach (ushort toolId in actionQueue.LocalActions.Select(GetSpoolToolId).Where(id => id != 0).Distinct())
            PreserveLocalMagazine(toolId);
        // Pause live mutation application immediately. Local actions newer than
        // this baseline are replayed after it is applied.
        customizationSnapshotApplied = false;
        // Item packets are normally drained on a tick, whereas this snapshot
        // starts a coroutine immediately. Flush preceding reliable Creates now:
        // a refresh must not replace an existing reel after we have loaded it
        // into the tool from this baseline.
        NetworkedItemManager.Instance.ProcessReceived();
        if (customizerStateLoaded && !applyingCustomizationSnapshot)
            CoroutineManager.Instance.StartCoroutine(ApplyPendingCustomizationStates());
    }

    internal void ReceiveAcknowledgment(ClientboundCustomizationAcknowledgmentPacket packet) =>
        actionQueue.Acknowledge(packet.ActionIds,
            applyingCustomizationSnapshot || pendingCustomizationState != null);

    private static ushort GetSpoolToolId(ICustomizationActionPacket action) => action switch
    {
        ReplaceSpoolPacket packet => packet.ToolItemNetId,
        LoadSpoolPacket packet => packet.ToolItemNetId,
        DropEmptySpoolPacket packet => packet.ToolItemNetId,
        _ => 0,
    };

    private void PreserveLocalMagazine(ushort toolId)
    {
        pendingCustomizationState?.MagazineStates.RemoveAll(packet => packet.ToolItemNetId == toolId);
        applyingCustomizationState?.MagazineStates.RemoveAll(packet => packet.ToolItemNetId == toolId);
        pendingCustomizationSnapshotRelationships.RemoveAll(action =>
            action is LoadSpoolPacket packet && packet.ToolItemNetId == toolId);
    }

    internal void ReceiveAction<T>(T packet)
        where T : CustomizationActionPacket, INetSerializable, new()
    {
        if (packet == null)
            return;

        byte owner = packet switch
        {
            ReplaceSpoolPacket spool => spool.OwnerPlayerId,
            ReplaceDuctTapePacket tape => tape.OwnerPlayerId,
            _ => 0,
        };
        if (owner == client.PlayerId && localReplacements.TryGetValue(packet.OriginActionId, out var replacement) &&
            CustomizationStateManager.BindLocalReplacement(packet, replacement))
        {
            localReplacements.Remove(packet.OriginActionId);
            return;
        }

        // LiteNetLib reuses the INetSerializable instance registered for this
        // subscription. Keep a value copy because the action can remain queued
        // while later packets are deserialized into that same instance.
        actionQueue.Receive(packet);
        StartCustomizationActionDrain();
    }

    private IEnumerator ApplyPendingCustomizationStates()
    {
        if (applyingCustomizationSnapshot)
            yield break;

        applyingCustomizationSnapshot = true;
        try
        {
            while (pendingCustomizationState != null)
            {
                CustomizationStateData state = pendingCustomizationState;
                applyingCustomizationState = state;
                int generation = actionQueue.Generation;
                pendingCustomizationState = null;
                pendingCustomizationSnapshotPlacements.Clear();
                pendingCustomizationSnapshotRelationships.Clear();
                pendingCustomizationSnapshotHoleStates.Clear();
                yield return CustomizationStateManager.ApplyCurrentStateWhenReady(state,
                    pendingCustomizationSnapshotPlacements, pendingCustomizationSnapshotRelationships,
                    pendingCustomizationSnapshotHoleStates,
                    () => generation != actionQueue.Generation);

                if (!actionQueue.CompleteSnapshot(generation))
                    continue;
                customizationSnapshotApplied = true;
                client.Log($"Customization state loaded ({state.Gadgets.Count} gadgets, {state.Holes.Count} holes)");
            }
        }
        finally
        {
            applyingCustomizationState = null;
            applyingCustomizationSnapshot = false;
        }

        StartCustomizationActionDrain();
    }

    private void StartCustomizationActionDrain()
    {
        if (customizationActionDrainRunning ||
            pendingCustomizationSnapshotPlacements.Count == 0 &&
            pendingCustomizationSnapshotRelationships.Count == 0 &&
            pendingCustomizationSnapshotHoleStates.Count == 0 &&
            pendingCustomizationActions.Count == 0)
            return;

        CoroutineManager.Instance.StartCoroutine(DrainPendingCustomizationActions());
    }

    private IEnumerator DrainPendingCustomizationActions()
    {
        customizationActionDrainRunning = true;
        try
        {
            while (client.IsRunning && (pendingCustomizationSnapshotPlacements.Count > 0 ||
                pendingCustomizationSnapshotRelationships.Count > 0 ||
                pendingCustomizationSnapshotHoleStates.Count > 0 || pendingCustomizationActions.Count > 0))
            {
                if (!customizationSnapshotApplied || applyingCustomizationSnapshot)
                {
                    yield return null;
                    continue;
                }

                bool appliedAny = false;
                for (int index = 0; index < pendingCustomizationSnapshotPlacements.Count; index++)
                {
                    var placement = pendingCustomizationSnapshotPlacements[index];
                    if (!CustomizationStateManager.IsSnapshotPlacementReady(placement, out _))
                        continue;

                    pendingCustomizationSnapshotPlacements.RemoveAt(index--);
                    // The initial object-state pass already applied or queued these
                    // values. If the item did not exist, its reliable Create carries
                    // a newer full state. Reapplying this older baseline here could
                    // roll back changes received while the target was unavailable.
                    CustomizationStateManager.ApplySnapshotPlacement(placement, applyTrackedValues: false);
                    appliedAny = true;
                }

                foreach (string targetKey in pendingCustomizationSnapshotHoleStates.Keys.ToArray())
                {
                    if (!CustomizationStateManager.ApplySnapshotHoleState(targetKey,
                        pendingCustomizationSnapshotHoleStates[targetKey]))
                        continue;

                    pendingCustomizationSnapshotHoleStates.Remove(targetKey);
                    appliedAny = true;
                }

                for (int index = 0; index < pendingCustomizationSnapshotRelationships.Count; index++)
                {
                    var action = pendingCustomizationSnapshotRelationships[index];
                    bool blockedByEarlierDependency = pendingCustomizationSnapshotRelationships.Take(index)
                        .Any(earlier => CustomizationStateManager.ActionsConflict(earlier, action));
                    bool blockedByPlacement = pendingCustomizationSnapshotPlacements
                        .Any(placement => CustomizationStateManager.SnapshotPlacementConflicts(placement, action));
                    bool blockedByHoleState = pendingCustomizationSnapshotHoleStates.Keys
                        .Any(targetKey => CustomizationStateManager.SnapshotHoleStateConflicts(targetKey, action));
                    if (blockedByEarlierDependency || blockedByPlacement || blockedByHoleState ||
                        !CustomizationStateManager.IsActionReady(action, out _))
                        continue;

                    pendingCustomizationSnapshotRelationships.RemoveAt(index--);
                    try
                    {
                        CustomizationStateManager.ApplyAction(action);
                    }
                    catch (Exception exception)
                    {
                        client.LogError($"Failed to apply deferred snapshot relationship {action.GetType().Name}: {exception}");
                    }
                    appliedAny = true;
                }

                for (int index = 0; index < pendingCustomizationActions.Count; index++)
                {
                    var action = pendingCustomizationActions[index];
                    bool blockedByEarlierDependency = pendingCustomizationActions.Take(index)
                        .Any(earlier => CustomizationStateManager.ActionsConflict(earlier, action));
                    bool blockedBySnapshotPlacement = pendingCustomizationSnapshotPlacements
                        .Any(placement => CustomizationStateManager.SnapshotPlacementConflicts(placement, action));
                    bool blockedBySnapshotRelationship = pendingCustomizationSnapshotRelationships
                        .Any(relationship => CustomizationStateManager.ActionsConflict(relationship, action));
                    bool blockedBySnapshotHoleState = pendingCustomizationSnapshotHoleStates.Keys
                        .Any(targetKey => CustomizationStateManager.SnapshotHoleStateConflicts(targetKey, action));
                    if (blockedByEarlierDependency || blockedBySnapshotPlacement ||
                        blockedBySnapshotRelationship || blockedBySnapshotHoleState ||
                        !CustomizationStateManager.IsActionReady(action, out _))
                        continue;

                    pendingCustomizationActions.RemoveAt(index--);
                    try
                    {
                        CustomizationStateManager.ApplyAction(action);
                    }
                    catch (Exception exception)
                    {
                        client.LogError($"Failed to apply deferred customization action {action.GetType().Name}: {exception}");
                    }
                    appliedAny = true;
                }

                if (!appliedAny)
                    yield return null;
            }
        }
        finally
        {
            customizationActionDrainRunning = false;
        }
    }

}
