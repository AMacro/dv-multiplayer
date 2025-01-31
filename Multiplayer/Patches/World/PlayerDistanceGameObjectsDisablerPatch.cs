using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch]
public static class PlayerDistanceGameObjectsDisablerPatch
{
    const int SKIPS = 2;
    static readonly CodeInstruction targetMethod = CodeInstruction.Call(typeof(Vector3), "op_Subtraction", [typeof(Vector3), typeof(Vector3)], null);
    static readonly CodeInstruction newMethod = CodeInstruction.Call(typeof(PlayerDistanceGameObjectsDisablerPatch), nameof(CheckConditions), [typeof(Vector3), typeof(Vector3), typeof(PlayerDistanceGameObjectsDisabler)], null);


    public static IEnumerable<MethodBase> TargetMethods()
    {
        var stuff = typeof(PlayerDistanceGameObjectsDisabler)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(t => t.Name.StartsWith("<GameObjectsDistanceCheck>"))
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance));

        foreach (var method in stuff) 
        {
            Multiplayer.LogDebug(() => $"TargetMethods: {method.Name}");
        }

        return typeof(PlayerDistanceGameObjectsDisabler)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(t => t.Name.StartsWith("<GameObjectsDistanceCheck>"))
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(m => m.Name == "MoveNext");
    }


    /*
     * We want to find the subtraction (line 79) replace it with loading "this" to the stack
     * and then line 80 can call our custom function
     * Lines 81 and 82 are not required
     * This pattern is used again in the re-enable check (lines 104 - 115)
 
        74	00D6	ldfld int32 PlayerDistanceGameObjectsDisabler/'<GameObjectsDistanceCheck>d__6'::'<i>5__2'
        75	00DB callvirt    instance !0 class [mscorlib] System.Collections.Generic.List`1<class [UnityEngine.CoreModule] UnityEngine.GameObject>::get_Item(int32)
        76	00E0	callvirt instance class [UnityEngine.CoreModule]
            UnityEngine.Transform[UnityEngine.CoreModule] UnityEngine.GameObject::get_transform()
        77	00E5	callvirt instance valuetype[UnityEngine.CoreModule] UnityEngine.Vector3 [UnityEngine.CoreModule] UnityEngine.Transform::get_position()
        78	00EA ldloc.2 //parameter for the position of the object we're testing against

            //overwrite with ldloc.1 (pass in 'this' as the final parameter)
        79	00EB call    valuetype[UnityEngine.CoreModule] UnityEngine.Vector3[UnityEngine.CoreModule] UnityEngine.Vector3::op_Subtraction(valuetype[UnityEngine.CoreModule] UnityEngine.Vector3, valuetype[UnityEngine.CoreModule] UnityEngine.Vector3)
            //overwrite with call to CheckConditions()
            //Insert 3 NOPs
        80	00F0	stloc.3         //skip 0
        81	00F1	ldloca.s V_3(3) //skip 1
        82	00F3	call instance float32[UnityEngine.CoreModule] UnityEngine.Vector3::get_sqrMagnitude() //Skip 2
        83	00F8	ldloc.1
        84	00F9	ldfld float32 PlayerDistanceGameObjectsDisabler::disableSqrDistance
        85	00FE ble.un.s    94 (0119) ldloc.1
     */
    //[HarmonyPatch("GameObjectsDistanceCheck")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> GameObjectsDistanceCheck(IEnumerable<CodeInstruction> instructions)
    {
        Multiplayer.LogDebug(() => $"Starting transpiler");

        var code = new List<CodeInstruction>(instructions);
        Multiplayer.LogDebug(() => "IL Before:");
        for (int i = 0; i < code.Count; i++)
        {
            Multiplayer.LogDebug(() => $"{i:D4}: {code[i]}");
        }

        int skipCtr = 0;
        bool skipFlag = false;

        var newCode = new List<CodeInstruction>();

        foreach (CodeInstruction instruction in instructions)
        {
            Multiplayer.LogDebug(() => $"Checking instruction: {instruction}");
            if (instruction.opcode == OpCodes.Call && instruction.operand?.ToString() == targetMethod.operand?.ToString())
            {
                Multiplayer.LogDebug(() => "Found target method, replacing");
                newCode.Add(new CodeInstruction(OpCodes.Ldloc_1));
                newCode.Add(newMethod);                        //skip 0
                newCode.Add(new CodeInstruction(OpCodes.Nop)); //skip 1
                newCode.Add(new CodeInstruction(OpCodes.Nop)); //skip 2
                skipCtr = 0;
                skipFlag = true;
            }
            else if (skipFlag)
            {
                if (skipCtr == SKIPS)
                {
                    skipFlag = false;
                    continue;
                }
                skipCtr++;
            }
            else
                newCode.Add(instruction);
        }

        Multiplayer.LogDebug(() => "IL After:");
        for (int i = 0; i < newCode.Count; i++)
        {
            Multiplayer.LogDebug(() => $"{i:D4}: {newCode[i]}");
        }

        return newCode;
    }


    public static float CheckConditions(Vector3 vecA, Vector3 vecB, PlayerDistanceGameObjectsDisabler instance)
    {
        if (instance.gameObject.name == "RefillStations" && NetworkLifecycle.Instance.IsHost())
        {
            //Multiplayer.LogDebug(() =>$"CheckConditions({instance?.gameObject?.name}, {vecA}, {vecB}) Camera pos: {PlayerManager.ActiveCamera.transform.position}");
            return vecA.AnyPlayerSqrMag();
        }

        return (vecA - vecB).sqrMagnitude;
    }
}
