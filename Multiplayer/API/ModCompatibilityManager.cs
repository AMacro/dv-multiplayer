using DV.JObjectExtstensions;
using DV.Utils;
using JetBrains.Annotations;
using MPAPI.Types;
using Multiplayer.Components.MainMenu;
using Multiplayer.Networking.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityModManagerNet;

namespace Multiplayer.API;

public class ModCompatibilityManager : SingletonBehaviour<ModCompatibilityManager>
{
    private const string JSON_FILE = "info.json";
    private const string JSON_MP_COMPAT_KEY = "MultiplayerCompatibility";
    private static readonly Dictionary<string, MultiplayerCompatibility> _modCompatibility = [];

    protected override void Awake()
    {
        base.Awake();

        DontDestroyOnLoad(this);

        //Register ourselves
        RegisterCompatibility("Multiplayer", MultiplayerCompatibility.All);

        //we don't care if the client does/doesn't have these mods
        RegisterCompatibility("RuntimeUnityEditor", MultiplayerCompatibility.Client);
        RegisterCompatibility("BookletOrganizer", MultiplayerCompatibility.Host);
        RegisterCompatibility("RemoteDispatch", MultiplayerCompatibility.Client);
        RegisterCompatibility("CommsRadioAPI", MultiplayerCompatibility.Client);
        RegisterCompatibility("DVCustomCargo", MultiplayerCompatibility.All);
        RegisterCompatibility("DVDiscordPresenceMod", MultiplayerCompatibility.Client);
        RegisterCompatibility("DVLangHelper", MultiplayerCompatibility.Client);
        RegisterCompatibility("LightingOverhaul", MultiplayerCompatibility.Client);

        //Json entries will override hardcoded entries
        ReadModJsons();

        //Hardcoded and json entries will be overridden by API calls
    }

    public void RegisterCompatibility(string modId, MultiplayerCompatibility compatibility)
    {
        Multiplayer.LogDebug(() => $"RegisterCompatibility({modId}, {compatibility})");

        if (!string.IsNullOrEmpty(modId))
            _modCompatibility[modId] = compatibility;
    }

    private void ReadModJsons()
    {
        foreach (var modEntry in UnityModManager.modEntries)
        {
            var jsonPath = Path.Combine(modEntry.Path, JSON_FILE);

            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath);
                    var jobj = JObject.Parse(json);
                    var compatStr = jobj.GetString(JSON_MP_COMPAT_KEY);

                    var parsed = Enum.TryParse(compatStr, out MultiplayerCompatibility compatibility);

                    Multiplayer.LogDebug(() => $"Mod '{modEntry.Info.DisplayName}' ({modEntry.Info.Id}) has MP mod compatibility entry \'{compatStr}\', parses to: {compatibility}");

                    if (parsed)
                        RegisterCompatibility(modEntry.Info.Id, compatibility);
                }
                catch (Exception e)
                {
                    Multiplayer.LogException($"Failed to parse mod entry {modEntry.Info.Id}", e);
                }
            }
            else
            {
                Multiplayer.LogWarning($"No json found for {modEntry.Info.Id}");
            }
        }
    }

    public bool TryGetCompatibility(string modId, out MultiplayerCompatibility compatibility)
    {
        return _modCompatibility.TryGetValue(modId, out compatibility);
    }

    public MultiplayerCompatibility GetCompatibility(ModInfo mod)
    {
        if (TryGetCompatibility(mod.Id, out var compatibility))
            return compatibility;

        return MultiplayerCompatibility.Undefined;
    }

    public ModValidationResult ValidateClientMods(ModInfo[] clientMods)
    {
        var localMods = GetLocalMods();
        var localModIds = localMods.Select(l => l.Id);

        var clientModIds = clientMods.Select(c => c.Id);

        List<ModInfo> missing = clientMods.Where(c => !localModIds.Contains(c.Id)).ToList();
        List<ModInfo> extra = localMods.Where(l => !clientModIds.Contains(l.Id)).ToList();

        bool valid = (missing.Count == 0) && (extra.Count == 0);

        return new()
        {
            IsValid = valid,
            Missing = missing,
            Extra = extra
        };
    }

    /// <summary>
    /// Checks if any incompatible mods are enabled and generates an message box to alert the user
    /// </summary>
    /// <returns>True if incompatible mods have been found</returns>
    public bool CheckModCompatibility()
    {
        List<string> incompatible = [];

        foreach (var modInfo in ModInfo.FromModEntries(UnityModManager.modEntries))
        {
            if (TryGetCompatibility(modInfo.Id, out var compatibility))
            {
                if (compatibility == MultiplayerCompatibility.Incompatible)
                    incompatible.Add(modInfo.Id);
            }
        }

        if (incompatible.Count == 0)
            return false;

        var message = $"{Locale.MAIN_MENU__INCOMPATIBLE_MODS} {string.Join(", ", incompatible)}";
        
        MainMenuThingsAndStuff.Instance.ShowOkPopup(message, () => { });

        return true;
    }

    // Returns a list of mods for use in the lobby data
    public string GetRequiredMods()
    {
        List<string> requiredMods = [];

        var local = GetLocalMods();

        if (local == null)
            return null;

        foreach (var modInfo in local)
            requiredMods.Add(modInfo.Id);

        return string.Join(", ", requiredMods);
    }

    //Returns a list of mods installed and enabled, filtered for mods that are required for hosts and clients
    public ModInfo[] GetLocalMods()
    {
        List<ModInfo> localMods = [];

        foreach (var modInfo in ModInfo.FromModEntries(UnityModManager.modEntries))
        {
            if (TryGetCompatibility(modInfo.Id, out var compatibility))
            {
                // Only include mods that are relevant for client validation
                switch (compatibility)
                {
                    case MultiplayerCompatibility.Undefined:
                    case MultiplayerCompatibility.All:
                        //undefined and "All" mods are required by all clients
                        localMods.Add(modInfo);
                        break;

                    case MultiplayerCompatibility.Incompatible:
                        //There shouldn't be any at this stage
                        return null;

                    case MultiplayerCompatibility.Host:
                    case MultiplayerCompatibility.Client:
                        // Not required, should have no impact on game play
                        break;
                }
            }
            else
            {
                // No compatibility info - include for validation (safe default)
                Multiplayer.LogWarning($"No compatibility info for mod {modInfo.Id}, including in validation");
                localMods.Add(modInfo);
            }
        }
        return localMods.ToArray();

    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(ModCompatibilityManager)}]";
    }
}

public class ModValidationResult
{
    public bool IsValid { get; set; }
    public List<ModInfo> Missing { get; set; } = [];
    public List<ModInfo> Extra { get; set; } = [];
}
