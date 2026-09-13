using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Multiplayer.Components.SaveGame;

namespace Multiplayer.Patches.SaveGame;

[HarmonyPatch(typeof(ItemSaveData), nameof(ItemSaveData.SaveItemData))]
internal static class ItemSaveDataPatch
{
    [HarmonyPostfix]
    private static void SaveOwner(ItemSaveData __instance, JObject __result) =>
        NativeItemPersistence.WriteOwner(__instance, __result);

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var invoke = AccessTools.Method(typeof(ItemSaveData.ItemSaveDataRequestedDelegate), "Invoke");
        var isolatedInvoke = AccessTools.Method(typeof(NativeItemPersistence), nameof(NativeItemPersistence.InvokeSaveListeners));
        int replacements = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(invoke))
            {
                // Both calls consume the existing delegate and JObject arguments.
                // Retain branch labels and exception markers on the instruction.
                instruction.opcode = OpCodes.Call;
                instruction.operand = isolatedInvoke;
                replacements++;
            }
            yield return instruction;
        }
        if (replacements != 1)
            throw new InvalidOperationException($"Expected one ItemSaveData callback invocation, found {replacements}");
    }
}

// LoadItemData stores the native JObject before invoking listeners. Reapply after
// post-load callbacks too, so initialization cannot replace the saved identity.
[HarmonyPatch(typeof(ItemSaveData))]
internal static class ItemOwnerLoadPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ItemSaveData.LoadItemData))]
    private static void RestoreOwner(ItemSaveData __instance) =>
        NativeItemPersistence.RestoreOwner(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ItemSaveData.PostLoadItemData))]
    private static void RestoreOwnerAfterInitialization(ItemSaveData __instance) =>
        NativeItemPersistence.RestoreOwner(__instance);
}
