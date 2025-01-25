using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib.Utils;
using UnityModManagerNet;

namespace Multiplayer.Networking.Data;

[Serializable]
public readonly struct ModInfo
{
    public readonly string Id;
    public readonly string Version;
      
    public ModInfo(string id, string version)
    {
        Id = id;
        Version = version;
    }

    public override string ToString()
    {
        return $"{Id} v{Version}";
    }

    public static void Serialize(NetDataWriter writer, ModInfo modInfo)
    {
        writer.Put(modInfo.Id);
        writer.Put(modInfo.Version);
    }

    public static ModInfo Deserialize(NetDataReader reader)
    {
        return new ModInfo(reader.GetString(), reader.GetString());
    }

    public static ModInfo[] FromModEntries(IEnumerable<UnityModManager.ModEntry> modEntries)
    {
        return modEntries
            .Where(entry => entry.Enabled)  //We only care if it's enabled
            .OrderBy(entry => entry.Info.Id)
            .Select(entry => new ModInfo(entry.Info.Id, entry.Info.Version))
            .ToArray();
    }
}
