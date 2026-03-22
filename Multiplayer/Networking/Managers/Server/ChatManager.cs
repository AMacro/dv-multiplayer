using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Multiplayer.Networking.Managers.Server;

public delegate void ChatCommandCallbackInternal(string message, ServerPlayer sender);
public delegate bool ChatFilterDelegateInternal(ref string message, ServerPlayer sender);

public class ChatManager
{
    public const string COMMAND_SERVER = "server";
    public const string COMMAND_SERVER_SHORT = "s";
    public const string COMMAND_WHISPER = "whisper";
    public const string COMMAND_WHISPER_SHORT = "w";
    public const string COMMAND_HELP = "help";
    public const string COMMAND_HELP_SHORT = "?";
    public const string COMMAND_LOG = "log";
    public const string COMMAND_LOG_SHORT = "l";
    public const string COMMAND_KICK = "kick";
    public const string COMMAND_SET_CREW_NAME = "crew";
    public const string COMMAND_SET_CREW_NAME_SHORT = "sc";


    public const string MESSAGE_COLOUR_SERVER = "9CDCFE";
    public const string MESSAGE_COLOUR_HELP = "00FF00";

    private readonly Dictionary<string, ChatCommandCallbackInternal> _registeredCommands = [];
    private readonly Dictionary<string, Func<string>> _registeredHelpMessages = [];
    private readonly List<ChatFilterDelegateInternal> _chatFilters = [];

    public ChatManager()
    {
        RegisterBuiltInCommands();
    }

    private void RegisterBuiltInCommands()
    {
        RegisterChatCommand
        (
            COMMAND_SERVER,
            COMMAND_SERVER_SHORT,
            () => $"{Locale.CHAT_HELP_SERVER_MSG}" +
                    $"\r\n\t\t/{COMMAND_SERVER} <{Locale.CHAT_HELP_MSG}>" +
                    $"\r\n\t\t/{COMMAND_SERVER_SHORT} <{Locale.CHAT_HELP_MSG}>",
            (message, sender) => ServerMessage(message, sender, null)
        );

        RegisterChatCommand
        (
            COMMAND_WHISPER,
            COMMAND_WHISPER_SHORT,
            () => $"{Locale.CHAT_HELP_WHISPER_MSG}" +
                    $"\r\n\t\t/{COMMAND_WHISPER} <{Locale.CHAT_HELP_PLAYER_NAME}> <{Locale.CHAT_HELP_MSG}>" +
                    $"\r\n\t\t/{COMMAND_WHISPER_SHORT} <{Locale.CHAT_HELP_PLAYER_NAME}> <{Locale.CHAT_HELP_MSG}>",
            WhisperMessage
        );

        RegisterChatCommand
        (
            COMMAND_HELP,
            COMMAND_HELP_SHORT,
            () => $"{Locale.CHAT_HELP_HELP}" +
                    $"\r\n\t\t/{COMMAND_HELP}" +
                    $"\r\n\t\t/{COMMAND_HELP_SHORT}",
            HelpMessage
        );

        RegisterChatCommand
        (
            COMMAND_KICK,
            null,
            () => $"Kick a player from the server (host only)" +
                    $"\r\n\t\t/{COMMAND_KICK}",
            KickMessage
        );

        RegisterChatCommand
        (
            COMMAND_SET_CREW_NAME,
            COMMAND_SET_CREW_NAME_SHORT,
            () => $"{Locale.CHAT_HELP_SET_CREW_NAME_HOST_MSG}" +
                    $"\r\n\t\t/{COMMAND_SET_CREW_NAME} <{Locale.CHAT_HELP_PLAYER_NAME}> <{Locale.CHAT_HELP_CREW_NAME}>" +
                    $"\r\n\t\t/{COMMAND_SET_CREW_NAME_SHORT} <{Locale.CHAT_HELP_PLAYER_NAME}> <{Locale.CHAT_HELP_CREW_NAME}>" +
                    $"\r\n{Locale.CHAT_HELP_SET_CREW_NAME_CLIENT_MSG}" +
                    $"\r\n\t\t/{COMMAND_SET_CREW_NAME} <{Locale.CHAT_HELP_CREW_NAME}>" +
                    $"\r\n\t\t/{COMMAND_SET_CREW_NAME_SHORT} <{Locale.CHAT_HELP_CREW_NAME}>",
                SetCrewNameMessage
        );

#if DEBUG
        RegisterChatCommand
        (
            COMMAND_LOG,
            COMMAND_LOG_SHORT,
            null,
            (args, sender) =>
                Multiplayer.specLog = !Multiplayer.specLog);
#endif
    }

