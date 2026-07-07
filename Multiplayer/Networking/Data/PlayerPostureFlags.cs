using System;

namespace Multiplayer.Networking.Data
{
    [Flags]
    public enum PlayerPostureFlags : byte
    {
        None = 0,
        Crouch = 1,
        Sit = 2,
        Swim = 4,
        Jump = 8,
    }
}
