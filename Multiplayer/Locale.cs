using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using I2.Loc;
using Multiplayer.Utils;


namespace Multiplayer;

public static class Locale
{
    private const string DEFAULT_LOCALE_FILE = "locale.csv";
    private const string DEFAULT_LANGUAGE = "English";
    public const string MISSING_TRANSLATION = "[ MISSING TRANSLATION ]";
    public const string PREFIX = "multiplayer/";

    private const string PREFIX_MAIN_MENU = $"{PREFIX}mm";
    private const string PREFIX_SERVER_BROWSER = $"{PREFIX}sb";
    private const string PREFIX_SERVER_HOST = $"{PREFIX}host";
    private const string PREFIX_DISCONN_REASON = $"{PREFIX}dr";
    private const string PREFIX_CAREER_MANAGER = $"{PREFIX}carman";
    private const string PREFIX_PLAYER_LIST = $"{PREFIX}plist";
    private const string PREFIX_LOADING_INFO = $"{PREFIX}linfo";
    private const string PREFIX_CHAT_INFO = $"{PREFIX}chat";
    private const string PREFIX_PAUSE_MENU = $"{PREFIX}pm";
    private const string PREFIX_PERMISSIONS_SYSTEM = $"{PREFIX}ps";
    private const string PREFIX_COMMS_RADIO = $"{PREFIX}cr";

    #region Main Menu
    public static string MAIN_MENU__JOIN_SERVER => Get(MAIN_MENU__JOIN_SERVER_KEY);
    public const string MAIN_MENU__JOIN_SERVER_KEY = $"{PREFIX_MAIN_MENU}/join_server";

    public static string MAIN_MENU__INCOMPATIBLE_MODS => Get(MAIN_MENU__INCOMPATIBLE_MODS_KEY);
    public const string MAIN_MENU__INCOMPATIBLE_MODS_KEY = $"{PREFIX_MAIN_MENU}/incompatible_mods";

    public static string MAIN_MENU__UPDATE_TITLE => Get(MAIN_MENU__MAIN_MENU__UPDATE_TITLE_KEY);
    public const string MAIN_MENU__MAIN_MENU__UPDATE_TITLE_KEY = $"{PREFIX_MAIN_MENU}/update_title";

    public static string MAIN_MENU__UPDATE_LATEST => Get(MAIN_MENU__UPDATE_LATEST_KEY);
    public const string MAIN_MENU__UPDATE_LATEST_KEY = $"{PREFIX_MAIN_MENU}/update_latest";

    public static string MAIN_MENU__UPDATE_INSTALLED => Get(MAIN_MENU__UPDATE_INSTALLED_KEY);
    public const string MAIN_MENU__UPDATE_INSTALLED_KEY = $"{PREFIX_MAIN_MENU}/update_installed";

    public static string MAIN_MENU__UPDATE_ACTION => Get(MAIN_MENU__UPDATE_ACTION_KEY);
    public const string MAIN_MENU__UPDATE_ACTION_KEY = $"{PREFIX_MAIN_MENU}/update_action";
    #endregion

