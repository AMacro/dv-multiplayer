using HarmonyLib;
using I2.Loc;

namespace Multiplayer.Patches.MainMenu
{
    [HarmonyPatch(typeof(LocalizationManager))]
    public static class LocalizationManagerPatch
    {
        /// <summary>
        /// Harmony prefix patch for LocalizationManager.TryGetTranslation.
        /// </summary>
        /// <param name="__result">The result to be set by the prefix method.</param>
        /// <param name="Term">The localization term to be translated.</param>
        /// <param name="Translation">The translated text to be set by the prefix method.</param>
        /// <returns>False if the custom translation logic handles the term, otherwise true to continue to the original method.</returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(LocalizationManager.TryGetTranslation))]
        private static bool TryGetTranslation_Prefix(ref bool __result, string Term, out string Translation)
        {
            Translation = string.Empty;

            // Check if the term starts with the specified locale prefix
            if (!Term.StartsWith(Locale.PREFIX))
                return true;

            // Attempt to get the translation for the term
            Translation = Locale.Get(Term);

            // If the translation is missing, set the result to true and skip the original method
            __result = Translation == Locale.MISSING_TRANSLATION;
            return false;
        }
    }
}

