using MPAPI;
using MPAPI.Interfaces;
using MultiplayerAPITest.Enums;
using MultiplayerAPITest.Packets;
using System;
using System.Text;
using UnityEngine;

namespace MultiplayerAPITest.TestComponents;

internal class ClientTest : MonoBehaviour
{
    const string LogPrefix = "ClientTest";
    const int DELAY_INTERVAL = 10; // 10 seconds

    IClient client;

    uint lastLogTick;

    protected void Awake()
    {
        client = MultiplayerAPI.Client;

        // Subscribe to game tick events
        MultiplayerAPI.Instance.OnTick += OnTick;

        // Subscribe to player events
        client.OnPlayerConnected += OnPlayerConnected;
        client.OnPlayerDisconnected += OnPlayerDisconnected;

        // Subscribe to packets
        Subscribe();

        // Check if we are also a host - some mods may need to do nothing on a client-only game
        // e.g. Clients should not generate jobs
        if (MultiplayerAPI.Instance.IsHost)
        {
            if (MultiplayerAPI.Instance.IsDedicatedServer)
            {
                //Dedicated servers have not been implemented yet, IsDedicatedServer will always return false
                Log("We are a dedicated server");
            }
            else
            {
                var gameType = MultiplayerAPI.Instance.IsSinglePlayer ? "single player" : "multiplayer";
                Log($"We are in a {gameType} self-hosted game");
            }
        }
        else
        {
            Log("We are only a client game");
        }
    }

    protected void Start() { }

    protected void Update() { }

    protected void OnDestroy()
    {
        // Unsubscribe from game tick events
        MultiplayerAPI.Instance.OnTick -= OnTick;

        // Unsubscribe from player events
        client.OnPlayerConnected -= OnPlayerConnected;
        client.OnPlayerDisconnected -= OnPlayerDisconnected;
    }

    private void Subscribe()
    {
        // Subscribe to network packets
        // Note: only packets that will be received by client need to be registered here
        client.RegisterPacket<SimplePacket>(OnTestSimpleModPacket);
        client.RegisterPacket<SimplePacketWithNetId>(OnSimplePacketWithNetId);
        client.RegisterSerializablePacket<ComplexModPacket>(OnTestComplexModPacket);
    }

    #region Example Tick Event
    private void OnTick(uint tick)
    {
        // This event is called every tick
        // This code is purely for testing purposes, not a recommened use case; normally it would be used for synchronising
        // and batching changes or to track how long since an update has been received for a specific object.

        // The TICK_RATE is fixed at both client and server; currently the rate is 24 ticks/second
        if ((tick - lastLogTick) > MultiplayerAPI.Instance.TICK_RATE * DELAY_INTERVAL)
        {
            //DELAY_INTERVAL (10 seconds) passed.
            //log my ping
            Log($"My current ping is {client.Ping} ms");

            //Log the ping for all players
            if (client.PlayerCount > 1)
            {
                StringBuilder sb = new($"Tick {tick}.\r\nThere are {client.PlayerCount} players, their pings are:");
                foreach (IPlayer player in client.Players)
                    sb.AppendLine($"\"{player?.PlayerId}\" {player.Ping} ms");

                Log(sb.ToString());
            }

            lastLogTick = tick;
        }
    }
    #endregion

    #region Player Events
    private void OnPlayerConnected(IPlayer player)
    {
        // This event is called when another player connects

        Log($"Player \"{player?.PlayerId}\" has connected.");
    }

    private void OnPlayerDisconnected(IPlayer player)
    {
        // This event is called when another player disconnects

        Log($"Player \"{player?.PlayerId}\" has connected.");
    }
    #endregion

    #region Packet Callbacks
    //method called when a `TestSimplePacket` packet is received
    private void OnTestSimpleModPacket(SimplePacket packet)
    {
        // We will just log this, but in a real use case you would validate the packet data, look up the referenced object and apply any updates required for your mod.
        Log($"Received {packet.GetType()}, CarId: {packet.CarId}, Position: {packet.Position}, WheelArraangement: {packet.WheelArrangement}");

        // For the purposes of testing and example, we will send the data back to the server
        SendSimplePacket(packet.CarId, packet.Position, packet.WheelArrangement);
    }

    //method called when a `TestSimplePacket` packet is received
    private void OnSimplePacketWithNetId(SimplePacketWithNetId packet)
    {
        // Let's locate the car

        if (packet.CarNetId == 0)
        {
            LogWarning("Received SimplePacketWithNetId with a CarNetId of 0!");
            return;
        }

        if (!MultiplayerAPI.Instance.TryGetObjectFromNetId(packet.CarNetId, out TrainCar car))
        {
            LogWarning($"Received SimplePacketWithNetId with a CarNetId of {packet.CarNetId}, but TrainCar was not found!");
            return;
        }

        Log($"Received {packet.GetType()}, CarNetId: {packet.CarNetId}, CarId: {car.ID}, Car Livery: {car.carLivery}, Position: {packet.Position}, Wheel Arrangement: {packet.WheelArrangement}");
    }

    //method called when a `TestComplexModPacket` packet is received
    private void OnTestComplexModPacket(ComplexModPacket packet)
    {
        StringBuilder sb = new($"Received {packet.GetType()}\r\nPacket Data");

        foreach (var kvp in packet.CarToPositionMap)
            sb.AppendLine($"\tCarId: {kvp.Key}, Position: {kvp.Value}");

        Log(sb.ToString());
    }
    #endregion

    #region Packet Senders
    public void SendSimplePacket(string carId, Vector3 position, WheelArrangement arrangement)
    {
        SimplePacket packet = new()
        {
            CarId = carId,
            Position = position,
            WheelArrangement = arrangement
        };

        //send the packet reliably
        client.SendPacketToServer(packet, true);
    }
    #endregion

    #region Logging

    public void LogDebug(Func<object> resolver)
    {
        MultiplayerAPITest.LogDebug(() => $"{LogPrefix} {resolver?.Invoke()}");
    }

    public void Log(object msg)
    {
        MultiplayerAPITest.Log($"{LogPrefix} {msg}");
    }

    public void LogWarning(object msg)
    {
        MultiplayerAPITest.LogWarning($"{LogPrefix} {msg}");
    }

    public void LogError(object msg)
    {
        MultiplayerAPITest.LogError($"{LogPrefix} {msg}");
    }

    #endregion
}
