using System;
using System.Reflection;
using UnityModManagerNet;

namespace Multiplayer.ModCompatibility;

internal class SkinManager
{
    private const string SKIN_MANAGER_MOD_ID = "SkinManagerMod";
    private const string SKIN_MANAGER_TYPE_NAME = "SkinManagerMod.Patches.CarSpawnerPatches";
    public static bool SkinManagerLoaded { get; private set; } = false;
    private static MethodInfo BaseSpawnPatch;

    private static bool initialised = false;

    public static void Initialize()
    {
        if (initialised)
            return;

        initialised = true;
        UnityModManager.toggleModsListen += ModToggle;

        Initialize_Internal();
    }

    private static void ModToggle(UnityModManager.ModEntry modEntry, bool enabled)
    {
        if (modEntry.Info.Id != SKIN_MANAGER_MOD_ID)
            return;

        if (enabled)
            Initialize_Internal();
        else
            DeInitialize();
    }

    private static void Initialize_Internal()
    {
        SkinManagerLoaded = false;
        UnityModManager.ModEntry skinManager = UnityModManager.FindMod(SKIN_MANAGER_MOD_ID);

        if (skinManager == null || skinManager.Enabled == false)
            return;

        Multiplayer.Log("SkinManager mod found...");
        try
        {
            BaseSpawnPatch = skinManager.Assembly.GetType(SKIN_MANAGER_TYPE_NAME)?.GetMethod("BaseSpawn", BindingFlags.NonPublic | BindingFlags.Static);
            if (BaseSpawnPatch == null)
            {
                Multiplayer.LogWarning("SkinManager mod found, but BaseSpawn method was not found.");
                return;
            }

            Multiplayer.Log("SkinManager mod integration complete.");
            SkinManagerLoaded = true;
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"Error while integrating with SkinManager mod: {ex.Message}");
        }
    }

    private static void DeInitialize()
    {
        SkinManagerLoaded = false;
        BaseSpawnPatch = null;
    }

    public static void PrepareThemes(TrainCar trainCar)
    {
        if (!SkinManagerLoaded || BaseSpawnPatch == null)
            return;

        try
        {
            BaseSpawnPatch.Invoke(null, [trainCar, false]);
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"Unable to prepare themes for train car {trainCar?.ID}\r\n{ex.Message}");
        }
    }
}
