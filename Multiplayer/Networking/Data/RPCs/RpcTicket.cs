using System;
using UnityEngine;

namespace Multiplayer.Networking.Data.RPCs;

public class RpcTicket
{
    public uint TicketId { get; }
    public bool IsResolved { get; private set; }
    public bool IsExpired { get; private set; }
    
    private Action<IRpcResponse> onResolve;
    private Action onTimeout;
    private readonly float expiryTime;

    public RpcTicket(uint ticketId, float timeOut)
    {
        TicketId = ticketId;
        expiryTime = Time.time + timeOut;
    }

    public RpcTicket OnResolve(Action<IRpcResponse> callback)
    {
        onResolve = callback;
        return this;
    }

    public RpcTicket OnTimeout(Action callback)
    {
        onTimeout = callback;
        return this;
    }

    public void Resolve(IRpcResponse response)
    {
        if (IsResolved || IsExpired) return;
        
        IsResolved = true;
        onResolve?.Invoke(response);
    }

    public void CheckExpiry()
    {
        if (IsResolved || IsExpired) return;
        
        if (Time.time >= expiryTime)
        {
            IsExpired = true;
            onTimeout?.Invoke();
        }
    }
}
