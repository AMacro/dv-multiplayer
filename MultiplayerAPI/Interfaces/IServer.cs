using MPAPI.Interfaces.Packets;
using MPAPI.Types;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MPAPI.Interfaces;

/// <summary>
/// Represents the method that will handle chat command execution.
/// </summary>
/// <param name="message">The message content without the command prefix (e.g. '/command parameter1 parameter2' becomes 'parameter1 parameter2').</param>
/// <param name="sender">The player who executed the command.</param>
public delegate void ChatCommandCallback(string message, IPlayer sender);

/// <summary>
/// Represents the method that will handle chat message filtering.
/// </summary>
/// <param name="message">The message content that can be modified by reference.</param>
/// <param name="sender">The player who sent the message.</param>
/// <returns><c>True</c> to pass the message to the next filter or send to players; <c>false</c> to block the message from further processing.</returns>
public delegate bool ChatFilterDelegate(ref string message, IPlayer sender);

/// <summary>
/// Interface for interacting with Multiplayer mod server instances.
/// </summary>
public interface IServer
{

    /// <summary>
    /// Event fired when a player connects and is authenticated, but before the player receives game state information.
    /// </summary>
    /// <remarks>
    /// The event handler receives an <see cref="IPlayer"/> object for the connected player. This event is not triggered for the local-client player on the host.
    /// </remarks>
    event Action<IPlayer> OnPlayerConnected;

    /// <summary>
    /// Event fired when a player disconnects, but before the <see cref="IPlayer"/> object is destroyed.
    /// </summary>
    /// <remarks>
    /// The event handler receives an <see cref="IPlayer"/> object for the connected player.
    /// </remarks>
    event Action<IPlayer> OnPlayerDisconnected;

    /// <summary>
    /// Event fired when a player has signalled they are ready for game state information.
    /// </summary>
    /// <remarks>
    /// This event occurs after the server has sent the game state, it does not guarantee the player has finished generating all cars, jobs, etc.
    /// </remarks>
    event Action<IPlayer> OnPlayerReady;

    #region Server Properties
    /// <summary>
    /// Gets number of players currently connected to the server.
    /// </summary>
    /// <returns>Positive integer representing the number of connected players.</returns>
    int PlayerCount { get; }

    /// <summary>
    /// Gets <see cref="IPlayer"/> objects for all players connected to the server.
    /// </summary>
    /// <returns>Read-only collection of <see cref="IPlayer"/> objects.</returns>
    public IReadOnlyCollection<IPlayer> Players { get; }

    /// <summary>
    /// Gets <see cref="IPlayer"/> for player by Id.
    /// </summary>
    /// <param name="id">Id for the player.</param>
    /// <returns><see cref="IPlayer"/> object if found, otherwise <c>null</c>.</returns>
    IPlayer GetPlayer(byte id);

    #endregion

    #region Packet API
    /// <summary>
    /// Register a packet type that uses automatic serialisation.
    /// </summary>
    /// <typeparam name="T">Packet type implementing <see cref="IPacket"/>.</typeparam>
    /// <param name="handler">Handler to call when packet is received.</param>
    void RegisterPacket<T>(ServerPacketHandler<T> handler) where T : class, IPacket, new();

    /// <summary>
    /// Register a packet type that uses manual serialisation.
    /// </summary>
    /// <typeparam name="T">Packet type implementing <see cref="ISerializablePacket"/>.</typeparam>
    /// <param name="handler">Handler to call when packet is received.</param>
    void RegisterSerializablePacket<T>(ServerPacketHandler<T> handler) where T : class, ISerializablePacket, new();


    /// <summary>
    /// Send a packet based on <see cref="IPacket"/> to all connected players.
    /// </summary>
    /// <typeparam name="T">Packet type.</typeparam>
    /// <param name="packet">Packet to send.</param>
    /// <param name="reliable">Whether to send reliably.</param>
    /// <param name="excludeSelf">Sends the packet to the local client when <c>false</c>, skips sending to the local client when <c>true</c>.</param>
    /// <param name="excludePlayer"><see cref="IPlayer"/> To be excluded from this broadcast.</param>
    void SendPacketToAll<T>(T packet, bool reliable = true, bool excludeSelf = false, IPlayer excludePlayer = null) where T : class, IPacket, new();

