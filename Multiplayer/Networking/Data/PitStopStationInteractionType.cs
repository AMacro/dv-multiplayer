using System;

namespace Multiplayer.Networking.Data;

[Flags]
public enum PitStopStationInteractionType : byte
{
    Reject,
    Grab,
    Ungrab,
    StateUpdate,
    SelectCar,
    PayOrder,
    CancelOrder,
    ProcessOrder,
}
