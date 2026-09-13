using LiteNetLib.Utils;

namespace Multiplayer.Networking;

// Fixed connection preamble, independent of login packet serialization.
internal static class ConnectionProtocol
{
    private const ulong Magic = 0x44564D50434F4E4E;
    internal const ushort Version = 1; // Increment for incompatible packet layout changes.

    internal static NetDataWriter Wrap(NetDataWriter login)
    {
        var writer = new NetDataWriter();
        writer.Put(Magic);
        writer.Put(Version);
        writer.Put(login.Data, 0, login.Length);
        return writer;
    }

    internal static bool TryRead(NetDataReader reader) =>
        reader.AvailableBytes >= sizeof(ulong) + sizeof(ushort) &&
        reader.GetULong() == Magic && reader.GetUShort() == Version;
}
