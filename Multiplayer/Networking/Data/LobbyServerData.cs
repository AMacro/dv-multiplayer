using Multiplayer.Components.MainMenu;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Networking.Data
{
    public class LobbyServerData : IServerBrowserGameDetails
    {
 
        public string id { get; set; }
 
        public string ip { get; set; }
        public int port { get; set; }

        [JsonProperty("server_name")]
        public string Name { get; set; }


        [JsonProperty("password_protected")]
        public bool HasPassword { get; set; }


        [JsonProperty("game_mode")]
        public int GameMode { get; set; }


        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }


        [JsonProperty("time_passed")]
        public string TimePassed { get; set; }


        [JsonProperty("current_players")]
        public int CurrentPlayers { get; set; }


        [JsonProperty("max_players")]
        public int MaxPlayers { get; set; }


        [JsonProperty("required_mods")]
        public string RequiredMods { get; set; }


        [JsonProperty("game_version")]
        public string GameVersion { get; set; }


        [JsonProperty("multiplayer_version")]
        public string MultiplayerVersion { get; set; }


        [JsonProperty("server_info")]
        public string ServerDetails { get; set; }

        [JsonIgnore]
        public int Ping { get; set; }


        public void Dispose() { }
        public static int GetDifficultyFromString(string difficulty)
        {
            int diff = 0;

            switch (difficulty)
            {
                case "Standard":
                    diff = 0;
                    break;
                case "Comfort":
                    diff = 1;
                    break;
                case "Realistic":
                    diff = 2;
                    break;
                default:
                    diff = 3;
                    break;
            }
            return diff;
        }

        public static string GetDifficultyFromInt(int difficulty)
        {
            string diff = "Standard";

            switch (difficulty)
            {
                case 0:
                    diff = "Standard";
                    break;
                case 1:
                    diff = "Comfort";
                    break;
                case 2:
                    diff = "Realistic";
                    break;
                default:
                    diff = "Custom";
                    break;
            }
            return diff;
        }

        public static int GetGameModeFromString(string difficulty)
        {
            int diff = 0;

            switch (difficulty)
            {
                case "Career":
                    diff = 0;
                    break;
                case "Sandbox":
                    diff = 1;
                    break;
                case "Scenario":
                    diff = 2;
                    break;
            }
            return diff;
        }

        public static string GetGameModeFromInt(int difficulty)
        {
            string diff = "Career";

            switch (difficulty)
            {
                case 0:
                    diff = "Career";
                    break;
                case 1:
                    diff = "Sandbox";
                    break;
                case 2:
                    diff = "Scenario";
                    break;
            }
            return diff;
        }

    }
}
