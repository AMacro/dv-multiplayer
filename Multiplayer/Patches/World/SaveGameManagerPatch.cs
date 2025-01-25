using DV.Scenarios.Common;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Components.SaveGame;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.FindStartGameData))]
public static class SaveGameManager_FindStartGameData_Patch
{
    private static void Postfix(AStartGameData __result)
    {
        if (NetworkLifecycle.Instance.IsServerRunning || NetworkLifecycle.Instance.IsClientRunning)
            return;
        StartServer(__result.DifficultyToUse);
    }

    private static void StartServer(IDifficulty difficulty)
    {
        if (NetworkLifecycle.Instance.StartServer(difficulty))
            return;

        NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
        {
            Popup popup = MainMenuThingsAndStuff.Instance.ShowOkPopup();
            if (popup == null)
                return;
            popup.labelTMPro.text = "Failed to start server! Ensure that the port is not in use and try again.";
        });

        DV.UI.MainMenu.GoBackToMainMenu();
    }
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.SaveAllowed))]
public static class SaveGameManager_SaveAllowed_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!NetworkLifecycle.Instance.IsClientRunning || NetworkLifecycle.Instance.IsHost())
            return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.UpdateInternalData))]
public static class SaveGameManager_UpdateInternalData_Patch
{
    private static void Postfix(SaveGameManager __instance)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        NetworkedSaveGameManager.Instance.Server_UpdateInternalData(__instance.data);
    }
}
