using System;

namespace Multiplayer.Networking.Data.Train;

[Flags]
public enum CouplerInteractionType : ushort
{
    NoAction = 0,

    CouplerCouple = 1,
    CouplerPark = 2,
    CouplerDrop = 4,
    CouplerTighten = 8,
    CouplerLoosen = 16,

    HoseConnect = 32,
    HoseDisconnect = 64,

    CockOpen = 128,
    CockClose = 256,

    CoupleViaUI = 512,
    UncoupleViaUI = 1024,
}
