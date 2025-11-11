using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using System;
using System.Collections.Generic;
using static UnityModManagerNet.UnityModManager;

namespace MPAPI.Interfaces;

/// <summary>
/// Interface for interacting with Multiplayer mod client instances.
/// </summary>
public interface IClient
{
    /// <summary>
    /// Event fired when a player connects.
    /// </summary>
    /// <remarks>
    /// The event handler receives an <see cref="IPlayer"/> object for the connected player.
    /// </remarks>
    event Action<IPlayer> OnPlayerConnected;

    /// <summary>
    /// Event fired when a player disconnects, but before the <see cref="IPlayer"/> object is destroyed.
    /// </summary>
    /// <remarks>
    /// The event handler receives an <see cref="IPlayer"/> object for the disconnected player.
    /// </remarks>
    event Action<IPlayer> OnPlayerDisconnected;

    /// <summary>
    /// Registers a block to prevent the client from sending the 'Ready' signal to the server until all mods have called 'CancelReadyBlock'.
    /// </summary>
    /// <param name="modInfo">Mod information.</param>
    /// <remarks>
    /// Only required if the mod needs complete loading prior to receiving game state from the server.
    /// </remarks>
    void RegisterReadyBlock(ModInfo modInfo);

    /// <summary>
    /// Cancels a previously registered ready block.
    /// </summary>
    /// <param name="modInfo">Mod information.</param>
    /// <remarks>
    /// All registered blocks must be cancelled prior to the client sending the 'Ready' signal to the server.
    /// </remarks>
    void CancelReadyBlock(ModInfo modInfo);

    /// <summary>
    /// Gets Player Id of the local player.
    /// </summary>
    /// <remarks>
    /// The local player does not have an <see cref="IPlayer"/> object.
    /// </remarks>
    byte PlayerId { get; }

    /// <summary>
    /// Gets <see cref="IPlayer"/> objects for all players connected to the server.
    /// </summary>
    /// <returns>Read-only collection of <see cref="IPlayer"/> objects.</returns>
    IReadOnlyCollection<IPlayer> Players { get; }

    /// <summary>
    /// Gets number of players currently connected to the server.
    /// </summary>
    /// <returns>Positive integer representing the number of connected players.</returns>
    int PlayerCount { get; }

    /// <summary>
    /// Gets the <see cref="IPlayer"/> for player by Id.
    /// </summary>
    /// <returns><see cref="IPlayer"/> object if found, otherwise <c>null</c>.</returns>
    IPlayer GetPlayer(byte playerId);

    /// <summary>
    /// Gets connection state for the client.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets ping for the client.
    /// </summary>
    int Ping { get; }

    #region Packet API
    /// <summary>
    /// Register a packet type that uses automatic serialisation.
    /// </summary>
    /// <typeparam name="T">Packet type implementing <see cref="IPacket"/>.</typeparam>
    /// <param name="handler">Handler to call when packet is received.</param>
    void RegisterPacket<T>(ClientPacketHandler<T> handler) where T : class, IPacket, new();

    /// <summary>
    /// Register a packet type that uses manual serialisation.
    /// </summary>
    /// <typeparam name="T">Packet type implementing <see cref="ISerializablePacket"/>.</typeparam>
    /// <param name="handler">Handler to call when packet is received.</param>
    void RegisterSerializablePacket<T>(ClientPacketHandler<T> handler) where T : class, ISerializablePacket, new();


    /// <summary>
    /// Send a packet based on <see cref="IPacket"/> to the server.
    /// </summary>
    /// <typeparam name="T">Packet type.</typeparam>
    /// <param name="packet">Packet to send.</param>
    /// <param name="reliable">Whether to send reliably.</param>
    void SendPacketToServer<T>(T packet, bool reliable = true) where T : class, IPacket, new();

    /// <summary>
    /// Send a packet based on <see cref="ISerializablePacket"/> to the server.
    /// </summary>
    /// <typeparam name="T">Packet type.</typeparam>
    /// <param name="packet">Packet to send.</param>
    /// <param name="reliable">Whether to send reliably.</param>
    void SendSerializablePacketToServer<T>(T packet, bool reliable = true) where T : class, ISerializablePacket, new();

    #endregion
}
