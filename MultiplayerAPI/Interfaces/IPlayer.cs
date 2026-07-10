using System;
using UnityEngine;

namespace MPAPI.Interfaces
{
    /// <summary>
    /// Represents a player in the multiplayer session, providing access to player state and information.
    /// </summary>
    public interface IPlayer
    {
        /// <summary>
        /// Gets the identifier for the player within the session.
        /// </summary>
        /// <remarks>
        /// This identifier can be used as a network ID for referencing the player across the network.
        /// If the player leaves the session the Id will be reassigned to the next player to join.
        /// </remarks>
        public byte PlayerId { get; }

        /// <summary>
        /// Gets the stable, persistent unique identifier for this player.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="PlayerId"/> (which is reassigned as players join and leave), this value is
        /// stable for a given player across reconnects and sessions, making it a suitable key for
        /// persisting or looking up per-player state.
        /// <para>
        /// On a server that requires authentication (the default), this is derived from the player's
        /// platform account, after the server has verified with that platform that the player owns it.
        /// A player cannot present an identity that is not theirs, so this value is safe to key
        /// security-sensitive state on.
        /// </para>
        /// <para>
        /// A host may switch authentication off, for LAN or non-Steam play. On such a server this value
        /// is whatever the client asserted about itself and proves nothing: any client can claim any
        /// identity. Consumers that key sensitive state on it should decide whether to trust an
        /// unauthenticated server rather than assume this value is meaningful.
        /// </para>
        /// Only the server tracks player identities, so this is populated on <see cref="IServer"/>-side
        /// player objects. On a pure client, remote players are not identity-tracked and this returns
        /// <see cref="System.Guid.Empty"/>.
        /// </remarks>
        Guid UniqueId { get; }

        /// <summary>
        /// Gets a value indicating whether this player proved ownership of their <see cref="UniqueId"/>.
        /// </summary>
        /// <remarks>
        /// <c>true</c> when the server verified the player's platform account with that platform at
        /// login. <c>false</c> when the host has authentication switched off, in which case
        /// <see cref="UniqueId"/> is self-asserted and interchangeable between clients.
        /// Consumers that persist per-player state, grant privileges, or otherwise treat identity as a
        /// security boundary should refuse to act when this is <c>false</c>.
        /// Always <c>false</c> on <see cref="IClient"/>-side player objects, which do not track identity.
        /// </remarks>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Gets the username of the player.
        /// </summary>
        public string Username { get; }

        /// <summary>
        /// Gets the crew name of the player.
        /// </summary>
        public string CrewName { get; }

        /// <summary>
        /// Gets the display name of the player.
        /// </summary>
        /// <remarks>
        /// The display name is the player's username if they are not in a crew, or "[CrewName] Username" if they are in a crew.
        /// </remarks>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the current world position of the player.
        /// </summary>
        Vector3 Position { get; }

        /// <summary>
        /// Gets the current Y-axis rotation of the player.
        /// </summary>
        float RotationY { get; }

        /// <summary>
        /// Gets a value indicating whether the player has finished loading the game world.
        /// </summary>
        /// <value>
        /// <c>true</c> if the player has completed world loading and is ready to receive game state updates; otherwise, <c>false</c>.
        /// </value>
        bool IsLoaded { get; }

        /// <summary>
        /// Gets a value indicating whether this player is the host of the multiplayer session.
        /// </summary>
        /// <value><c>true</c> if the player is the session host; otherwise, <c>false</c>.</value>
        bool IsHost { get; }

        /// <summary>
        /// Gets the current network ping/latency for this player.
        /// </summary>
        /// <value>The one-way time in milliseconds between the server and this player.</value>
        int Ping { get; }

        /// <summary>
        /// Gets a value indicating whether this player is on a car.
        /// </summary>
        /// <value><c>true</c> if the player is on a car; otherwise, <c>false</c>.</value>
        bool IsOnCar { get; }

        /// <summary>
        /// Gets the train car that the player is currently occupying.
        /// </summary>
        /// <value>
        /// The <see cref="TrainCar"/> instance the player is on, or <c>null</c> if the player is not on any car.
        /// </value>
        TrainCar OccupiedCar { get; }
    }
}
