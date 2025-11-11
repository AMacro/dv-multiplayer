using DV.Logic.Job;
using I2.Loc;
using MPAPI;
using MPAPI.Interfaces;
using MultiplayerAPITest.Enums;
using MultiplayerAPITest.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MultiplayerAPITest.TestComponents;

internal class ServerTest : MonoBehaviour
{
    const string LogPrefix = "ServerTest";
    const string MESSAGE_COLOUR_SERVER = "9CDCFE";
    const int DELAY_INTERVAL = 20; // seconds

    uint lastLogTick = 0;

    IServer server;

    protected void Awake()
    {
        server = MultiplayerAPI.Server;

        // Subscribe to game tick events
        MultiplayerAPI.Instance.OnTick += OnTick;

        // Subscribe to player events
        server.OnPlayerConnected += OnPlayerConnected;
        server.OnPlayerDisconnected += OnPlayerDisconnected;
        server.OnPlayerReady += OnPlayerReady;

        // Subscribe to packets and chat commands
        Subscribe();
    }

    protected void Start() { }

    protected void Update() { }

    protected void OnDestroy()
    {
        // Unsubscribe from game tick events
        MultiplayerAPI.Instance.OnTick -= OnTick;

        // Unsubscribe from player events
        server.OnPlayerConnected -= OnPlayerConnected;
        server.OnPlayerDisconnected -= OnPlayerDisconnected;
        server.OnPlayerReady -= OnPlayerReady;
    }