    #region Server Browser
    public static string SERVER_BROWSER__TITLE => Get(SERVER_BROWSER__TITLE_KEY);
    public const string SERVER_BROWSER__TITLE_KEY = $"{PREFIX_SERVER_BROWSER}/title";
    public static string SERVER_BROWSER__MANUAL_CONNECT => Get(SERVER_BROWSER__MANUAL_CONNECT_KEY);
    public const string SERVER_BROWSER__MANUAL_CONNECT_KEY = $"{PREFIX_SERVER_BROWSER}/manual_connect";
    public static string SERVER_BROWSER__HOST => Get(SERVER_BROWSER__HOST_KEY);
    public const string SERVER_BROWSER__HOST_KEY = $"{PREFIX_SERVER_BROWSER}/host";
    public static string SERVER_BROWSER__REFRESH => Get(SERVER_BROWSER__REFRESH_KEY);
    public const string SERVER_BROWSER__REFRESH_KEY = $"{PREFIX_SERVER_BROWSER}/refresh";
    public static string SERVER_BROWSER__JOIN => Get(SERVER_BROWSER__JOIN_KEY);
    public const string SERVER_BROWSER__JOIN_KEY = $"{PREFIX_SERVER_BROWSER}/join_game";
    public static string SERVER_BROWSER__IP => Get(SERVER_BROWSER__IP_KEY);
    private const string SERVER_BROWSER__IP_KEY = $"{PREFIX_SERVER_BROWSER}/ip";
    public static string SERVER_BROWSER__IP_INVALID => Get(SERVER_BROWSER__IP_INVALID_KEY);
    private const string SERVER_BROWSER__IP_INVALID_KEY = $"{PREFIX_SERVER_BROWSER}/ip_invalid";
    public static string SERVER_BROWSER__PORT => Get(SERVER_BROWSER__PORT_KEY);
    private const string SERVER_BROWSER__PORT_KEY = $"{PREFIX_SERVER_BROWSER}/port";
    public static string SERVER_BROWSER__PORT_INVALID => Get(SERVER_BROWSER__PORT_INVALID_KEY);
    private const string SERVER_BROWSER__PORT_INVALID_KEY = $"{PREFIX_SERVER_BROWSER}/port_invalid";
    public static string SERVER_BROWSER__PASSWORD => Get(SERVER_BROWSER__PASSWORD_KEY);
    private const string SERVER_BROWSER__PASSWORD_KEY = $"{PREFIX_SERVER_BROWSER}/password";
    public static string SERVER_BROWSER__PLAYERS => Get(SERVER_BROWSER__PLAYERS_KEY);
    private const string SERVER_BROWSER__PLAYERS_KEY = $"{PREFIX_SERVER_BROWSER}/players";

    public static string SERVER_BROWSER__PASSWORD_REQUIRED => Get(SERVER_BROWSER__PASSWORD_REQUIRED_KEY);
    private const string SERVER_BROWSER__PASSWORD_REQUIRED_KEY = $"{PREFIX_SERVER_BROWSER}/password_required";
    public static string SERVER_BROWSER__PASSWORD_REQUIRED_YES => Get(SERVER_BROWSER__PASSWORD_REQUIRED_YES_KEY);
    private const string SERVER_BROWSER__PASSWORD_REQUIRED_YES_KEY = $"{PREFIX_SERVER_BROWSER}/password_required_yes";
    public static string SERVER_BROWSER__PASSWORD_REQUIRED_NO => Get(SERVER_BROWSER__PASSWORD_REQUIRED_NO_KEY);
    private const string SERVER_BROWSER__PASSWORD_REQUIRED_NO_KEY = $"{PREFIX_SERVER_BROWSER}/password_required_no";

    public static string SERVER_BROWSER__GAME_VERSION => Get(SERVER_BROWSER__GAME_VERSION_KEY);
    private const string SERVER_BROWSER__GAME_VERSION_KEY = $"{PREFIX_SERVER_BROWSER}/game_version";
    public static string SERVER_BROWSER__MOD_VERSION => Get(SERVER_BROWSER__MOD_VERSION_KEY);
    private const string SERVER_BROWSER__MOD_VERSION_KEY = $"{PREFIX_SERVER_BROWSER}/mod_version";
    public static string SERVER_BROWSER__YES => Get(SERVER_BROWSER__YES_KEY);
    private const string SERVER_BROWSER__YES_KEY = $"{PREFIX_SERVER_BROWSER}/yes";
    public static string SERVER_BROWSER__NO => Get(SERVER_BROWSER__NO_KEY);
    private const string SERVER_BROWSER__NO_KEY = $"{PREFIX_SERVER_BROWSER}/no";
    public static string SERVER_BROWSER__OK => Get(SERVER_BROWSER__OK_KEY);
    private const string SERVER_BROWSER__OK_KEY = $"{PREFIX_SERVER_BROWSER}/ok";
    public static string SERVER_BROWSER__MISMATCH => Get(SERVER_BROWSER__MISMATCH_KEY);
    private const string SERVER_BROWSER__MISMATCH_KEY = $"{PREFIX_SERVER_BROWSER}/mismatch";
    public static string SERVER_BROWSER__MISSING => Get(SERVER_BROWSER__MISSING_KEY);
    private const string SERVER_BROWSER__MISSING_KEY = $"{PREFIX_SERVER_BROWSER}/missing";

    public static string SERVER_BROWSER__REQUIRED_MODS => Get(SERVER_BROWSER__REQUIRED_MODS_KEY);
    private const string SERVER_BROWSER__REQUIRED_MODS_KEY = $"{PREFIX_SERVER_BROWSER}/required_mods";

