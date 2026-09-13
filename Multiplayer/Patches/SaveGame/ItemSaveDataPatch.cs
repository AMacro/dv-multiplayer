using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Components.SaveGame;

namespace Multiplayer.Patches.SaveGame;

[HarmonyPatch(typeof(ItemSaveData), nameof(ItemSaveData.SaveItemData))]
internal static class ItemSaveDataPatch
{
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
