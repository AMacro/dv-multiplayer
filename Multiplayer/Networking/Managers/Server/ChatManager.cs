using LiteNetLib;
using Multiplayer.Components.Networking;
using System.Linq;
using Multiplayer.Networking.Data;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Multiplayer.Networking.Managers.Server;

public static class ChatManager
{
    public const string COMMAND_SERVER = "server";
    public const string COMMAND_SERVER_SHORT = "s";
    public const string COMMAND_WHISPER = "whisper";
    public const string COMMAND_WHISPER_SHORT = "w";
    public const string COMMAND_HELP_SHORT = "?";
    public const string COMMAND_HELP = "help";

    public const string MESSAGE_COLOUR_SERVER = "9CDCFE";
    public const string MESSAGE_COLOUR_HELP = "00FF00";

    public static void ProcessMessage(string message, NetPeer sender)
    {

        if (message == null || message == string.Empty)
            return;

        //Check we could find the sender player data
        if (!NetworkLifecycle.Instance.Server.TryGetServerPlayer(sender, out var player))
            return;


        //Check if we have a command
        if (message.StartsWith("/"))
        {
            string command = message.Substring(1).Split(' ')[0];

            switch (command)
            {
                case COMMAND_SERVER_SHORT:
                    ServerMessage(message, sender, null, COMMAND_SERVER_SHORT.Length);
                    break;
                case COMMAND_SERVER:
                    ServerMessage(message, sender, null, COMMAND_SERVER.Length);
                    break;

                case COMMAND_WHISPER_SHORT:
                    WhisperMessage(message, COMMAND_WHISPER_SHORT.Length, player.Username, sender);
                    break;
                case COMMAND_WHISPER:
                    WhisperMessage(message, COMMAND_WHISPER.Length, player.Username, sender);
                    break;

                case COMMAND_HELP_SHORT:
                    HelpMessage(sender);
                    break;
                case COMMAND_HELP:
                    HelpMessage(sender);
                    break;

                //allow messages that are not commands to go through
                default:
                    ChatMessage(message,player.Username, sender);
                    break;
            }

            return;

        }

        //not a server command, process as normal message
        ChatMessage(message, player.Username, sender);
    }

    private static void ChatMessage(string message, string sender, NetPeer peer)
    {
        //clean up the message to stop format injection
        message = Regex.Replace(message, "</noparse>", string.Empty, RegexOptions.IgnoreCase);

        message = $"<alpha=#50>{sender}:</color> <noparse>{message}</noparse>";
        NetworkLifecycle.Instance.Server.SendChat(message, peer);
    }

    public static void ServerMessage(string message, NetPeer sender, NetPeer exclude = null, int commandLength =-1)
    {
        //If user is not the host, we should ignore - will require changes for dedicated server
        if (sender !=null && !NetworkLifecycle.Instance.IsHost(sender))
            return;

        //Remove the command "/server" or "/s"
        if (commandLength > 0)
        {
            message = message.Substring(commandLength + 2);
        }

        message = $"<color=#{MESSAGE_COLOUR_SERVER}>{message}</color>";
        NetworkLifecycle.Instance.Server.SendChat(message, exclude);
    }

    private static void WhisperMessage(string message, int commandLength, string senderName, NetPeer sender)
    {
        NetPeer recipient;
        string recipientName;

        Multiplayer.Log($"Whispering: \"{message}\", sender: {senderName}, senderID: {sender?.Id}");

        //Remove the command "/whisper" or "/w"
        message = message.Substring(commandLength + 2);

        if (message == null || message == string.Empty)
            return;

        /*
        //Check if name is in Quotes e.g. '/w "Mr Noname" my message'
        if (message.StartsWith("\""))
        {
            int endQuote = message.Substring(1).IndexOf('"');
            if (endQuote == -1 || endQuote == 0)
                return;

            recipientName = message.Substring(1, endQuote);

            //Remove the peer name
            message = message.Substring(recipientName.Length + 3);
        }
        else
        {*/
            recipientName = message.Split(' ')[0];

            //Remove the peer name
            message = message.Substring(recipientName.Length + 1);
        //}

        Multiplayer.Log($"Whispering parse 1: \"{message}\", sender: {senderName}, senderID: {sender?.Id}, peerName: {recipientName}");

        //look up the peer ID
        recipient = NetPeerFromName(recipientName);
        if(recipient == null)
        {
            Multiplayer.Log($"Whispering failed: \"{message}\", sender: {senderName}, senderID: {sender?.Id}, peerName: {recipientName}");

            message = $"<color=#{MESSAGE_COLOUR_SERVER}>{recipientName} not found - you're whispering into the void!</color>";
            NetworkLifecycle.Instance.Server.SendWhisper(message, sender);
            return;
        }

        Multiplayer.Log($"Whispering parse 2: \"{message}\", sender: {senderName}, senderID: {sender?.Id}, peerName: {recipientName}, peerID: {recipient?.Id}");

        //clean up the message to stop format injection
        message = Regex.Replace(message, "</noparse>", string.Empty, RegexOptions.IgnoreCase);

        message = "<i><alpha=#50>" + senderName + ":</color> <noparse>" + message + "</noparse></i>";

        NetworkLifecycle.Instance.Server.SendWhisper(message, recipient);
    }

    private static void HelpMessage(NetPeer peer)
    {
        string message = $"<color=#{MESSAGE_COLOUR_HELP}>Available commands:" +

                        "\r\n\r\n\tSend a message as the server (host only)" +
                        "\r\n\t\t/server <message>" +
                        "\r\n\t\t/s <message>" +

                        "\r\n\r\n\tWhisper to a player" +
                        "\r\n\t\t/whisper <PlayerName> <message>" +
                        "\r\n\t\t/w <PlayerName> <message>" +

                        "\r\n\r\n\tDisplay this help message" +
                        "\r\n\t\t/help" +
                        "\r\n\t\t/?" +

                        "</color>";

        NetworkLifecycle.Instance.Server.SendWhisper(message, peer);
    }


    private static NetPeer NetPeerFromName(string peerName)
    {
     
        if(peerName == null || peerName == string.Empty)
            return null;

        ServerPlayer player = NetworkLifecycle.Instance.Server.ServerPlayers.Where(p => p.Username == peerName).FirstOrDefault();
        if (player == null)
            return null;

        if(NetworkLifecycle.Instance.Server.TryGetNetPeer(player.Id, out NetPeer peer))
        {
            return peer;
        }

        return null;

    }
}
