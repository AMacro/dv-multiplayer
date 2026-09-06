namespace Multiplayer.Networking.Data.Train;

public class LocoInUseData
{
    public enum LocoInUseReason : byte
    {
        None = 0,
        Moving = 1,
        Occupied = 2,
        Brakes = 3,
        Engine = 4,
        Timeout = 5,
        Job = 6,
        CoupledCarInUse = 7
    }
}
