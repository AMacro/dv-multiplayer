using LiteNetLib.Utils;
using Multiplayer.Utils;

namespace Multiplayer.Networking.Data.Train;

public readonly struct CouplingData
{
    public readonly bool IsCoupled;
    public readonly ChainCouplerInteraction.State State;
    public readonly ushort ConnectionNetId;
    public readonly bool ConnectionToFront;
    public readonly bool HoseConnected;
    public readonly bool PreventAutoCouple;
    public readonly bool CockOpen;

    public CouplingData(bool isCoupled, bool hoseConnected, ChainCouplerInteraction.State state,
        ushort connectionNetId, bool connectionToFront, bool preventAutoCouple, bool cockOpen)
    {
        IsCoupled = isCoupled;
        State = state;
        ConnectionNetId = connectionNetId;
        ConnectionToFront = connectionToFront;
        HoseConnected = hoseConnected;
        PreventAutoCouple = preventAutoCouple;
        CockOpen = cockOpen;
    }

    public static void Serialize(NetDataWriter writer, CouplingData data)
    {
        writer.Put(data.IsCoupled);
        writer.Put(data.HoseConnected);
        writer.Put((byte)data.State);

        if (data.IsCoupled || data.HoseConnected)
        {
            writer.Put(data.ConnectionNetId);
            writer.Put(data.ConnectionToFront);
        }

        writer.Put(data.PreventAutoCouple);
        writer.Put(data.CockOpen);
    }

    public static CouplingData Deserialize(NetDataReader reader)
    {
        bool isCoupled = reader.GetBool();
        bool hoseConnected = reader.GetBool();
        var state = (ChainCouplerInteraction.State)reader.GetByte();

        ushort connectionNetId = 0;
        bool connectionToFront = false;

        if (isCoupled || hoseConnected)
        {
            connectionNetId = reader.GetUShort();
            connectionToFront = reader.GetBool();
        }

        bool preventAutoCouple = reader.GetBool();
        bool cockOpen = reader.GetBool();

        return new CouplingData(isCoupled, hoseConnected, state, connectionNetId,
            connectionToFront, preventAutoCouple, cockOpen);
    }
    public static CouplingData From(Coupler coupler)
    {
        return new CouplingData(
            isCoupled: coupler.IsCoupled(),
            hoseConnected: coupler.hoseAndCock.IsHoseConnected,
            state: coupler.state,
            connectionNetId: coupler.IsCoupled() ? coupler.coupledTo.train.GetNetId() : (ushort)0,
            connectionToFront: coupler.IsCoupled() && coupler.coupledTo.isFrontCoupler,
            preventAutoCouple: coupler.preventAutoCouple,
            cockOpen: coupler.IsCockOpen
        );
    }
}
