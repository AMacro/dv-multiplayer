using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DV;
using DV.UIFramework;
using HarmonyLib;
using JetBrains.Annotations;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Editor;
using Multiplayer.Patches.Mods;
using Multiplayer.Patches.World;
using UnityChan;
using UnityEngine;
using UnityModManagerNet;
using Steamworks;

namespace Multiplayer;

public static class Multiplayer
{
    private const string LOG_FILE = "multiplayer.log";

    public static UnityModManager.ModEntry ModEntry;
    public static Settings Settings;

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
    

    public static bool specLog = false;

    [UsedImplicitly]
    private static bool Load(UnityModManager.ModEntry modEntry)
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

            Log($"Multiplayer JSON Version: {ModEntry.Info.Version}, Internal Version: {Ver}\r\nGame version: {BuildInfo.BUILD_VERSION_MAJOR.ToString()}\r\nBuilbot version: {BuildInfo.BUILDBOT_INFO.ToString()}");

            Log("Patching...");
            harmony = new Harmony(ModEntry.Info.Id);
            harmony.PatchAll();
            SimComponent_Tick_Patch.Patch(harmony);

            UnityModManager.ModEntry remoteDispatch = UnityModManager.FindMod("RemoteDispatch");
            if (remoteDispatch?.Enabled == true)
            {
                Log("Found RemoteDispatch, patching...");
                RemoteDispatchPatch.Patch(harmony, remoteDispatch.Assembly);
            }

            //UnityModManager.ModEntry passengerJobs = UnityModManager.FindMod("PassengerJobs");
            //if (passengerJobs?.Enabled == true)
            //{
            //    Log("Found PassengerJobs, initialising...");
            //    PassengerJobsMod.Init();
            //}

            if (!LoadAssets())
                return false;

            if (typeof(AutoBlink).IsClass)
            {
                // Ensure the UnityChan assembly gets loaded.
            }

            Log("Creating NetworkManager...");
            NetworkLifecycle.CreateLifecycle();
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
            Log($"Multiplayer Latest Version: {ModEntry.NewestVersion}");

            ModEntry.OnLateUpdate -= Multiplayer.LateUpdate;

            if (ModEntry.NewestVersion > ModEntry.Version)
            {
                if (MainMenuThingsAndStuff.Instance != null)
                {
                    Popup update =  MainMenuThingsAndStuff.Instance.ShowOkPopup();

                    if (update == null)
                        return;

                    update.labelTMPro.text = "Multiplayer Mod Update Available!\r\n\r\n"+
                                                $"<align=left>Latest version:\t\t{ModEntry.NewestVersion}\r\n" +
                                                $"Installed version:\t\t<color=\"red\">{ModEntry.Version}</color>\r\n\r\n" +
                                                "Run Unity Mod Manager Installer to apply the update.</align>";

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
