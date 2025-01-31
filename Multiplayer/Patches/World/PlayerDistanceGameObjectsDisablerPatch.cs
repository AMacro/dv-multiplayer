using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch]
public static class PlayerDistanceGameObjectsDisablerPatch
{
    const int SKIPS = 2;
    static readonly CodeInstruction targetMethod = CodeInstruction.Call(typeof(Vector3), "op_Subtraction", [typeof(Vector3), typeof(Vector3)], null);
    static readonly CodeInstruction newMethod = CodeInstruction.Call(typeof(PlayerDistanceGameObjectsDisablerPatch), nameof(CustomCalcSqrMagnitude), [typeof(Vector3), typeof(Vector3), typeof(PlayerDistanceGameObjectsDisabler)], null);


    public static IEnumerable<MethodBase> TargetMethods()
    {
        //We're targeting an 'IEnumerable'; this gets compiled as a state machine with
        //a method per state.
        //Find all of the resultant states that are a 'MoveNext', these are the methods we need to patch.
        //Doing this dynamically reduces the chance a game update breaks the transpiler
        return typeof(PlayerDistanceGameObjectsDisabler)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(t => t.Name.StartsWith("<GameObjectsDistanceCheck>"))
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(m => m.Name == "MoveNext");
    }


    /*
     * We want to find the call to Vector3 subtraction `(optimizingGameObjects[i].transform.position - position)`
     * (found on line 79 of the IL code) and replace it with an instruction
     * that loads the current instance "this" to the stack.
     * we want to override line 80 so it calls our custom method `CustomCalcSqrMagnitude()`
     * Lines 81 and 82 are not required and need to be NOP'd out
     * This pattern is used again in the re-enable check (lines 104 - 115)
 
        74	00D6	ldfld int32 PlayerDistanceGameObjectsDisabler/'<GameObjectsDistanceCheck>d__6'::'<i>5__2'
        75	00DB callvirt    instance !0 class [mscorlib] System.Collections.Generic.List`1<class [UnityEngine.CoreModule] UnityEngine.GameObject>::get_Item(int32)
        76	00E0	callvirt instance class [UnityEngine.CoreModule]
            UnityEngine.Transform[UnityEngine.CoreModule] UnityEngine.GameObject::get_transform()
        77	00E5	callvirt instance valuetype[UnityEngine.CoreModule] UnityEngine.Vector3 [UnityEngine.CoreModule] UnityEngine.Transform::get_position()
        78	00EA ldloc.2 //parameter for the position of the player's camera

            //overwrite line 79 with ldloc.1 (pass in 'this' as the final parameter of call to CustomCalcSqrMagnitude())
        79	00EB call    valuetype[UnityEngine.CoreModule] UnityEngine.Vector3[UnityEngine.CoreModule] UnityEngine.Vector3::op_Subtraction(valuetype[UnityEngine.CoreModule] UnityEngine.Vector3, valuetype[UnityEngine.CoreModule] UnityEngine.Vector3)
            //overwrite with call to CustomCalcSqrMagnitude() (techinically we are inserting the call and skipping thr original)
            //Insert 3 NOPs
        80	00F0	stloc.3         //skip 0
        81	00F1	ldloca.s V_3(3) //skip 1
        82	00F3	call instance float32[UnityEngine.CoreModule] UnityEngine.Vector3::get_sqrMagnitude() //Skip 2
        83	00F8	ldloc.1
        84	00F9	ldfld float32 PlayerDistanceGameObjectsDisabler::disableSqrDistance
        85	00FE ble.un.s    94 (0119) ldloc.1

     */
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> GameObjectsDistanceCheck(IEnumerable<CodeInstruction> instructions)
    {
        //Multiplayer.LogDebug(() =>
        //{
        //    var code = new List<CodeInstruction>(instructions);

        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine("Starting transpiler");
        //    sb.AppendLine("IL Before:");
        //    for (int i = 0; i < code.Count; i++)
        //        sb.AppendLine($"{i:D4}: {code[i]}");

        //    return sb.ToString();
        //});

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
                skipCtr = 0;    //reset as there are 2 identical sections to the code to be patched.
                skipFlag = true;
            }
            else if (skipFlag)
            {
                if (skipCtr == SKIPS)
                {
                    skipFlag = false; //stop skipping
                    continue;
                }
                skipCtr++;
            }
            else
                newCode.Add(instruction);
        }

        //Multiplayer.LogDebug(() =>
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine("IL After:");
        //    for (int i = 0; i < newCode.Count; i++)
        //        sb.AppendLine($"{i:D4}: {newCode[i]}");

        //    return sb.ToString();
        //});

        return newCode;
    }


    public static float CustomCalcSqrMagnitude(Vector3 vecA, Vector3 vecB, PlayerDistanceGameObjectsDisabler instance)
    {
        //At present we only need to target instsances of `PlayerDistanceGameObjectsDisabler`
        //that are on the 'RefillStations' game object, as this is managing Pit Stop stations
        //we need these to be active on the host when any player is nearby.
        if (instance.gameObject.name == "RefillStations" && NetworkLifecycle.Instance.IsHost())
        {
            //Multiplayer.LogDebug(() =>$"CustomCalcSqrMagnitude({instance?.gameObject?.name}, {vecA}, {vecB}) Camera pos: {PlayerManager.ActiveCamera.transform.position}");
            return vecA.AnyPlayerSqrMag();
        }

        return (vecA - vecB).sqrMagnitude;
    }
}
