using Multiplayer.Components.MainMenu;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Networking.Data
{
    public class LobbyServerUpdateData
    {
        public string game_server_id { get; set; }

        public string private_key { get; set; }

        [JsonProperty("time_passed")]
        public string TimePassed { get; set; }


        [JsonProperty("current_players")]
        public int CurrentPlayers { get; set; }


        public LobbyServerUpdateData(string game_server_id, string private_key, string timePassed,int currentPlayers)
        {
            this.game_server_id = game_server_id;
            this.private_key = private_key;
            this.TimePassed = timePassed;
            this.CurrentPlayers = currentPlayers;
        }



    }
}
