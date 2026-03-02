using DV.LocoRestoration;

namespace Multiplayer.Networking.Data.Train;

public enum RestorationType : byte
{
    None = 0,
    Single = 1,
    Double = 2
}

public struct RestorationData
{
    public ushort NetId;
    public LocoRestorationController.RestorationState RestorationState;
    public ushort SecondCarNetId;
}
