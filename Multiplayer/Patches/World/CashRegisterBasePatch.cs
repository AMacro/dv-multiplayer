using DV.CashRegister;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CashRegisterBase))]
public class CashRegisterBasePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterBase.SetCash))]
    private static void SetCash(CashRegisterBase __instance, double amount)
    {
        if (__instance is not CashRegisterWithModules cashRegisterWithModules)
            return;

        Multiplayer.LogDebug(() => $"SetCash() {__instance.GetObjectPath()}, Deposited: {amount}");

        if (!NetworkedCashRegisterWithModules.TryGet(cashRegisterWithModules, out var netCashRegister))
        {
            Multiplayer.LogWarning($"Attempting to SetCash, but NetworkedCashRegisterWithModules not found for {cashRegisterWithModules.GetObjectPath()}");
            return;
        }

        if (netCashRegister.IsShopRegister)
            return;

        netCashRegister.SetCash(amount);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterBase.OnEnable))]
    private static bool OnEnable(CashRegisterBase __instance)
    {
        //Multiplayer.LogDebug(() => $"CashRegisterBase.OnEnable({__instance.GetObjectPath()}) {__instance.GetType()}");
        if (__instance is not CashRegisterWithModules)
            return true;

        return NetworkLifecycle.Instance.IsHost();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterBase.OnDisable))]
    private static bool OnDisable(CashRegisterBase __instance)
    {
        //Multiplayer.LogDebug(() => $"CashRegisterBase.OnDisable({__instance.GetObjectPath()}) {__instance.GetType()}");
        if (__instance is not CashRegisterWithModules)
            return true;

        // Prevent clients from cancelling/returning cash on cash registers when loading the game or leaving the area
        __instance.StopAllCoroutines();
        return NetworkLifecycle.Instance.IsHost();
    }
}

[HarmonyPatch]
public class CashRegisterBaseReturnMoneyToPlayerCheckPatch
{
    const int TARGET_NOPS = 3;
    static readonly CodeInstruction targetMethod = CodeInstruction.Call(typeof(Vector3), "op_Subtraction", [typeof(Vector3), typeof(Vector3)], null);

    public static IEnumerable<MethodBase> TargetMethods()
    {
        //We're targeting an 'IEnumerable'; this gets compiled as a state machine with
        //a method per state.
        //Find all of the resultant states that are a 'MoveNext', these are the methods we need to patch.
        //Doing this dynamically reduces the chance a game update breaks the transpiler
        return typeof(CashRegisterBase)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(t => t.Name.StartsWith("<ReturnMoneyToPlayerCheck>"))
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(m => m.Name == "MoveNext");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int nopCtr = 0;
        bool foundEntry = false;

        List<CodeInstruction> newCode = [] ;

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand?.ToString() == targetMethod.operand?.ToString())
            {
                foundEntry = true;
                newCode.Add(CodeInstruction.Call(typeof(DvExtensions), nameof(DvExtensions.AnyPlayerSqrMag), [typeof(Vector3)], null)); //inject our method
            }
            else if (foundEntry && nopCtr < TARGET_NOPS)
            {
                nopCtr++;
                newCode.Add(new CodeInstruction(OpCodes.Nop));
            }
            else
            {
                newCode.Add(instruction);
            }
        }

        return newCode;
    }
}