    private void Subscribe()
    {
        // Subscribe to network packets - note: only packets that will be received by server need to be registered here
        server.RegisterPacket<SimplePacket>(OnTestSimpleModPacket);
        server.RegisterPacket<SimplePacketWithNetId>(OnSimplePacketWithNetId);
        server.RegisterSerializablePacket<ComplexModPacket>(OnTestComplexModPacket);

        // Subscribe to chat commands - these have been added for API testing and examples 
        server.RegisterChatCommand("packet", "p", OnChatCommandSendPacketHelp, OnChatCommandSendPacket);            //this command allows testing of simple packet sending
        server.RegisterChatCommand("locopos", "lp", OnChatCommandSendLocoPosHelp, OnChatCommandSendLocoPos);        //this command allows testing of complex packet sending
        server.RegisterChatCommand("closest", "cd", OnChatCommandClosestPlayerHelp, OnChatCommandClosestPlayer);    //this command returns the distance of the closest player to a given TrainCar
        server.RegisterChatCommand("stats", null, OnChatCommandStatsHelp, OnChatCommandStats);                      //this command returns the number of connected players and all player names

        // Subscribe to chat filters
        server.RegisterChatFilter(OnChatMessage);
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
            //Log the ping for all players
            if (server.PlayerCount == 0)
            {
                Log($"Tick {tick}.\r\nThere are no players connected");
            }

            StringBuilder sb = new($"Tick {tick}.\r\nThere are {server.PlayerCount} players, their pings are:");
            foreach (IPlayer player in server.Players)
                sb.AppendLine($"\"{player?.PlayerId}\" {player.Ping} ms");

            Log(sb.ToString());

            lastLogTick = tick;
        }
    }
    #endregion

    #region Player Events
    private void OnPlayerConnected(IPlayer player)
    {
        // Send mod settings, parameters, etc.
        // Note: This event occurs when the player is authenticated and before the player receives game state info

        Log($"Player {player?.PlayerId} (\"{player?.Username}\") has connected. (Is Loaded: {player?.IsLoaded})");
    }

    private void OnPlayerReady(IPlayer player)
    {
        // Player has indicated the world is loaded and they are ready to receive game state info
        // Note: This event occurs after the server has sent the game state, it does not guarantee the player has finished generating all cars, jobs, etc.

        Log($"Player \"{player?.PlayerId}\" is ready. (Is Loaded: {player?.IsLoaded})");

        //Send an anouncement to all players
        server.SendServerChatMessage($"Please welcome our newest driver {player?.PlayerId}!");
    }

    private void OnPlayerDisconnected(IPlayer player)
    {
        // Player has disconnected
        // Note: This event occurs immediately prior to destroying the player object
        // Complete all cleanup prior to returning from this method

        Log($"Player \"{player?.Username}\" has disconnected");
    }
    #endregion

    #region Packet Callbacks

    // Method called when a `SimplePacket` packet is received
    private void OnTestSimpleModPacket(SimplePacket packet, IPlayer player)
    {
        Log($"Received {packet.GetType()} from player: {player.Username}, CarId: {packet.CarId}, Position: {packet.Position}, WheelArraangement: {packet.WheelArrangement}");
    }

    // Method called when a `SimplePacketWithNetId` packet is received
    private void OnSimplePacketWithNetId(SimplePacketWithNetId packet, IPlayer player)
    {
        Log($"Received {packet.GetType()} from player: {player.Username}, CarId: {packet.CarNetId}, Position: {packet.Position}, Wheel Arrangement: {packet.WheelArrangement}");
    }

    //method called when a `ComplexModPacket` packet is received
    private void OnTestComplexModPacket(ComplexModPacket packet, IPlayer player)
    {
        StringBuilder sb = new($"Received {packet.GetType()}\r\nPacket Data");

        foreach (var kvp in packet.CarToPositionMap)
            sb.AppendLine($"\tCarId: {kvp.Key}, Position: {kvp.Value}");

        Log(sb.ToString());
    }
    #endregion

    #region Packet Senders
    public void SendSimplePacketToAll(string carId, Vector3 position, WheelArrangement arrangement, IPlayer excludePlayer = null)
    {
        SimplePacket packet = new()
        {
            CarId = carId,
            Position = position,
            WheelArrangement = arrangement
        };

        // Send the packet reliably (ensure it makes it to all players), allow sending to the local client (true will block sending to a local client), exclude a player if specified
        server.SendPacketToAll(packet, true, false, excludePlayer);
    }

    public void SendSimplePacketWithNetIdToAll(ushort carId, Vector3 position, WheelArrangement arrangement, IPlayer excludePlayer = null)
    {

        SimplePacketWithNetId packet = new()
        {
            CarNetId = carId,
            Position = position,
            WheelArrangement = arrangement
        };

        //send the packet reliably (ensure it makes it to all players)
        server.SendPacketToAll(packet, true, true, excludePlayer);
    }

    public void SendComplexPacket(Dictionary<string, Vector3> carToPos, IPlayer excludePlayer = null)
    {

        ComplexModPacket packet = new()
        {
            CarToPositionMap = carToPos
        };

        // Send the packet reliably (ensure it makes it to all players), allow sending to the local client (true will block sending to a local client), exclude a player if specified
        server.SendSerializablePacketToAll(packet, true, false, excludePlayer);
    }
    #endregion

    #region Chat Command Callbacks
    // Called when a player uses the chat command '/packet' or '/p'
    private void OnChatCommandSendPacket(string message, IPlayer sender)
    {
        string[] args = message.Split(' ');
        string whisper;

        if (args.Length < 2)
        {
            LogWarning($"Received 'SendPacket' chat command from player \"{sender.Username}\", but not enough arguments were specified. Command: {message}");
            return;
        }

        if (string.IsNullOrEmpty(args[0]))
        {
            LogWarning($"Received 'SendPacket' chat command from player \"{sender.Username}\", but the first argument is empty. Command: {message}");
            whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>Not enough arguments supplied. Type /? for help.</color>";
            server.SendWhisperChatMessage(whisper, sender);
            return;
        }

        if (string.IsNullOrEmpty(args[1]))
        {
            LogWarning($"Received 'SendPacket' chat command from player \"{sender.Username}\", but the second argument is empty. Command: {message}");
        }

        LogDebug(() => $"OnChatCommandSendPacket({message}, {sender?.Username}) post-args checks");

        var tc = GetTrainCarFromID(args[1].ToUpper());

        if (tc == null)
        {
            // Send a whisper back to the player who sent the command
            whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>TrainCar '{args[1]}' not found</color>";
            server.SendWhisperChatMessage(whisper, sender);
            return;
        }

        var pos = tc.transform.position - WorldMover.currentMove;

        switch (args[0].ToLower())
        {
            case "simple": //send a simple packet
                // Send a simple packet to all players using TrainCar id as a string, TrainCar position and a random wheel arrangement
                SendSimplePacketToAll(args[1], pos, GetRandomWheelArrangement());
                whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>Sending simple packet for '{args[1]}'</color>";
                break;

            case "net": //send a simple packet using a netId

                if (MultiplayerAPI.Instance.TryGetNetId(tc, out ushort netId))
                {
                    // Send a simple packet to all players using TrainCar NetId, TrainCar position and a random wheel arrangement
                    SendSimplePacketWithNetIdToAll(netId, pos, GetRandomWheelArrangement());
                    whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>Sending net packet for '{args[1]}'</color>";
                }
                else
                {
                    whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>NetId not found for TrainCar '{args[1]}'</color>";
                }

                break;

            default:
                LogWarning($"Received 'SendPacket' chat command from player \"{sender.Username}\", but the packet type '{args[0].ToLower()}' was not recognised. Command: {message}");

                // Send a whisper back to the player who sent the command
                whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>Packet type '{args[0].ToLower()}' was not recognised</color>";

                break;
        }

        server.SendWhisperChatMessage(whisper, sender);
    }

    // Called when a player uses the chat command '/locopos' or '/lp'
    private void OnChatCommandSendLocoPos(string message, IPlayer sender)
    {
        //this chat command has no arguments
        Dictionary<string, Vector3> carMap = [];

        foreach (var kvp in TrainCarRegistry.Instance.logicCarToTrainCar)
        {
            Car logicCar = kvp.Key;
            TrainCar trainCar = kvp.Value;

            //locos only
            if (!trainCar.IsLoco)
                continue;

            if (!string.IsNullOrEmpty(logicCar.ID) && trainCar != null)
                carMap[logicCar.ID] = trainCar.transform.position - WorldMover.currentMove;
        }

        if (carMap.Count > 0)
            SendComplexPacket(carMap);

        var whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>Loco Position packet sent</color>";
        server.SendWhisperChatMessage(whisper, sender);
    }

    private void OnChatCommandClosestPlayer(string message, IPlayer sender)
    {
        string[] args = message.Split(' ');
        string whisper;

        if (args.Length < 1)
        {
            LogWarning($"Received 'ClosestPlayer' chat command from player \"{sender.Username}\", but not enough arguments were specified. Command: {message}");
            return;
        }

        if (string.IsNullOrEmpty(args[0]))
        {
            LogWarning($"Received 'ClosestPlayer' chat command from player \"{sender.Username}\", but the second argument is empty. Command: {message}");
        }

        var tc = GetTrainCarFromID(args[0].ToUpper());

        if (tc != null)
        {
            // Check the distance between all players and the TrainCar
            float closestSq = server.AnyPlayerSqrMag(tc.gameObject);
            float closest = Mathf.Sqrt(closestSq);

            // Send a whisper back to the player who sent the command
            whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>The closest player to {tc.ID} is {closest:F2} metres away</color>";
        }
        else
        {
            whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>TrainCar '{args[0]}' not found</color>";
        }

        server.SendWhisperChatMessage(whisper, sender);
    }

    private void OnChatCommandStats(string message, IPlayer sender)
    {
        StringBuilder whisper = new($"<color=#{MESSAGE_COLOUR_SERVER}>There {(server.PlayerCount > 1 ? "are" : "is")} {server.PlayerCount} connected player{(server.PlayerCount > 1 ? "s" : "")}:");

        foreach (var player in server.Players)
            whisper.Append($"<br>\t{(player.IsHost ? "<b>" : "")}{player.Username}{(player.IsHost ? "</b>" : "")} Id: {player.PlayerId}, Ping: {player.Ping}{(player.IsOnCar ? $", Riding {player.OccupiedCar.ID}" : "")}");

        whisper.Append("</color>");

        server.SendWhisperChatMessage(whisper.ToString(), sender);
    }
    #endregion

    #region Chat Help Callbacks
    private string OnChatCommandSendPacketHelp()
    {
        // this is a very basic example and a better localisation system should be used
        return LocalizationManager.CurrentLanguage switch
        {
            "German" => "Aktiviere den Server um ein Testpaket zu senden" +
                            "\r\n\t\t/packet <Typ: simple | net> <ID-Auto>" +
                            "\r\n\t\t/p <Typ: simple | net> <ID-Auto>" +
                            "\r\n\t\t/packet simple L-025",

            "Italian" => "Attiva il server per inviare un pacchetto di prova" +
                            "\r\n\t\t/packet <tipo: simple | net> <ID auto>" +
                            "\r\n\t\t/p <tipo: simple | net> <ID auto>" +
                            "\r\n\t\t/packet simple L-025",

            _ => "Trigger server to send a test packet" +
                            "\r\n\t\t/packet <type: simple | net> <Car ID>" +
                            "\r\n\t\t/p <type: simple | net> <Car ID>" +
                            "\r\n\t\t/packet simple L-025",
        };
    }
    private string OnChatCommandSendLocoPosHelp()
    {
        // this is a very basic example and a better localisation system should be used
        return LocalizationManager.CurrentLanguage switch
        {
            "German" => "Aktiviere den Server um ein Paket mit der Lokomotive und ihrer Position zu senden" +
                            "\r\n\t\t/locopos" +
                            "\r\n\t\t/lp",

            "Italian" => "Attiva il server per inviare un pacchetto complesso di auto e le loro posizioni" +
                            "\r\n\t\t/locopos" +
                            "\r\n\t\t/lp",

            _ => "Trigger server to send a complex packet of cars and their positions" +
                            "\r\n\t\t/locopos" +
                            "\r\n\t\t/lp",
        };
    }

    private string OnChatCommandClosestPlayerHelp()
    {
        // this is a very basic example and a better localisation system should be used
        return LocalizationManager.CurrentLanguage switch
        {
            "German" => "Aktiviere den Server um die Entfernung zwischen dem Spieler und dem nächsten Auto zu senden" +
                            "\r\n\t\t/closest <ID-Auto>" +
                            "\r\n\t\t/cd <Car ID>",

            "Italian" => "Restituisce la distanza tra un dato vagone e il giocatore più vicino" +
                            "\r\n\t\t/closest <ID auto>" +
                            "\r\n\t\t/cd <Car ID>",

            _ => "Returns the distance between a given TrainCar and the closest player" +
                            "\r\n\t\t/closest <Car ID>" +
                            "\r\n\t\t/cd <Car ID>",
        };
    }

    private string OnChatCommandStatsHelp()
    {
        // this is a very basic example and a better localisation system should be used
        return LocalizationManager.CurrentLanguage switch
        {
            "German" => "Gibt die Spieleranzahl und eine Liste aller verbundenen Spieler zurück" +
                            "\r\n\t\t/stats",

            "Italian" => "Restituisce il conteggio dei giocatori e l'elenco di tutti i giocatori connessi" +
                            "\r\n\t\t/stats",

            _ => "Returns player count and list of all connected players" +
                            "\r\n\t\t/stats",
        };
    }

    #endregion

    #region Chat Message Filters

    //simple swear filter (not intended for real use) - this is a basic example of a chat filter,but could be used for much more.
    private bool OnChatMessage(ref string message, IPlayer sender)
    {
        string[] veryBadWords = { "poo", "loser" };
        string[] moderatelyBadWords = { "bum", "dumb" };

        //check for very bad words - block the message entirely if found
        string localMessage = message;
        if (veryBadWords.Any(word => localMessage.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            //send a whisper back to the player
            var whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>Please do not swear on this server</color>";
            server.SendWhisperChatMessage(whisper, sender);

            //block the message from being sent
            return false;
        }

        //check for moderately bad words - allow the message but replace with astersiks
        foreach (string badWord in moderatelyBadWords)
        {
            var badWordstart = message.IndexOf(badWord, StringComparison.OrdinalIgnoreCase);
            while (badWordstart >= 0)
            {
                message = message.Remove(badWordstart, badWord.Length).Insert(badWordstart, new string('*', badWord.Length));
                badWordstart = message.IndexOf(badWord, badWordstart + badWord.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
    }
    #endregion

    #region helpers
    private TrainCar GetTrainCarFromID(string carId)
    {
        return TrainCarRegistry.Instance.logicCarToTrainCar.FirstOrDefault(kvp => kvp.Value.ID == carId).Value;
    }

    private WheelArrangement GetRandomWheelArrangement()
    {
        var values = Enum.GetValues(typeof(WheelArrangement));
        var random = new System.Random();
        return (WheelArrangement)values.GetValue(random.Next(values.Length));
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