    public bool RegisterChatCommand(string commandLong, string commandShort, Func<string> helpMessage, ChatCommandCallbackInternal callback)
    {
        if (string.IsNullOrEmpty(commandLong) || callback == null)
            return false;

        if (_registeredCommands.ContainsKey(commandLong.ToLower()) ||
            (!string.IsNullOrEmpty(commandShort) && _registeredCommands.ContainsKey(commandShort.ToLower())))
            return false;

        _registeredCommands[commandLong.ToLower()] = callback;

        if (!string.IsNullOrEmpty(commandShort) && !_registeredCommands.ContainsKey(commandShort.ToLower()))
        {
            _registeredCommands[commandShort.ToLower()] = callback;
        }

        if (helpMessage != null)
        {
            _registeredHelpMessages[commandLong.ToLower()] = helpMessage;
        }

        return true;
    }

    public void RegisterChatFilter(ChatFilterDelegateInternal callback)
    {
        if (callback != null)
        {
            _chatFilters.Add(callback);
        }
    }

    public void ProcessMessage(string message, ServerPlayer sender)
    {

        if (string.IsNullOrEmpty(message))
            return;

        Multiplayer.LogDebug(() => $"ProcessMessage(\'{message}\')");

        //Check if we have a command
        if (message.StartsWith("/"))
        {
            string[] messageParams = message.Substring(1).Split(' ');
            string command = messageParams[0].ToLower();

            Multiplayer.LogDebug(() => $"ProcessMessage(\'{message}\') starts with, substr: {message.Substring(0)}), command: {command}");

            //check registered commands
            if (!string.IsNullOrEmpty(command) && _registeredCommands.TryGetValue(command, out var commandCallback))
            {
                //remove the command, leading slash and trailing space
                var cleanedMessage = message.Substring(command.Length + 1).Trim();

                Multiplayer.LogDebug(() => $"ProcessMessage(\'{message}\') cleaned message: {cleanedMessage}");

                commandCallback(cleanedMessage, sender);

                return;
            }
        }

        //not a server command, process as normal message
        ProcessChatMessage(message, sender);
    }

    private void ProcessChatMessage(string message, ServerPlayer sender)
    {
        if (sender == null)
            return;

        //clean up the message to stop format injection
        message = Regex.Replace(message, "</noparse>", string.Empty, RegexOptions.IgnoreCase);

        //call each filter until either a filter returns false or all filters have been called
        foreach (var filter in _chatFilters)
        {
            if (!filter(ref message, sender))
                return;
        }

        message = $"<alpha=#50>{sender.DisplayName}:</color> <noparse>{message}</noparse>";
        NetworkLifecycle.Instance.Server.SendChat(message, sender);
    }

    public void ServerMessage(string message, ServerPlayer sender, ServerPlayer exclude = null)
    {
        //If user is not the host, we should ignore - will require changes for dedicated server
        if (sender != null && !NetworkLifecycle.Instance.IsHost(sender))
            return;

        message = $"<color=#{MESSAGE_COLOUR_SERVER}>{message}</color>";
        NetworkLifecycle.Instance.Server.SendChat(message, exclude);
    }

    private void WhisperMessage(string message, ServerPlayer sender)
    {
        Multiplayer.LogDebug(() => $"Whispering: \"{message}\", sender: {sender?.Username}, senderID: {sender?.PlayerId}");

        if (sender == null)
            return;

        if (string.IsNullOrEmpty(message))
            return;

        string[] parts = message.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return;

        string recipientName = parts[0];
        string whisperMessage = parts[1];


        Multiplayer.LogDebug(() => $"Whispering parse 1: \"{message}\", sender: {sender?.Username}, senderID: {sender?.PlayerId}, peerName: {recipientName}");

        //look up the peer ID
        var recipient = ServerPlayerFromUsername(recipientName);
        if (recipient == null)
        {
            Multiplayer.LogDebug(() => $"Whispering failed: \"{message}\", sender: {sender?.Username}, senderID: {sender?.PlayerId}, peerName: {recipientName}");

            whisperMessage = $"<color=#{MESSAGE_COLOUR_SERVER}>{Locale.Get(Locale.CHAT_WHISPER_NOT_FOUND_KEY, [recipientName])}</color>";
            NetworkLifecycle.Instance.Server.SendWhisper(whisperMessage, sender);
            return;
        }

        Multiplayer.LogDebug(() => $"Whispering parse 2: \"{message}\", sender: {sender?.Username}, senderID: {sender?.PlayerId}, peerName: {recipientName}, peerID: {recipient?.PlayerId}");

        //clean up the message to stop format injection
        whisperMessage = Regex.Replace(whisperMessage, "</noparse>", string.Empty, RegexOptions.IgnoreCase);

        //call each chat filter until either a filter returns false or all filters have been called
        foreach (var filter in _chatFilters)
        {
            if (!filter(ref message, sender))
                return;
        }

        whisperMessage = "<i><alpha=#50>" + sender.DisplayName + ":</color> <noparse>" + whisperMessage + "</noparse></i>";

        NetworkLifecycle.Instance.Server.SendWhisper(whisperMessage, recipient);
    }

