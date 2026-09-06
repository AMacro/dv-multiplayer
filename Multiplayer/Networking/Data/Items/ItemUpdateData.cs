using LiteNetLib.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data.Items;

public class ItemUpdateData
{
    private const int MaxTrackedValues = 4096;

    [Flags]
    public enum ItemUpdateType : byte
    {
        None = 0,
        Create = 1,
        Destroy = 2,
        ItemState = 4,
        ItemPosition = 8,
        ObjectState = 16,
        Ownership = 32,
        FullSync = ItemState | ItemPosition | ObjectState,
    }

    public ItemUpdateType UpdateType { get; set; }
    public ushort ItemNetId { get; set; }
    public byte OwnerPlayerId { get; set; }
    public bool BelongsToPlayer { get; set; }
    public bool RestockOnDestroy { get; set; }
    public string PrefabName { get; set; }
    public ItemState ItemState { get; set; }
    public bool Active { get; set; }
    public Vector3 ItemPosition { get; set; }
    public Quaternion ItemRotation { get; set; }
    public Vector3 ThrowDirection { get; set; }
    public byte Player { get; set; }
    public int InventorySlotIndex { get; set; } = -1;
    public int ContainerSlotIndex { get; set; } = -1;
    public string ContainerId { get; set; }
    // Identity of this item's container, distinct from the container holding it.
    public string ContainerIdentity { get; set; }
    public bool InLockedSlot { get; set; }
    public bool IsDropped { get; set; }
    public ushort CarNetId { get; set; }
    public bool AttachedFront  { get; set; }
    public Dictionary<string, object> States { get; set; }
    public uint SentTick { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)UpdateType);
        writer.Put(ItemNetId);

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        // Stamp newly-created snapshots when they first go on the wire, but retain
        // the sender's capture tick when the host relays a client update. Replacing
        // that tick at the host would pair old phase values with a newer timestamp.
        if (SentTick == 0)
            SentTick = NetworkLifecycle.Instance.SynchronizedTick;
        writer.Put(SentTick);

        if (UpdateType.HasFlag(ItemUpdateType.Create))
        {
            writer.Put(OwnerPlayerId);
            writer.Put(BelongsToPlayer);
            writer.Put(RestockOnDestroy);
            writer.Put(PrefabName);
            writer.Put(ContainerIdentity ?? string.Empty);
        }
        else if (UpdateType.HasFlag(ItemUpdateType.Ownership))
        {
            writer.Put(OwnerPlayerId);
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ItemState))
        {
            writer.Put((byte)ItemState);
            if (ItemState == ItemState.Dropped || ItemState == ItemState.Thrown) // || UpdateType.HasFlag(ItemUpdateType.ItemPosition)
            {
                Vector3Serializer.Serialize(writer, ItemPosition);
                QuaternionSerializer.Serialize(writer, ItemRotation);

                if (ItemState == ItemState.Thrown)
                    Vector3Serializer.Serialize(writer, ThrowDirection);
            }
            else if (IsCarriedState(ItemState))
            {
                writer.Put(Player);
                writer.Put(InventorySlotIndex);
                writer.Put(InLockedSlot);
                writer.Put(IsDropped);
                if (ItemState == ItemState.InContainer)
                {
                    writer.Put(ContainerId ?? string.Empty);
                    writer.Put(ContainerSlotIndex);
                }
            }
            else if (ItemState == ItemState.Attached)
            {
                writer.Put(CarNetId);
                writer.Put(AttachedFront);
            }
            else if (ItemState == ItemState.InLostAndFound)
            {
                writer.Put(Active);
                Vector3Serializer.Serialize(writer, ItemPosition);
                QuaternionSerializer.Serialize(writer, ItemRotation);
            }
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            int stateCount = States?.Count ?? 0;
            if (stateCount > MaxTrackedValues)
                throw new InvalidOperationException($"Too many tracked item values: {stateCount}");

            writer.Put(stateCount);
            if (States != null)
            {
                foreach (var state in States)
                {
                    writer.Put(state.Key);
                    TrackedValueSerializer.Serialize(writer, state.Value);
                }
            }
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        UpdateType = (ItemUpdateType)reader.GetByte();
        ItemNetId = reader.GetUShort();

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        SentTick = reader.GetUInt();

        if (UpdateType.HasFlag(ItemUpdateType.Create))
        {
            OwnerPlayerId = reader.GetByte();
            BelongsToPlayer = reader.GetBool();
            RestockOnDestroy = reader.GetBool();
            PrefabName = reader.GetString();
            ContainerIdentity = reader.GetString();
        }
        else if (UpdateType.HasFlag(ItemUpdateType.Ownership))
        {
            OwnerPlayerId = reader.GetByte();
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ItemState))
        {
            ItemState = (ItemState)reader.GetByte();
            if (ItemState == ItemState.Dropped || ItemState == ItemState.Thrown) // || UpdateType.HasFlag(ItemUpdateType.ItemPosition)
            {
                ItemPosition = Vector3Serializer.Deserialize(reader);
                ItemRotation = QuaternionSerializer.Deserialize(reader);

                if (ItemState == ItemState.Thrown)
                {
                    Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown before: {ThrowDirection}");
                    ThrowDirection = Vector3Serializer.Deserialize(reader);
                    Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown after: {ThrowDirection}");
                }
            }
            else if (IsCarriedState(ItemState))
            {
                Player = reader.GetByte();
                InventorySlotIndex = reader.GetInt();
                InLockedSlot = reader.GetBool();
                IsDropped = reader.GetBool();
                if (ItemState == ItemState.InContainer)
                {
                    ContainerId = reader.GetString();
                    ContainerSlotIndex = reader.GetInt();
                }
            }
            else if (ItemState == ItemState.Attached)
            {
                CarNetId = reader.GetUShort();
                AttachedFront = reader.GetBool();
            }
            else if (ItemState == ItemState.InLostAndFound)
            {
                Active = reader.GetBool();
                ItemPosition = Vector3Serializer.Deserialize(reader);
                ItemRotation = QuaternionSerializer.Deserialize(reader);
            }
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            int stateCount = reader.GetInt();
            if (stateCount < 0 || stateCount > MaxTrackedValues)
                throw new InvalidOperationException($"Invalid tracked item value count: {stateCount}");

            if (stateCount > 0)
            {
                States = new Dictionary<string, object>();
                for (int i = 0; i < stateCount; i++)
                {
                    string key = reader.GetString();
                    object value = TrackedValueSerializer.Deserialize(reader);
                    States[key] = value;
                }
            }
        }
    }

    private static bool IsCarriedState(ItemState state) =>
        state is ItemState.InHand or ItemState.InInventory or ItemState.InContainer;

}
