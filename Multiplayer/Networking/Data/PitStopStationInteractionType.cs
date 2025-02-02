using System;

namespace Multiplayer.Networking.Data;

public enum PitStopStationInteractionType : byte
{
    Reject,         //bit 0
    Grab,           //bit 0
    Ungrab,         //bit 1
    StateUpdate,    //bit 2
    SelectCar,      //bit 3
    PayOrder,      //bit 4
    CancelOrder,   //bit 5
    ProcessOrder
}