    public void KickMessage(string message, ServerPlayer sender)
    {
        ServerPlayer playerToKick;
        string playerName;
        string whisper;

        //If user is not the host, we should ignore - will require changes for dedicated server
        if (sender == null || !NetworkLifecycle.Instance.IsHost(sender))
            return;

        playerName = message.Split(' ')[0];
        if (string.IsNullOrEmpty(playerName))
            return;

        playerToKick = ServerPlayerFromUsername(playerName);

        if (playerToKick == null || NetworkLifecycle.Instance.IsHost(playerToKick))
        {
            whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>{Locale.Get(Locale.CHAT_KICK_UNABLE_KEY, [playerName])}</color>";
        }
        else
        {
            whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>{Locale.Get(Locale.CHAT_KICK_KICKED_KEY, [playerName])}</color>";

            NetworkLifecycle.Instance.Server.KickPlayer(playerToKick);
        }

        NetworkLifecycle.Instance.Server.SendWhisper(whisper, sender);
    }

    private void SetCrewNameMessage(string message, ServerPlayer sender)
    {
        ServerPlayer playerToSet;
        string playerName;
        string crewName;

        if (sender == null)
            return;

        string[] parts = message.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 1)
            return;

        if (!NetworkLifecycle.Instance.IsHost(sender))
        {
            if (!Multiplayer.Settings.AllowClientCrewNames)
            {
                var whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>{Locale.CHAT_SET_CREW_DISALLOWED}</color>";
                NetworkLifecycle.Instance.Server.SendWhisper(whisper, sender);
                return;
            }

            playerToSet = sender;
            crewName = message;
        }
        else
        {
            playerName = parts[0];
            playerToSet = ServerPlayerFromUsername(playerName);

            if (playerToSet == null)
            {
                string whisper = $"<color=#{MESSAGE_COLOUR_SERVER}>{Locale.Get(Locale.CHAT_SET_CREW_PLAYER_NOT_FOUND_KEY, [playerName])}</color>";
                NetworkLifecycle.Instance.Server.SendWhisper(whisper, sender);
                return;
            }

            crewName = string.Join(" ", parts.Skip(1));
        }
      
        playerToSet.CrewName = crewName;

        if (crewName != string.Empty)
        {
            string announce = $"<color=#{MESSAGE_COLOUR_SERVER}>{Locale.Get(Locale.CHAT_SET_CREW_JOINED_MSG_KEY, [playerToSet.Username, playerToSet.CrewName])}</color>";

            NetworkLifecycle.Instance.Server.SendChat(announce);
        }
    }

    private void HelpMessage(string _, ServerPlayer player)
    {

        if (player == null)
            return;

        Multiplayer.LogDebug(() => $"HelpMessage()");

        StringBuilder sb = new($"<color=#{MESSAGE_COLOUR_HELP}>{Locale.CHAT_HELP_AVAILABLE}");

        foreach (var helpMessage in _registeredHelpMessages)
        {
            var help = helpMessage.Value?.Invoke();
            if (help != null)
                sb.AppendLine("\r\n\t" + help);
        }

        sb.AppendLine("</color>");

        /*
         * $"<color=#{MESSAGE_COLOUR_HELP}>Available commands:" +

                        "\r\n\r\n\tSend a message as the server (host only)" +
                        "\r\n\t\t/server <message>" +
                        "\r\n\t\t/s <message>" +

                        "\r\n\r\n\tWhisper to a playerToKick" +
                        "\r\n\t\t/whisper <PlayerName> <message>" +
                        "\r\n\t\t/w <PlayerName> <message>" +

                        "\r\n\r\n\tDisplay this help message" +
                        "\r\n\t\t/help" +
                        "\r\n\t\t/?" +

                        "</color>";
        */
        NetworkLifecycle.Instance.Server.SendWhisper(sb.ToString(), player);
    }


    private ServerPlayer ServerPlayerFromUsername(string playerName)
    {

        if (string.IsNullOrEmpty(playerName))
            return null;

        return NetworkLifecycle.Instance.Server.ServerPlayers.Where(p => p.Username == playerName).FirstOrDefault();
    }
}
