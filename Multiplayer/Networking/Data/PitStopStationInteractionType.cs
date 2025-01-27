using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