    public static string SERVER_BROWSER__EXTRA_MODS => Get(SERVER_BROWSER__EXTRA_MODS_KEY);
    private const string SERVER_BROWSER__EXTRA_MODS_KEY = $"{PREFIX_SERVER_BROWSER}/extra_mods";
    public static string SERVER_BROWSER__INCOMPATIBLE => Get(SERVER_BROWSER__INCOMPATIBLE_KEY);
    private const string SERVER_BROWSER__INCOMPATIBLE_KEY = $"{PREFIX_SERVER_BROWSER}/incompatible";
    public static string SERVER_BROWSER__EXTRA_MOD => Get(SERVER_BROWSER__EXTRA_MOD_KEY);
    private const string SERVER_BROWSER__EXTRA_MOD_KEY = $"{PREFIX_SERVER_BROWSER}/extra_mod";
    public static string SERVER_BROWSER__NO_SERVERS => Get(SERVER_BROWSER__NO_SERVERS_KEY);
    public const string SERVER_BROWSER__NO_SERVERS_KEY = $"{PREFIX_SERVER_BROWSER}/no_servers";
    public static string SERVER_BROWSER__INFO_TITLE => Get(SERVER_BROWSER__INFO_TITLE_KEY);
    public const string SERVER_BROWSER__INFO_TITLE_KEY = $"{PREFIX_SERVER_BROWSER}/info/title";
    public static string SERVER_BROWSER__INFO_CONTENT => Get(SERVER_BROWSER__INFO_CONTENT_KEY);
    public const string SERVER_BROWSER__INFO_CONTENT_KEY = $"{PREFIX_SERVER_BROWSER}/info/content";
    #endregion

    #region Server Host
    public static string SERVER_HOST__TITLE => Get(SERVER_HOST__TITLE_KEY);
    public const string SERVER_HOST__TITLE_KEY = $"{PREFIX_SERVER_HOST}/title";
    public static string SERVER_HOST_PASSWORD => Get(SERVER_HOST_PASSWORD_KEY);
    public const string SERVER_HOST_PASSWORD_KEY = $"{PREFIX_SERVER_HOST}/password";
    public static string SERVER_HOST_NAME => Get(SERVER_HOST_NAME_KEY);
    public const string SERVER_HOST_NAME_KEY = $"{PREFIX_SERVER_HOST}/name";
    public static string SERVER_HOST_PUBLIC => Get(SERVER_HOST_PUBLIC_KEY);
    public const string SERVER_HOST_PUBLIC_KEY = $"{PREFIX_SERVER_HOST}/public";
    public static string SERVER_HOST_VISIBILITY => Get(SERVER_HOST_PUBLIC_KEY);
    public const string SERVER_HOST_VISIBILITY_KEY = $"{PREFIX_SERVER_HOST}/visibility";

    public static string[] SERVER_HOST_VISIBILITY_MODES = [$"{SERVER_HOST_VISIBILITY_MODES_KEY}/private", $"{SERVER_HOST_VISIBILITY_MODES_KEY}/friends", $"{SERVER_HOST_VISIBILITY_MODES_KEY}/public"];
    public const string SERVER_HOST_VISIBILITY_MODES_KEY = $"{PREFIX_SERVER_HOST}/visibility/modes";
    public static string SERVER_HOST_DETAILS => Get(SERVER_HOST_DETAILS_KEY);
    public const string SERVER_HOST_DETAILS_KEY = $"{PREFIX_SERVER_HOST}/details";
    public static string SERVER_HOST_MAX_PLAYERS => Get(SERVER_HOST_MAX_PLAYERS_KEY);
    public const string SERVER_HOST_MAX_PLAYERS_KEY = $"{PREFIX_SERVER_HOST}/max_players";
    public static string SERVER_HOST_START => Get(SERVER_HOST_START_KEY);
    public const string SERVER_HOST_START_KEY = $"{PREFIX_SERVER_HOST}/start";

    public static string SERVER_HOST__INSTRUCTIONS_FIRST => Get(SERVER_HOST__INSTRUCTIONS_FIRST_KEY);
    public const string SERVER_HOST__INSTRUCTIONS_FIRST_KEY = $"{PREFIX_SERVER_HOST}/instructions/first";
    public static string SERVER_HOST__MOD_WARNING => Get(SERVER_HOST__MOD_WARNING_KEY);

