using DV;
using DV.UIFramework;
using HarmonyLib;
using JetBrains.Annotations;
using LiteNetLib;
using MPAPI;
using Multiplayer.API;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Editor;
using Multiplayer.Patches.Mods;
using Multiplayer.Patches.World;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityChan;
using UnityEngine;
using UnityModManagerNet;

namespace Multiplayer;

public static class Multiplayer
{
    private const string LOG_FILE = "multiplayer.log";

    public static UnityModManager.ModEntry ModEntry;
    public static Settings Settings;
    private static APIProvider _apiProvider;

    private static AssetBundle assetBundle;
    public static AssetIndex AssetIndex { get; private set; }
    public static string Ver {
        get {
            AssemblyInformationalVersionAttribute info = (AssemblyInformationalVersionAttribute)typeof(Multiplayer).Assembly.
                                                            GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                                                            .FirstOrDefault();

            if (info == null || Settings.ForceJson)
                return ModEntry.Info.Version;

            return info.InformationalVersion.Split('+')[0];
        }
    }

    public static string LocalBuildInfo => BuildInfo.BUILD_VERSION_MAJOR.ToString() + " - " + BuildInfo.BUILDBOT_INFO;


    public static bool specLog = false;

    [UsedImplicitly]
    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        ModEntry = modEntry;
        Settings = Settings.Load(modEntry);//Settings.Load<Settings>(modEntry);
        ModEntry.OnGUI = Settings.Draw;
        ModEntry.OnSaveGUI = Settings.Save;
        ModEntry.OnLateUpdate = LateUpdate;

        Harmony harmony = null;

        try
        {
            File.Delete(LOG_FILE);

            Locale.Load(ModEntry.Path);

            var gameVer = BuildInfo.BUILD_VERSION_MAJOR.ToString() +
                (string.IsNullOrEmpty(BuildInfo.BUILD_VERSION_SUFFIX) ? "" : "." + BuildInfo.BUILD_VERSION_SUFFIX);

            bool APIcompatible = false;
            if (Version.TryParse(APIProvider.BUILT_AGAINST_API_VERSION, out var builtVerAPI) && Version.TryParse(MultiplayerAPI.LoadedApiVersion, out var loadedVerAPI))
            {
                APIcompatible = loadedVerAPI >= builtVerAPI;
            }

            Log($"\r\n\r\n" +
                $"\tMultiplayer JSON Version: {ModEntry.Info.Version}, Internal Version: {Ver}\r\n" +
                $"\tGame Version: {gameVer}\r\n" +
                $"\tBuildbot Version: {BuildInfo.BUILDBOT_INFO.ToString()}\r\n" +
                $"\tLiteNetLib Version: {LiteNetLibVer()}\r\n" +
                $"\tMultiplayer API Required Version: {APIProvider.BUILT_AGAINST_API_VERSION}, Loaded Version: {MultiplayerAPI.LoadedApiVersion}\r\n" +
                $"\tMultiplayer API Compatible: {APIcompatible}\r\n");

            if (!APIcompatible)
            {
                throw new Exception("Multiplayer API version mismatch! One or more mods are using a newer version of the Multiplayer API, please update Multiplayer Mod or disable these mods.\r\n");
            }

            Log("Patching...");
            harmony = new Harmony(ModEntry.Info.Id);
#if DEBUG
            Harmony.DEBUG = true;
#endif
            harmony.PatchAll();
            SimComponent_Tick_Patch.Patch(harmony);

            UnityModManager.ModEntry remoteDispatch = UnityModManager.FindMod("RemoteDispatch");
            if (remoteDispatch?.Enabled == true)
            {
                Log("Found RemoteDispatch, patching...");
                RemoteDispatchPatch.Patch(harmony, remoteDispatch.Assembly);
            }

            if (!LoadAssets())
                return false;

            if (typeof(AutoBlink).IsClass)
            {
                // Ensure the UnityChan assembly gets loaded.
            }

            Log("Creating NetworkManager...");
            NetworkLifecycle.CreateLifecycle();

            Log("Loading Compatibility Manager...");
            ModCompatibilityManager.Instance.CheckInstance();

            Log("Loading API Provider...");
            _apiProvider = new APIProvider();
            MultiplayerAPI.RegisterAPI(_apiProvider);

#if DEBUG
            CheckPatches();
#endif

        }
        catch (Exception ex)
        {
            LogException("Failed to load:", ex);
            harmony?.UnpatchAll();
            return false;
        }

