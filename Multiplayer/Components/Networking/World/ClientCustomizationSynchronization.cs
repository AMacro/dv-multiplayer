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
    private readonly List<GadgetPlacementData> pendingCustomizationSnapshotPlacements = [];
    private readonly List<ICustomizationActionPacket> pendingCustomizationSnapshotRelationships = [];
    private readonly Dictionary<string, List<CustomizationHoleData>> pendingCustomizationSnapshotHoleStates = [];
    private readonly List<ICustomizationActionPacket> pendingCustomizationActions = [];
    private readonly List<ICustomizationActionPacket> unacknowledgedLocalActions = [];

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
        pendingCustomizationSnapshotPlacements.Clear();
        pendingCustomizationSnapshotRelationships.Clear();
        pendingCustomizationSnapshotHoleStates.Clear();
        pendingCustomizationActions.Clear();
        unacknowledgedLocalActions.Clear();
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
        // Ordinary actions are not echoed to their sender. Retain them until a
        // host snapshot reports that it processed their origin id. This also
        // protects edits made after a recovery snapshot was captured but before
        // that older baseline reaches the client. Replacement actions are
        // server-echoed after their authoritative replacement item id is assigned.
        if (packet is not ReplaceSpoolPacket and not ReplaceDuctTapePacket)
            unacknowledgedLocalActions.Add(packet.Copy());

    }

    internal void ReceiveSnapshot(ClientboundCustomizationStatePacket packet)
    {
        if (packet?.State == null)
        {
            client.LogWarning("Received an invalid customization state packet");
            return;
        }

        pendingCustomizationState = packet.State;
        // Reliable server-to-client ordering guarantees that actions already in
        // this queue were sent before this snapshot was captured. The snapshot
        // supersedes them; actions received while it is being applied arrive
        // afterwards and remain queued.
        pendingCustomizationActions.Clear();
        // Pause live mutation application immediately. Local actions newer than
        // this baseline are replayed after it is applied.
        customizationSnapshotApplied = false;
        if (customizerStateLoaded && !applyingCustomizationSnapshot)
            CoroutineManager.Instance.StartCoroutine(ApplyPendingCustomizationStates());
    }

    internal void ReceiveAction<T>(T packet)
        where T : CustomizationActionPacket, INetSerializable, new()
    {
        if (packet == null)
            return;

        // LiteNetLib reuses the INetSerializable instance registered for this
        // subscription. Keep a value copy because the action can remain queued
        // while later packets are deserialized into that same instance.
        pendingCustomizationActions.Add(packet.Copy());
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
                pendingCustomizationState = null;
                pendingCustomizationSnapshotPlacements.Clear();
                pendingCustomizationSnapshotRelationships.Clear();
                pendingCustomizationSnapshotHoleStates.Clear();
                yield return CustomizationStateManager.ApplyCurrentStateWhenReady(state,
                    pendingCustomizationSnapshotPlacements, pendingCustomizationSnapshotRelationships,
                    pendingCustomizationSnapshotHoleStates);

                var capturedLocalActions = state.ProcessedOriginActionIds.ToHashSet();
                var actionsNewerThanSnapshot = unacknowledgedLocalActions
                    .Where(action => !capturedLocalActions.Contains(action.OriginActionId))
                    .ToList();
                pendingCustomizationActions.InsertRange(0, actionsNewerThanSnapshot.Select(action => action.Copy()));
                unacknowledgedLocalActions.Clear();
                unacknowledgedLocalActions.AddRange(actionsNewerThanSnapshot);
                customizationSnapshotApplied = true;
                client.Log($"Customization state loaded ({state.Gadgets.Count} gadgets, {state.Holes.Count} holes)");
            }
        }
        finally
        {
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