    public const string SERVER_HOST__MOD_WARNING_KEY = $"{PREFIX_SERVER_HOST}/instructions/mod_warning";
    public static string SERVER_HOST__RECOMMEND => Get(SERVER_HOST__RECOMMEND_KEY);
    public const string SERVER_HOST__RECOMMEND_KEY = $"{PREFIX_SERVER_HOST}/instructions/recommend";
    public static string SERVER_HOST__SIGNOFF => Get(SERVER_HOST__SIGNOFF_KEY);
    public const string SERVER_HOST__SIGNOFF_KEY = $"{PREFIX_SERVER_HOST}/instructions/signoff";



    #endregion

    #region Disconnect Reason
    public static string DISCONN_REASON__UNKNOWN_HOST => Get(DISCONN_REASON__UNKNOWN_HOST_KEY);
    public const string DISCONN_REASON__UNKNOWN_HOST_KEY = $"{PREFIX_DISCONN_REASON}/unknown";

    public static string DISCONN_REASON__HOST_UNREACHABLE => Get(DISCONN_REASON__HOST_UNREACHABLE_KEY);
    public const string DISCONN_REASON__HOST_UNREACHABLE_KEY = $"{PREFIX_DISCONN_REASON}/unreachable";

    public static string DISCONN_REASON__HOST_TIMED_OUT => Get(DISCONN_REASON__HOST_TIMED_OUT_KEY);
    public const string DISCONN_REASON__HOST_TIMED_OUT_KEY = $"{PREFIX_DISCONN_REASON}/timeout";

    public static string DISCONN_REASON__REJECTED => Get(DISCONN_REASON__REJECTED_KEY);
    public const string DISCONN_REASON__REJECTED_KEY = $"{PREFIX_DISCONN_REASON}/rejected";

    public static string DISCONN_REASON__SHUTTING_DOWN => Get(DISCONN_REASON__SHUTTING_DOWN_KEY);
    public const string DISCONN_REASON__SHUTTING_DOWN_KEY = $"{PREFIX_DISCONN_REASON}/shutdown";

    public static string DISCONN_REASON__PLAYER_KICKED => Get(DISCONN_REASON__PLAYER_KICKED_KEY);
    public const string DISCONN_REASON__PLAYER_KICKED_KEY = $"{PREFIX_DISCONN_REASON}/kicked";

    public static string DISCONN_REASON__INVALID_PASSWORD => Get(DISCONN_REASON__INVALID_PASSWORD_KEY);
    public const string DISCONN_REASON__INVALID_PASSWORD_KEY = $"{PREFIX_DISCONN_REASON}/invalid_password";

    public static string DISCONN_REASON__GAME_VERSION => Get(DISCONN_REASON__GAME_VERSION_KEY);
    public const string DISCONN_REASON__GAME_VERSION_KEY = $"{PREFIX_DISCONN_REASON}/game_version";

    public static string DISCONN_REASON__FULL_SERVER => Get(DISCONN_REASON__FULL_SERVER_KEY);
    public const string DISCONN_REASON__FULL_SERVER_KEY = $"{PREFIX_DISCONN_REASON}/full_server";

    public static string DISCONN_REASON__MODS => Get(DISCONN_REASON__MODS_KEY);
    public const string DISCONN_REASON__MODS_KEY = $"{PREFIX_DISCONN_REASON}/mods";

    public static string DISCONN_REASON__MOD_LIST => Get(DISCONN_REASON__MOD_LIST_KEY);
    public const string DISCONN_REASON__MOD_LIST_KEY = $"{PREFIX_DISCONN_REASON}/mod_list";

    public static string DISCONN_REASON__MODS_MISSING => Get(DISCONN_REASON__MODS_MISSING_KEY);
    public const string DISCONN_REASON__MODS_MISSING_KEY = $"{PREFIX_DISCONN_REASON}/mods_missing";

    public static string DISCONN_REASON__MODS_EXTRA => Get(DISCONN_REASON__MODS_EXTRA_KEY);
    public const string DISCONN_REASON__MODS_EXTRA_KEY = $"{PREFIX_DISCONN_REASON}/mods_extra";

    public static string DISCONN_REASON__MODS_INCOMPATIBLE => Get(DISCONN_REASON__MODS_INCOMPATIBLE_KEY);
    public const string DISCONN_REASON__MODS_INCOMPATIBLE_KEY = $"{PREFIX_DISCONN_REASON}/mods_incompatible";
    #endregion

