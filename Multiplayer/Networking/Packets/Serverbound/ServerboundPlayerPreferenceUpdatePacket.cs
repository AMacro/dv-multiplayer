using Multiplayer.Networking.Data.Player;
using System.Collections.Generic;


namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundPlayerPreferenceUpdatePacket
{
    public byte[] PreferenceKeys { get; set; }
    public string[] PreferenceValues { get; set; }

    public Dictionary<PlayerPreference, string> GetPreferencesDictionary()
    {
        var preferences = new Dictionary<PlayerPreference, string>();
        for (int i = 0; i < PreferenceKeys.Length; i++)
        {
            preferences[(PlayerPreference)PreferenceKeys[i]] = PreferenceValues[i];
        }
        return preferences;
    }
}
