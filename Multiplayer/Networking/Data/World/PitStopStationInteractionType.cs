using System;

namespace Multiplayer.Networking.Data.World;

public enum PitStopStationInteractionType : byte
{
    Reject,
    LeverState,
    ResourceUpdate,

    CarSelectorGrab,
    CarSelectorUngrab,
    CarSelection,

    FaucetGrab,
    FaucetUngrab,
    FaucetPosition,
}
