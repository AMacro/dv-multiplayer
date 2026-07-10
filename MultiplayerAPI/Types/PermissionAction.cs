namespace MPAPI.Types;

/// <summary>
/// Identifies a server-authoritative action a player is attempting, so registered mods can
/// veto it before the base mod grants it. See <see cref="MPAPI.Interfaces.IServer.RegisterPermissionCheck"/>.
/// </summary>
public enum PermissionAction
{
    /// <summary>
    /// The player is requesting control authority over a cab control (throttle, brake, reverser, etc.)
    /// on the target car.
    /// </summary>
    TrainControlAuthority,

    /// <summary>
    /// The player is requesting to rerail the target car.
    /// </summary>
    Rerail,

    /// <summary>
    /// The player is requesting to restore (summon/repair) the target locomotive.
    /// </summary>
    Restore,
}
