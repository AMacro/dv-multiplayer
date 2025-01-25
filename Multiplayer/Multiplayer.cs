using System;
using System.IO;
using HarmonyLib;
using JetBrains.Annotations;
using Multiplayer.Components.Networking;
using Multiplayer.Editor;
using Multiplayer.Patches.Mods;
using Multiplayer.Patches.World;
using UnityChan;
using UnityEngine;
using UnityModManagerNet;

namespace Multiplayer;

public static class Multiplayer
{
    private const string LOG_FILE = "multiplayer.log";

    private static UnityModManager.ModEntry ModEntry;
    public static Settings Settings;

    private static AssetBundle assetBundle;
    public static AssetIndex AssetIndex { get; private set; }

    [UsedImplicitly]
    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        ModEntry = modEntry;
        Settings = Settings.Load<Settings>(modEntry);
        ModEntry.OnGUI = Settings.Draw;
        ModEntry.OnSaveGUI = Settings.Save;

        Harmony harmony = null;

        try
        {
            File.Delete(LOG_FILE);

            Locale.Load(ModEntry.Path);

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
        string str = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        if (Settings.EnableLogFile)
            File.AppendAllLines(LOG_FILE, new[] { str });
        ModEntry.Logger.Log(str);
    }

    #endregion
}
