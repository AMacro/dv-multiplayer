using LiteNetLib.Utils;
using Multiplayer.Utils;

namespace Multiplayer.Networking.Data;

public readonly struct BogieData
{
    private readonly byte PackedBools;
    public readonly double PositionAlongTrack;
    public readonly ushort TrackNetId;
    public readonly int TrackDirection;

    public bool IncludesTrackData => (PackedBools & 1) != 0;
    public bool HasDerailed => (PackedBools & 2) != 0;

    private BogieData(byte packedBools, double positionAlongTrack, ushort trackNetId, int trackDirection)
    {
        PackedBools = packedBools;
        PositionAlongTrack = positionAlongTrack;
        TrackNetId = trackNetId;
        TrackDirection = trackDirection;
    }

    public static BogieData FromBogie(Bogie bogie, bool includeTrack, int trackDirection)
    {
        bool includesTrackData = includeTrack && !bogie.HasDerailed && bogie.track;
        return new BogieData(
            (byte)((includesTrackData ? 1 : 0) | (bogie.HasDerailed ? 2 : 0)),
            bogie.traveller?.Span ?? -1.0,
            includesTrackData ? bogie.track.Networked().NetId : (ushort)0,
            trackDirection
        );
    }

    public static void Serialize(NetDataWriter writer, BogieData data)
    {
        writer.Put(data.PackedBools);
        if (!data.HasDerailed) writer.Put(data.PositionAlongTrack);
        if (!data.IncludesTrackData) return;
        writer.Put(data.TrackNetId);
        writer.Put(data.TrackDirection);
    }

    public static BogieData Deserialize(NetDataReader reader)
    {
        byte packedBools = reader.GetByte();
        bool includesTrackData = (packedBools & 1) != 0;
        bool hasDerailed = (packedBools & 2) != 0;
        double positionAlongTrack = !hasDerailed ? reader.GetDouble() : -1.0;
        ushort trackNetId = includesTrackData ? reader.GetUShort() : (ushort)0;
        int trackDirection = includesTrackData ? reader.GetInt() : 0;
        return new BogieData(packedBools, positionAlongTrack, trackNetId, trackDirection);
    }
}
