using System;
using System.Security.Cryptography;
using System.Text;

namespace Multiplayer.Networking.Auth;

/// <summary>
///     Derives a player's stable identity from an authenticated platform account id.
/// </summary>
/// <remarks>
///     A player's identity must be a pure function of a credential they cannot choose. If the client
///     picks the value, it is an assertion rather than a proof, and any client can assert any identity.
///     <para>
///         This maps an account id to an RFC 4122 name-based (version 5) UUID. The mapping is stable, so
///         the same account yields the same <see cref="Guid"/> on every server and across reinstalls,
///         but it cannot be claimed without first proving ownership of the account.
///     </para>
/// </remarks>
internal static class PlayerIdentity
{
    /// <summary>
    ///     Namespace for the generated UUIDs. Changing this value re-keys every stored player, orphaning
    ///     their save data.
    /// </summary>
    private static readonly Guid Namespace = new("f0c4b0f4-3c5a-4a7c-9d2b-4a2f1f7f4b21");

    public static Guid FromSteamId(ulong steamId)
    {
        return NameBased($"steam:{steamId}");
    }

    private static Guid NameBased(string name)
    {
        byte[] namespaceBytes = Namespace.ToByteArray();
        SwapEndianness(namespaceBytes); // RFC 4122 hashes the namespace big-endian; Guid.ToByteArray() is little-endian.

        byte[] hash;
        using (SHA1 sha1 = SHA1.Create())
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            sha1.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
            sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
            hash = sha1.Hash;
        }

        byte[] result = new byte[16];
        Array.Copy(hash, result, 16);

        result[6] = (byte)((result[6] & 0x0F) | 0x50); // version 5
        result[8] = (byte)((result[8] & 0x3F) | 0x80); // RFC 4122 variant

        SwapEndianness(result);
        return new Guid(result);
    }

    private static void SwapEndianness(byte[] guid)
    {
        Swap(guid, 0, 3);
        Swap(guid, 1, 2);
        Swap(guid, 4, 5);
        Swap(guid, 6, 7);
    }

    private static void Swap(byte[] bytes, int left, int right)
    {
        (bytes[left], bytes[right]) = (bytes[right], bytes[left]);
    }
}
