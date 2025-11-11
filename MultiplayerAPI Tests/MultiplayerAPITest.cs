using HarmonyLib;
using JetBrains.Annotations;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;
using MultiplayerAPITest.TestComponents;
using System;
using UnityEngine;
using UnityModManagerNet;

namespace MultiplayerAPITest;

public static class MultiplayerAPITest
{
    public static UnityModManager.ModEntry ModEntry;

    [UsedImplicitly]
    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        ModEntry = modEntry;
        //Settings = Settings.Load(modEntry);
        //ModEntry.OnGUI = Settings.Draw;
        //ModEntry.OnSaveGUI = Settings.Save;
        ModEntry.OnLateUpdate = LateUpdate;

        Harmony harmony = null;

        try
        {
            Log($"Multiplayer Mod is loaded: {MultiplayerAPI.IsMultiplayerLoaded}");

            Log("Patching...");
            harmony = new Harmony(ModEntry.Info.Id);
            harmony.PatchAll();

            Log("Loaded!");
        }
        catch (Exception ex)
        {
            LogException("Failed to load:", ex);
            harmony?.UnpatchAll();
            return false;
        }

        return true;
    }

    
    private static void LateUpdate(UnityModManager.ModEntry modEntry, float dt)
    {
        //Mod loading order can't be guaranteed, so we should wait for all mods to load prior to checking for multiplayer.
        //Alternatively, set 'Multiplayer' in the 'LoadAfter' parameter in your 'info.json'
        Log($"MultiplayerAPITest.LateUpdate() Multiplayer Mod is loaded: {MultiplayerAPI.IsMultiplayerLoaded}, API Version: {MultiplayerAPI.LoadedApiVersion} ");
        if (MultiplayerAPI.IsMultiplayerLoaded)
        {
            //Register that this mod needs to be installed on both server and client
            MultiplayerAPI.Instance.SetModCompatibility(ModEntry.Info.Id, MultiplayerCompatibility.All);

            // Register for server start and client start events
            // Note: for a non dedicated server both server and client events will be fired
            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ClientStarted += OnClientStarted;
        }

        modEntry.OnLateUpdate -= LateUpdate;
    }

    private static void OnServerStarted(IServer server)
    {
        // How you handle the server starting is up to you
        // In this test/example mod we are injecting a server manager into the scene, but you could
        // also just integrate it into your mod's existing workflow.
        // Keep in mind on a non-dedicated server, both the client and server will run concurrently
        GameObject go = new GameObject("MPAPI ServerTest", [typeof(ServerTest)]);
        GameObject.DontDestroyOnLoad(go);
    }

    private static void OnClientStarted(IClient client)
    {
        // How you handle the client starting is up to you
        // In this test/example mod we are injecting a client manager into the scene, but you could
        // also just integrate it into your mod's existing workflow.
        // Keep in mind on a non-dedicated host, both the client and server will run concurrently
        GameObject go = new GameObject("MPAPI ClientTest", [typeof(ClientTest)]);
        GameObject.DontDestroyOnLoad(go);
    }

    #region Logging

    public static void LogDebug(Func<object> resolver)
    {
        //if (!Settings.DebugLogging)
        //    return;
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
        //if (Settings.EnableLogFile)
        //    File.AppendAllLines(LOG_FILE, new[] { str });
        ModEntry.Logger.Log(str);
    }

    #endregion
}
