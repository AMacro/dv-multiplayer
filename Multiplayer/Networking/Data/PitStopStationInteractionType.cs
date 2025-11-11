using System;

namespace Multiplayer.Networking.Data;

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