    #region Career Manager
    public static string CAREER_MANAGER__FEES_HOST_ONLY => Get(CAREER_MANAGER__FEES_HOST_ONLY_KEY);
    private const string CAREER_MANAGER__FEES_HOST_ONLY_KEY = $"{PREFIX_CAREER_MANAGER}/fees_host_only";
    #endregion

    #region Player List
    public static string PLAYER_LIST__TITLE => Get(PLAYER_LIST__TITLE_KEY);
    private const string PLAYER_LIST__TITLE_KEY = $"{PREFIX_PLAYER_LIST}/title";
    #endregion

    #region Loading Info
    public static string LOADING_INFO__WAIT_FOR_SERVER => Get(LOADING_INFO__WAIT_FOR_SERVER_KEY);
    private const string LOADING_INFO__WAIT_FOR_SERVER_KEY = $"{PREFIX_LOADING_INFO}/wait_for_server";

    public static string LOADING_INFO__SYNC_WORLD_STATE => Get(LOADING_INFO__SYNC_WORLD_STATE_KEY);
    private const string LOADING_INFO__SYNC_WORLD_STATE_KEY = $"{PREFIX_LOADING_INFO}/sync_world_state";
    #endregion

    #region Chat
    public static string CHAT_PLACEHOLDER => Get(CHAT_PLACEHOLDER_KEY);
    public const string CHAT_PLACEHOLDER_KEY = $"{PREFIX_CHAT_INFO}/placeholder";
    public static string CHAT_HELP_AVAILABLE => Get(CHAT_HELP_AVAILABLE_KEY);
    public const string CHAT_HELP_AVAILABLE_KEY = $"{PREFIX_CHAT_INFO}/help/available";
    public static string CHAT_HELP_SERVER_MSG => Get(CHAT_HELP_SERVER_MSG_KEY);
    public const string CHAT_HELP_SERVER_MSG_KEY = $"{PREFIX_CHAT_INFO}/help/servermsg";
    public static string CHAT_HELP_WHISPER_MSG => Get(CHAT_HELP_WHISPER_MSG_KEY);
    public const string CHAT_HELP_WHISPER_MSG_KEY = $"{PREFIX_CHAT_INFO}/help/whispermsg";
    public static string CHAT_HELP_SET_CREW_NAME_HOST_MSG => Get(CHAT_HELP_SET_CREW_NAME_HOST_MSG_KEY);
    public const string CHAT_HELP_SET_CREW_NAME_HOST_MSG_KEY = $"{PREFIX_CHAT_INFO}/help/setcrewname_host";
    public static string CHAT_HELP_SET_CREW_NAME_CLIENT_MSG => Get(CHAT_HELP_SET_CREW_NAME_CLIENT_MSG_KEY);
    public const string CHAT_HELP_SET_CREW_NAME_CLIENT_MSG_KEY = $"{PREFIX_CHAT_INFO}/help/setcrewname_client";

    public static string CHAT_HELP_HELP => Get(CHAT_HELP_HELP_KEY);
    public const string CHAT_HELP_HELP_KEY = $"{PREFIX_CHAT_INFO}/help/help";
    public static string CHAT_HELP_MSG => Get(CHAT_HELP_MSG_KEY);
    public const string CHAT_HELP_MSG_KEY = $"{PREFIX_CHAT_INFO}/help/msg";
    public static string CHAT_HELP_PLAYER_NAME => Get(CHAT_HELP_PLAYER_NAME_KEY);
    public const string CHAT_HELP_PLAYER_NAME_KEY = $"{PREFIX_CHAT_INFO}/help/playername";
    public static string CHAT_HELP_CREW_NAME => Get(CHAT_HELP_CREW_NAME_KEY);
    public const string CHAT_HELP_CREW_NAME_KEY = $"{PREFIX_CHAT_INFO}/help/crewname";

    public static string CHAT_WHISPER_NOT_FOUND => Get(CHAT_WHISPER_NOT_FOUND_KEY);
    public const string CHAT_WHISPER_NOT_FOUND_KEY = $"{PREFIX_CHAT_INFO}/whisper/not_found";

