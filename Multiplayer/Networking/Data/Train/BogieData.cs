using LiteNetLib.Utils;
using Multiplayer.Utils;
using System;

namespace Multiplayer.Networking.Data.Train;

[Flags]
public enum BogieFlags : byte
{
    None = 0,
    IncludesTrackData = 1,
    HasDerailed = 2
}
public readonly struct BogieData
{
    private readonly BogieFlags DataFlags;
    public readonly double PositionAlongTrack;
    public readonly ushort TrackNetId;
    public readonly int TrackDirection;

    public bool IncludesTrackData => DataFlags.HasFlag(BogieFlags.IncludesTrackData);
    public bool HasDerailed => DataFlags.HasFlag(BogieFlags.HasDerailed);

    private BogieData(BogieFlags flags, double positionAlongTrack, ushort trackNetId, int trackDirection)
    {
        // Prevent invalid state combinations
        if (flags.HasFlag(BogieFlags.HasDerailed))
            flags &= ~BogieFlags.IncludesTrackData; // Clear track data flag if derailed

        DataFlags = flags;
        PositionAlongTrack = positionAlongTrack;
        TrackNetId = trackNetId;
        TrackDirection = trackDirection;
    }

    public static BogieData FromBogie(Bogie bogie, bool includeTrack)
    {
        bool includesTrackData = includeTrack && !bogie.HasDerailed && bogie.track;

        BogieFlags flags = BogieFlags.None;
        if (includesTrackData) flags |= BogieFlags.IncludesTrackData;
        if (bogie.HasDerailed) flags |= BogieFlags.HasDerailed;

        return new BogieData(
            flags,
            bogie.traveller?.Span ?? -1.0,
            includesTrackData ? bogie.track.Networked().NetId : (ushort)0,
            bogie.trackDirection
        );
    }

    public static void Serialize(NetDataWriter writer, BogieData data)
    {
        writer.Put((byte)data.DataFlags);
        if (!data.HasDerailed) writer.Put(data.PositionAlongTrack);
        if (!data.IncludesTrackData) return;
        writer.Put(data.TrackNetId);
        writer.Put(data.TrackDirection);
    }

    public static BogieData Deserialize(NetDataReader reader)
    {
        BogieFlags flags = (BogieFlags)reader.GetByte();

        // Read position if not derailed
        double positionAlongTrack = !flags.HasFlag(BogieFlags.HasDerailed)
            ? reader.GetDouble()
            : -1.0;

        // Read track data if included
        ushort trackNetId = 0;
        int trackDirection = 0;
        if (flags.HasFlag(BogieFlags.IncludesTrackData))
        {
            trackNetId = reader.GetUShort();
            trackDirection = reader.GetInt();
        }

        return new BogieData(flags, positionAlongTrack, trackNetId, trackDirection);
    }
}
