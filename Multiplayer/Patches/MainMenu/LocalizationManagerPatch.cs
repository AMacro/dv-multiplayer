using HarmonyLib;
using I2.Loc;

namespace Multiplayer.Patches.MainMenu;

[HarmonyPatch(typeof(LocalizationManager))]
public static class LocalizationManagerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(LocalizationManager.TryGetTranslation))]
    private static bool TryGetTranslation_Prefix(ref bool __result, string Term, out string Translation)
    {
        Translation = string.Empty;
        if (!Term.StartsWith(Locale.PREFIX))
            return true;
        Translation = Locale.Get(Term);
        __result = Translation == Locale.MISSING_TRANSLATION;
        return false;
    }
}
