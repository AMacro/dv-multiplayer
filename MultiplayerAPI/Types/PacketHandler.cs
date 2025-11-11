using MPAPI.Interfaces;

namespace MPAPI.Types;

/// <summary>
/// Delegate for handling received packets on the server
/// </summary>
/// <typeparam name="T">Packet type</typeparam>
/// <param name="packet">The received packet</param>
/// <param name="sender">The player who sent the packet</param>
public delegate void ServerPacketHandler<T>(T packet, IPlayer sender) where T : class;

/// <summary>
/// Delegate for handling received packets on the client
/// </summary>
/// <typeparam name="T">Packet type</typeparam>
/// <param name="packet">The received packet</param>
public delegate void ClientPacketHandler<T>(T packet) where T : class;
