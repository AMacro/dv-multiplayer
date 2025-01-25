namespace Multiplayer.Networking.Data
{
    public class LobbyServerResponseData
    {
 
        public string game_server_id { get; set; }
        public string private_key { get; set; }

        public LobbyServerResponseData(string game_server_id, string private_key, bool? ipv4_request = null)
        {
            this.game_server_id = game_server_id;
            this.private_key = private_key;
        }
    }
}
