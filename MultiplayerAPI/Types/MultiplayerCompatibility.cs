
namespace MPAPI.Types;

/// <summary>
/// Defines how a mod works with multiplayer functionality.
/// </summary>
public enum MultiplayerCompatibility : byte
{

    /// <summary>
    /// Mod has not defined compatibility.
    ///     If the host is using this mod all clients must also have it.
    ///     If a client is using this mod and the host is not, the client will be unable to join the game.
    /// </summary>
    Undefined,

    /// <summary>
    /// Mod is incompatible with multiplayer.
    ///     The mod must be disabled if Multiplayer Mod is enabled.
    /// </summary>
    Incompatible,

    /// <summary>
    /// Mod must be installed on the host and all clients.
    ///     Players without this mod will be unable to join the game.
    ///     Mods are responsible for disabling behaviour when connecting to a host without the mod.
    /// </summary>
    All,

    /// <summary>
    /// Mod must be installed on the host.
    ///     Mods are responsible for disabling their behaviour if the player is not the host.
    /// </summary>
    Host,

    /// <summary>
    /// Mod has no effect on the game play and can be ignored.
    ///     This should be used for client-only mods e.g. GUI enhancements, controller mods, RUE, etc.
    /// </summary>
    Client,
}
