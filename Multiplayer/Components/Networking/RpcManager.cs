using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Networking.Data.RPCs;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Multiplayer.Components.Networking;

public class RpcManager : SingletonBehaviour<RpcManager>
{
    private uint nextTicketId = 1;
    private readonly Dictionary<uint, RpcTicket> activeTickets = [];
    private readonly Dictionary<Type, uint> responseTypeToHash = [];
    private readonly Dictionary<uint, Type> hashToResponseType = [];

    protected override void Awake()
    {
        base.Awake();
        DiscoverResponseTypes();
    }

    public RpcTicket CreateTicket(float timeOut)
    {
        uint ticketId = nextTicketId++;
        var ticket = new RpcTicket(ticketId, timeOut);
        activeTickets[ticketId] = ticket;
        return ticket;
    }

    public void ResolveTicket(uint ticketId, IRpcResponse response)
    {
        Multiplayer.LogDebug(() => $"Resolving ticket {ticketId} with response {response.GetType().Name}");
        if (activeTickets.TryGetValue(ticketId, out var ticket))
        {
            Multiplayer.LogDebug(() => $"Found active ticket {ticketId}, resolving...");
            ticket.Resolve(response);
            activeTickets.Remove(ticketId);
        }
        else
        {
            Multiplayer.LogWarning($"Attempted to resolve non-existent or expired ticket with ID {ticketId}");
        }
    }

    protected void Update()
    {
        if (activeTickets.Count == 0)
            return;

        List<uint> expiredTickets = [];

        foreach (var kvp in activeTickets)
        {
            var ticket = kvp.Value;
            ticket.CheckExpiry();

            if (ticket.IsExpired)
                expiredTickets.Add(kvp.Key);
        }

        foreach (var ticketId in expiredTickets)
        {
            Multiplayer.LogDebug(() => $"Removing expired ticket {ticketId}");
            activeTickets.Remove(ticketId);
        }
    }

    public uint GetResponseTypeHash(IRpcResponse response)
    {
        var type = response.GetType();

        if (!responseTypeToHash.TryGetValue(type, out var hash))
        {
            RegisterResponseType(type);
            hash = responseTypeToHash[type];
        }

        return hash;
    }

    public IRpcResponse CreateResponseInstance(uint responseHash)
    {
        if (!hashToResponseType.TryGetValue(responseHash, out var responseType))
        {
            // Get all IRpcResponses, add to hash, then try again
            DiscoverResponseTypes();

            if (!hashToResponseType.TryGetValue(responseHash, out responseType))
            {
                Multiplayer.LogError($"Received RPC response with unknown type hash {responseHash:X8}");
                return null;
            }
        }

        Multiplayer.LogDebug(() => $"RpcManager.CreateResponseInstance({responseHash:X8}) Found response type {responseType.Name}, creating instance...");
        return (IRpcResponse)Activator.CreateInstance(responseType);
    }

    private void DiscoverResponseTypes()
    {
        // Get all assemblies in the current domain
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            try
            {
                // Find all types that implement IRpcResponse and are not abstract/interfaces with a parameterless constructor
                var responseTypes = assembly.GetTypes()
                    .Where
                    (
                        type =>
                            typeof(IRpcResponse).IsAssignableFrom(type) &&
                            !type.IsInterface &&
                            !type.IsAbstract &&
                            type.GetConstructor(Type.EmptyTypes) != null
                    )
                    .ToArray();

                foreach (var type in responseTypes)
                    RegisterResponseType(type);
            }
            catch (ReflectionTypeLoadException ex)
            {
                Multiplayer.Log($"RPC Manager could not load some types from assembly {assembly.FullName}: {ex.Message}");
            }
        }

        Multiplayer.LogDebug(() => $"RpcManager.DiscoverResponseTypes() Registered types: {string.Join(", ", hashToResponseType.Values.Select(t => t.Name))}");
    }

    private void RegisterResponseType(Type responseType)
    {
        uint hash = StringHashing.Fnv1aHash(responseType.FullName);

        if (hashToResponseType.ContainsKey(hash))
            return;

        hashToResponseType[hash] = responseType;
        responseTypeToHash[responseType] = hash;
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(RpcManager)}]";
    }
}
