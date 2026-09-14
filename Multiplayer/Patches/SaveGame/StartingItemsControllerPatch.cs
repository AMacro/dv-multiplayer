using HarmonyLib;

namespace Multiplayer.Patches.SaveGame;

[HarmonyPatch(typeof(StartingItemsController), "StartingItemsSafeguard")]
internal static class StartingItemsControllerSafeguardPatch
{
    private static bool Prefix(SaveGameData saveGameData)
    {
        bool authoritative = saveGameData?.GetBool("Multiplayer_AuthoritativeInventory") == true;
        bool returningPlayer = saveGameData?.GetBool("Multiplayer_HasSavedInventory") == true;

        // First-time multiplayer players receive the seven supplied tools plus
        // native essentials such as the radio and maps. Returning players use
        // their persisted record verbatim, including an intentionally empty one.
        return !authoritative || !returningPlayer;
    }
}
