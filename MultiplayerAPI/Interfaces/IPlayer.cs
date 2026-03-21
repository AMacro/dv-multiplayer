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