    public static string CHAT_KICK_UNABLE => Get(CHAT_KICK_UNABLE_KEY);
    public const string CHAT_KICK_UNABLE_KEY = $"{PREFIX_CHAT_INFO}/kick/unable";
    public static string CHAT_KICK_KICKED => Get(CHAT_KICK_KICKED_KEY);
    public const string CHAT_KICK_KICKED_KEY = $"{PREFIX_CHAT_INFO}/kick/kicked";
    public static string CHAT_SET_CREW_DISALLOWED => Get(CHAT_SET_CREW_DISALLOWED_KEY);
    public const string CHAT_SET_CREW_DISALLOWED_KEY = $"{PREFIX_CHAT_INFO}/setcrew/set_crew_disallowed";
    public static string CHAT_SET_CREW_PLAYER_NOT_FOUND => Get(CHAT_SET_CREW_PLAYER_NOT_FOUND_KEY);
    public const string CHAT_SET_CREW_PLAYER_NOT_FOUND_KEY = $"{PREFIX_CHAT_INFO}/setcrew/player_not_found";
    public static string CHAT_SET_CREW_JOINED_MSG => Get(CHAT_SET_CREW_JOINED_MSG_KEY);
    public const string CHAT_SET_CREW_JOINED_MSG_KEY = $"{PREFIX_CHAT_INFO}/setcrew/joined";



    #endregion

    #region Pause Menu
    public static string PAUSE_MENU_DISCONNECT => Get(PAUSE_MENU_DISCONNECT_KEY);
    public const string PAUSE_MENU_DISCONNECT_KEY = $"{PREFIX_PAUSE_MENU}/disconnect_msg";

    public static string PAUSE_MENU_QUIT => Get(PAUSE_MENU_QUIT_KEY);
    public const string PAUSE_MENU_QUIT_KEY = $"{PREFIX_PAUSE_MENU}/quit_msg";
    #endregion

    #region Permissions System
    public static string PERMISSIONS_INSUFFICIENT => Get(PERMISSIONS_INSUFFICIENT_KEY);
    public const string PERMISSIONS_INSUFFICIENT_KEY = $"{PREFIX_PERMISSIONS_SYSTEM}/insufficient";
    #endregion

    #region Comms Radio
    public static string COMMS_RADIO_WORK_TRAIN_IN_USE => Get(COMMS_RADIO_WORK_TRAIN_IN_USE_KEY);
    public const string COMMS_RADIO_WORK_TRAIN_IN_USE_KEY = $"{PREFIX_COMMS_RADIO}/in_use";
    #endregion

    private static bool initializeAttempted;
    private static ReadOnlyDictionary<string, Dictionary<string, string>> csv;

    public static void Load(string localeDir)
    {
        initializeAttempted = true;
        string path = Path.Combine(localeDir, DEFAULT_LOCALE_FILE);
        if (!File.Exists(path))
        {
            Multiplayer.LogError($"Failed to find locale file at '{path}'! Please make sure it's there.");
            return;
        }

        csv = Csv.Parse(File.ReadAllText(path));
        //Multiplayer.LogDebug(() => $"Locale dump: {Csv.Dump(csv)}");
    }

    public static string Get(string key, string overrideLanguage = null)
    {
        if (!initializeAttempted)
            throw new InvalidOperationException("Not initialized");

        if (csv == null)
            return MISSING_TRANSLATION;

        string locale = overrideLanguage ?? LocalizationManager.CurrentLanguage;
        if (!csv.ContainsKey(locale))
        {
            if (locale == DEFAULT_LANGUAGE)
            {
                Multiplayer.LogError($"Failed to find locale language {locale}! Something is broken, this shouldn't happen. Dumping CSV data:");
                Multiplayer.LogError($"\n{Csv.Dump(csv)}");
                return MISSING_TRANSLATION;
            }

            locale = DEFAULT_LANGUAGE;
            Multiplayer.LogWarning($"Failed to find locale language {locale}");
        }

        Dictionary<string, string> localeDict = csv[locale];
        string actualKey = key.StartsWith(PREFIX) ? key.Substring(PREFIX.Length) : key;
        if (localeDict.TryGetValue(actualKey, out string value))
        {
            if (string.IsNullOrEmpty(value))
                return overrideLanguage == null && locale != DEFAULT_LANGUAGE ? Get(actualKey, DEFAULT_LANGUAGE) : MISSING_TRANSLATION;
            return value;
        }

        Multiplayer.LogDebug(() => $"Failed to find locale key '{actualKey}'!");
        return MISSING_TRANSLATION;
    }

    public static string Get(string key, params object[] placeholders)
    {
        return string.Format(Get(key), placeholders);
    }

    public static string Get(string key, params string[] placeholders)
    {
        return Get(key, (object[])placeholders);
    }
}
