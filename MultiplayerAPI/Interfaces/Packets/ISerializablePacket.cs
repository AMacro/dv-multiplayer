using System.IO;

namespace MPAPI.Interfaces.Packets;

/// <summary>
/// Base interface for packets using manual serialisation.
/// Implementing classes must handle their own serialisation/deserialisation.
/// </summary>
public interface ISerializablePacket
{
    /// <summary>
    /// Serialise the packet data to the provided <see cref="BinaryWriter"/>.
    /// </summary>
    /// <param name="writer"><see cref="BinaryWriter"/> to serialise data to.</param>
    void Serialize(BinaryWriter writer);

    /// <summary>
    /// Deserialise the packet data from the provided <see cref="BinaryReader"/>.
    /// </summary>
    /// <param name="reader"><see cref="BinaryReader"/> to deserialise data from.</param>
    void Deserialize(BinaryReader reader);
}
