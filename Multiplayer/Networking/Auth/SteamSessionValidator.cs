using System;
using System.Collections.Generic;
using System.Linq;
using DV.Platform.Steam;
using Multiplayer.Networking.TransportLayers;
using Steamworks;
using UnityEngine;

namespace Multiplayer.Networking.Auth;

/// <summary>
///     Server-side verification of platform authentication tickets.
/// </summary>
/// <remarks>
///     Verification is a round trip to Steam, so it cannot complete inside the connection-request handler.
///     <see cref="BeginAuthSession" /> only reports that the ticket is well-formed; the authoritative
///     answer arrives later on <see cref="SteamUser.OnValidateAuthTicketResponse" />. Callers must
///     therefore accept the peer and withhold every privilege until <c>onSuccess</c> fires.
///     <para>
///         Every started session must be ended, on failure, timeout, or disconnect, or Steam keeps the
///         session open and later tickets for that account are rejected as already in use.
///     </para>
/// </remarks>
internal sealed class SteamSessionValidator : IDisposable
{
    /// <summary>How long an account may sit unverified before we give up on Steam answering.</summary>
    private const float TIMEOUT_SECONDS = 15f;

    private sealed class PendingSession
    {
        public ITransportPeer Peer;
        public float ExpiresAt;
        public Action OnSuccess;
        public Action<string> OnFailure;
    }

    private readonly Dictionary<ulong, PendingSession> pending = new();

    public int PendingCount => pending.Count;

    public SteamSessionValidator()
    {
        SteamUser.OnValidateAuthTicketResponse += OnValidateAuthTicketResponse;
    }

    public void Dispose()
    {
        SteamUser.OnValidateAuthTicketResponse -= OnValidateAuthTicketResponse;

        foreach (ulong steamId in pending.Keys.ToList())
            End(steamId);

        pending.Clear();
    }

    /// <summary>
    ///     Starts verification of <paramref name="ticket" />. Returns false, with the reason logged, if
    ///     Steam rejects the ticket outright; in that case neither callback will fire.
    /// </summary>
    public bool TryBegin(ITransportPeer peer, ulong steamId, byte[] ticket, Action onSuccess, Action<string> onFailure)
    {
        if (!DVSteamworks.Success || !SteamClient.IsValid)
        {
            Multiplayer.LogError("Cannot verify a player: Steam is unavailable on the host");
            return false;
        }

        if (steamId == 0 || ticket == null || ticket.Length == 0)
            return false;

        if (pending.ContainsKey(steamId))
        {
            // A second connection attempt while the first is still being verified.
            Multiplayer.LogWarning($"Already verifying SteamID {steamId}; rejecting the duplicate attempt");
            return false;
        }

        BeginAuthResult result = SteamUser.BeginAuthSession(ticket, steamId);
        if (result != BeginAuthResult.OK)
        {
            Multiplayer.LogWarning($"Steam rejected the ticket for SteamID {steamId}: {result}");
            return false;
        }

        pending[steamId] = new PendingSession
        {
            Peer = peer,
            ExpiresAt = Time.realtimeSinceStartup + TIMEOUT_SECONDS,
            OnSuccess = onSuccess,
            OnFailure = onFailure
        };

        return true;
    }

    /// <summary>
    ///     Ends the session of a peer that dropped before it was verified. Such a peer never became a
    ///     player, so it can only be found by the peer itself.
    /// </summary>
    public void EndByPeer(ITransportPeer peer)
    {
        foreach (KeyValuePair<ulong, PendingSession> entry in pending.Where(e => e.Value.Peer == peer).ToList())
            End(entry.Key);
    }

    /// <summary>Ends a verified session. Safe to call for an account we never started one for.</summary>
    public void End(ulong steamId)
    {
        if (steamId == 0 || !DVSteamworks.Success || !SteamClient.IsValid)
            return;

        pending.Remove(steamId);
        SteamUser.EndAuthSession(steamId);
    }

    /// <summary>Pumps Steam callbacks and expires sessions Steam never answered for.</summary>
    public void Tick()
    {
        if (pending.Count == 0)
            return;

        try
        {
            SteamClient.RunCallbacks();
        }
        catch (Exception e)
        {
            Multiplayer.LogDebug(() => $"Steam callback pump threw: {e.Message}");
        }

        float now = Time.realtimeSinceStartup;

        // Snapshot: Fail() mutates the dictionary.
        foreach (KeyValuePair<ulong, PendingSession> entry in pending.Where(e => e.Value.ExpiresAt <= now).ToList())
            Fail(entry.Key, "Steam did not answer in time");
    }

    private void OnValidateAuthTicketResponse(SteamId steamId, SteamId ownerSteamId, AuthResponse response)
    {
        if (!pending.TryGetValue(steamId.Value, out PendingSession session))
            return;

        if (response != AuthResponse.OK)
        {
            Fail(steamId.Value, response.ToString());
            return;
        }

        // The account that owns the licence differs from the player under Steam Family Sharing. The
        // player is still authenticated as themselves, which is all identity cares about.
        if (ownerSteamId.Value != steamId.Value)
            Multiplayer.Log($"SteamID {steamId.Value} is playing a copy owned by {ownerSteamId.Value}");

        pending.Remove(steamId.Value);
        session.OnSuccess();
    }

    private void Fail(ulong steamId, string reason)
    {
        if (!pending.TryGetValue(steamId, out PendingSession session))
            return;

        pending.Remove(steamId);
        SteamUser.EndAuthSession(steamId);

        Multiplayer.LogWarning($"Verification failed for SteamID {steamId}: {reason}");
        session.OnFailure(reason);
    }
}
