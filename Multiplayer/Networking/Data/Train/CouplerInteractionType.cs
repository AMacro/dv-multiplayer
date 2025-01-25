using System;

namespace Multiplayer.Networking.Data.Train;

[Flags]
public enum CouplerInteractionType : ushort
{
    NoAction = 0,
    Start = 1,

    CouplerCouple = 2,
    CouplerPark = 4,
    CouplerDrop = 8,
    CouplerTighten = 16,
    CouplerLoosen = 32,

    HoseConnect = 64,
    HoseDisconnect = 128,

    CockOpen = 256,
    CockClose = 512,

    CoupleViaUI = 1024,
    UncoupleViaUI = 2048,

    CoupleViaRemote = 4096,
    UncoupleViaRemote = 8192,
}
