using DV.Platform.Steam;
using Steamworks;

namespace Multiplayer.Networking.Auth;

/// <summary>
///     Holds the local player's platform authentication ticket for the lifetime of a connection.
/// </summary>
/// <remarks>
///     The ticket is minted by Steam for this account and this application. It is not secret — it travels
///     over the wire in the login packet — but it cannot be produced for an account the caller does not
///     own, and the server verifies it against Steam's backend rather than taking the client's word.
///     <para>
///         Tickets must be cancelled when the connection ends, otherwise the session leaks and subsequent
///         tickets can be rejected as already in use.
///     </para>
/// </remarks>
internal static class ClientAuthTicket
{
    private static AuthTicket ticket;

    public static bool IsAvailable => DVSteamworks.Success && SteamClient.IsValid;

    /// <summary>Mints a fresh ticket, replacing any ticket still held.</summary>
    public static bool TryAcquire(out byte[] data, out ulong steamId)
    {
        data = null;
        steamId = 0;

        if (!IsAvailable)
            return false;

        Release();

        ticket = SteamUser.GetAuthSessionTicket(default);
        if (ticket?.Data == null || ticket.Data.Length == 0)
        {
            Multiplayer.LogWarning("Steam returned an empty authentication ticket");
            Release();
            return false;
        }

        data = ticket.Data;
        steamId = SteamClient.SteamId.Value;
        return true;
    }

    public static void Release()
    {
        ticket?.Cancel();
        ticket = null;
    }
}
