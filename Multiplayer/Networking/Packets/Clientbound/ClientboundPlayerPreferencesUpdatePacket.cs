using Multiplayer.Networking.Data.Player;
using System;
using System.Collections.Generic;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerPreferencesUpdatePacket
{
    public byte PlayerId { get; set; }
    public byte[] PreferenceKeys { get; set; }
    public string[] PreferenceValues { get; set; }

    public Dictionary<PlayerPreference, string> GetPreferencesDictionary()
    {
        var dict = new Dictionary<PlayerPreference, string>();
        for (int i = 0; i < PreferenceKeys.Length; i++)
            dict[(PlayerPreference)PreferenceKeys[i]] = PreferenceValues[i];

        return dict;
    }
}