    /// <summary>
    /// Send a packet based on <see cref="ISerializablePacket"/> to all connected players.
    /// </summary>
    /// <typeparam name="T">Packet type.</typeparam>
    /// <param name="packet">Packet to send.</param>
    /// <param name="reliable">Whether to send reliably.</param>
    /// <param name="excludeSelf">Sends the packet to the local client when <c>false</c>, skips sending to the local client when <c>true</c>.</param>
    /// <param name="excludePlayer"><see cref="IPlayer"/> To be excluded from this broadcast.</param>
    void SendSerializablePacketToAll<T>(T packet, bool reliable = true, bool excludeSelf = false, IPlayer excludePlayer = null) where T : class, ISerializablePacket, new();

    /// <summary>
    /// Send a packet based on <see cref="IPacket"/> to a specific player.
    /// </summary>
    /// <typeparam name="T">Packet type.</typeparam>
    /// <param name="packet">Packet to send.</param>
    /// <param name="player">Target player.</param>
    /// <param name="reliable">Whether to send reliably.</param>
    void SendPacketToPlayer<T>(T packet, IPlayer player, bool reliable = true) where T : class, IPacket, new();

    /// <summary>
    /// Send a packet based on <see cref="ISerializablePacket"/> to a specific player.
    /// </summary>
    /// <typeparam name="T">Packet type.</typeparam>
    /// <param name="packet">Packet to send.</param>
    /// <param name="player">Target player.</param>
    /// <param name="reliable">Whether to send reliably.</param>
    void SendSerializablePacketToPlayer<T>(T packet, IPlayer player, bool reliable = true) where T : class, ISerializablePacket, new();
    #endregion

    #region Server Util
    /// <summary>
    /// Returns the distance (Square Magnitude) of the closest player to a given GameObject.
    /// </summary>
    /// <param name="gameObject">GameObject to compare players against.</param>
    /// <returns>Returns the distance (Square Magnitude) of the closest player, or float.MaxValue if no player is nearby.</returns>
    float AnyPlayerSqrMag(GameObject gameObject);

    /// <summary>
    /// Returns the distance (Square Magnitude) of the closest player to a given point.
    /// </summary>
    /// <param name="anchor">Anchor point to compare players against.</param>
    /// <returns>Returns the distance (Square Magnitude) of the closest player, or float.MaxValue if no player is nearby.</returns>
    float AnyPlayerSqrMag(Vector3 anchor);
    #endregion

    #region Chat API
    /// <summary>
    /// Sends a server chat message.
    /// </summary>
    /// <param name="message">Message to be sent.</param>
    /// <param name="excludePlayer">Player to exclude. If <c>null</c>, message will go to all players.</param>
    /// <remarks>Server chat messages are messsages sent from the server, not from a specific player and will be stylised as such.</remarks>
    void SendServerChatMessage(string message, IPlayer excludePlayer = null);

    /// <summary>
    /// Sends a chat message to a specific player.
    /// </summary>
    /// <param name="message">Message to be sent.</param>
    /// <param name="player">Recipient player.</param>
    void SendWhisperChatMessage(string message, IPlayer player);

    /// <summary>
    /// Registers a chat command e.g. `/server` and optional short command '/s'.
    /// </summary>
    /// <param name="commandLong">Command to be filtered for, without a leading '/' e.g. 'server'.</param>
    /// <param name="commandShort">Optional short command to be filtered for, without a leading '/' e.g. 's'.</param>
    /// <param name="helpMessage">Optional callback for a help message e.g. <![CDATA["Send a message as the server (host only)\r\n\t\t/server <message>\r\n\t\t/s <message>"]]>. It is recommended to provide localisation/translation for this string.</param>
    /// <param name="callback">Action to execute when the command is triggered. First parameter contains message without the command e.g. '/command parameter1 parameter2' will become 'parameter1 parameter2', second parameter is the player who executed the command.</param>
    /// <returns>True if the command was successfully registered, false if registration failed (e.g. command already exists).</returns>
    bool RegisterChatCommand(string commandLong, string commandShort, Func<string> helpMessage, ChatCommandCallback callback);


    /// <summary>
    /// Registers a chat filter that processes non-command messages in registration order.
    /// Filters form a chain where each filter can either allow the message to continue to the next filter or block further processing.
    /// If all filters return <c>true</c>, the message will be sent to all players (default action).
    /// This filter also applies to whispered messages, regardless of source (player or server).
    /// </summary>
    /// <param name="callback">Filter function type `ChatFilterDelegate` that processes the message. First delegate parameter is the message content, second parameter is the player who sent the message. Return <c>true</c> to pass the message to the next filter/default action, <c>false</c> to block propagation.</param>
    void RegisterChatFilter(ChatFilterDelegate callback);

    #endregion
}
