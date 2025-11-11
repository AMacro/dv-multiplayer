
namespace MPAPI.Interfaces;

/// <summary>
/// Provides methods for mapping between built-in game objects and their network identifiers in the Multiplayer system.
/// </summary>
/// <remarks>
/// This interface enables bidirectional lookup between game objects and their corresponding network IDs,
/// which are used to synchronise object references across the network. Only objects that are actively
/// synchronised by Multiplayer mod will have associated network identifiers.
/// 
/// Additional objects from the base-game will be added as Multiplayer features are implemented. If there are
/// specific object types you would like to see supported, please create an issue on the Multiplayer Mod GitHub repository.
/// </remarks>
public interface INetIdProvider
{
    /// <summary>
    /// Attempts to retrieve the network identifier for the specified object.
    /// </summary>
    /// <typeparam name="T">The type of object to get the network ID for. Must be a reference type.</typeparam>
    /// <param name="obj">The object to get the network identifier for.</param>
    /// <param name="netId">
    /// When this method returns, contains the network identifier associated with the object if found; 
    /// otherwise, the default value for the type.
    /// </param>
    /// <returns>
    /// <c>true</c> if the network identifier was successfully retrieved; otherwise, <c>false</c>.
    /// </returns>
    bool TryGetNetId<T>(T obj, out ushort netId) where T : class;

    /// <summary>
    /// Attempts to retrieve the object associated with the specified network identifier.
    /// </summary>
    /// <typeparam name="T">The type of object to retrieve. Must be a reference type.</typeparam>
    /// <param name="netId">The network identifier of the object to retrieve.</param>
    /// <param name="obj">
    /// When this method returns, contains the object associated with the network identifier if found; 
    /// otherwise, the default value for the type.
    /// </param>
    /// <returns>
    /// <c>true</c> if the object was successfully retrieved; otherwise, <c>false</c>.
    /// </returns>
    bool TryGetObject<T>(ushort netId, out T obj) where T : class;
}