        return true;
    }

    public static bool LoadAssets()
    {
        if (assetBundle != null)
        {
            LogDebug(() => "Asset Bundle is still loaded, skipping loading it again.");
            return true;
        }

        Log("Loading AssetBundle...");
        string assetBundlePath = Path.Combine(ModEntry.Path, "multiplayer.assetbundle");
        if (!File.Exists(assetBundlePath))
        {
            LogError($"AssetBundle not found at '{assetBundlePath}'!");
            return false;
        }

        assetBundle = AssetBundle.LoadFromFile(assetBundlePath);
        AssetIndex[] indices = assetBundle.LoadAllAssets<AssetIndex>();
        if (indices.Length != 1)
        {
            LogError("Expected exactly one AssetIndex in the AssetBundle!");
            return false;
        }

        AssetIndex = indices[0];
        return true;
    }

    private static void LateUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
    {
        if (ModEntry.NewestVersion != null && ModEntry.NewestVersion.ToString() != "")
        {
#if DEBUG
            CheckPatches();
#endif
            Log($"Multiplayer Latest Version: {ModEntry.NewestVersion}");

            ModEntry.OnLateUpdate -= Multiplayer.LateUpdate;

            if (ModEntry.NewestVersion > ModEntry.Version)
            {
                if (MainMenuThingsAndStuff.Instance != null)
                {
                    Popup update =  MainMenuThingsAndStuff.Instance.ShowOkPopup();

                    if (update == null)
                        return;

                    /*
                    update.labelTMPro.text = "Multiplayer Mod Update Available!\r\n\r\n"+
                                                $"<align=left>Latest version:\t\t{ModEntry.NewestVersion}\r\n" +
                                                $"Installed version:\t\t<color=\"red\">{ModEntry.Version}</color>\r\n\r\n" +
                                                "Run Unity Mod Manager Installer to apply the update.</align>";
                    */

                    var latestVer = Locale.Get(Locale.MAIN_MENU__UPDATE_LATEST_KEY, [$"\t\t{ModEntry.NewestVersion}"]);
                    var installedVer = Locale.Get(Locale.MAIN_MENU__UPDATE_INSTALLED_KEY, [$"\t\t<color=\"red\">{ModEntry.Version}</color>"]);

                    update.labelTMPro.text = Locale.MAIN_MENU__UPDATE_TITLE +
                                             $"\r\n\r\n<align=left>{latestVer}" +
                                             $"\r\n{installedVer}\r\n\r\n" +
                                             $"{Locale.MAIN_MENU__UPDATE_ACTION}</align>";

                    Vector3 currPos = update.labelTMPro.transform.localPosition;
                    Vector2 size = update.labelTMPro.rectTransform.sizeDelta;

                    float delta = size.y - update.labelTMPro.preferredHeight;
                    currPos.y -= delta *2 ;
                    size.y = update.labelTMPro.preferredHeight;

                    update.labelTMPro.transform.localPosition = currPos;
                    update.labelTMPro.rectTransform.sizeDelta = size;

                    currPos = update.positiveButton.transform.localPosition;
                    currPos.y += delta * 2;
                    update.positiveButton.transform.localPosition = currPos;


                }
            }
        }
    }

    static string LiteNetLibVer()
    {
        Assembly liteNetLibAssembly = typeof(NetManager).Assembly;
        AssemblyName assemblyName = liteNetLibAssembly.GetName();

        return assemblyName.Version.ToString();
    }
#if DEBUG
    public static void CheckPatches()
    {
        StringBuilder sb = new StringBuilder("Harmony patches:");
        foreach (var info in Harmony.GetAllPatchedMethods())
        {
            var patches = Harmony.GetPatchInfo(info);
            sb.Append($"\r\n- {info.DeclaringType.FullName}.{info.Name} patched by:");
            foreach (var p in patches.Prefixes)
                sb.Append($"\r\n  - Prefix: {p.PatchMethod.DeclaringType.FullName}.{p.PatchMethod.Name}");
            foreach (var p in patches.Postfixes)
                sb.Append($"\r\n  - Postfix: {p.PatchMethod.DeclaringType.FullName}.{p.PatchMethod.Name}");
        }

        LogDebug(()=>sb.ToString());
    }
#endif


    #region Logging

    public static void LogDebug(Func<object> resolver)
    {
        if (!Settings.DebugLogging)
            return;
        WriteLog($"[Debug] {resolver.Invoke()}");
    }

    public static void Log(object msg)
    {
        WriteLog($"[Info] {msg}");
    }

    public static void LogWarning(object msg)
    {
        WriteLog($"[Warning] {msg}");
    }

    public static void LogError(object msg)
    {
        WriteLog($"[Error] {msg}");
    }

    public static void LogException(object msg, Exception e)
    {
        ModEntry.Logger.LogException($"{msg}", e);
    }

    private static void WriteLog(string msg)
    {
        string str = $"[{DateTime.Now.ToUniversalTime():HH:mm:ss.fff}] {msg}";
        if (Settings.EnableLogFile)
            File.AppendAllLines(LOG_FILE, new[] { str });
        ModEntry.Logger.Log(str);
    }

    #endregion
}
