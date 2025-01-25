using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(RespawnOnDrop))]
[HarmonyPatch("RespawnOrDestroy")]
[HarmonyPatch(MethodType.Enumerator)]
class RespawnOnDropPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        return codes; //disable pactch temporarily

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Pre-patch:");
        foreach (var code in codes)
        {
            sb.AppendLine(code.ToString());
        }

        Debug.Log(sb.ToString());

        // Find base.gameObject.SetActive(false)
        //  ldloc.1 NULL[Label10]                                                           //this is the 'base' loading to the stack
        //  call UnityEngine.GameObject UnityEngine.Component::get_gameObject()             //call to retrieve the gameObject
        //  ldc.i4.0 NULL                                                                   //load a 'false' onto the stack
        //  callvirt System.Void UnityEngine.GameObject::SetActive(System.Boolean value)    //call to SetActive()

        int startIndex = -1;
        for (int i = 0; i < codes.Count - 1; i++)
        {
            if (codes[i].opcode == OpCodes.Ldloc_1 &&
                codes[i + 1].Calls(AccessTools.Method(typeof(Component), "get_gameObject")) &&
                codes[i + 2].opcode == OpCodes.Ldc_I4_0 &&
                codes[i + 3].Calls(AccessTools.Method(typeof(GameObject), "SetActive")))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
        {
            Multiplayer.LogError(() => $"RespawnOnDrop.RespawnOrDestroy() transpiler failed - start index not found!");
            return codes.AsEnumerable();
        }

        // Find SingletonBehaviour<StorageController>.Instance.AddItemToLostAndFound(this.item);
        int endIndex = codes.FindIndex(startIndex, x =>
            x.Calls(AccessTools.Method(typeof(StorageController), "AddItemToLostAndFound")));


        if (endIndex < 0)
        {
            Multiplayer.LogError(() => $"RespawnOnDrop.RespawnOrDestroy() transpiler failed - end index not found!");
            return codes.AsEnumerable();
        }


        // replace 'else' branch with NOPs rather than trying to patch labels
        for (int i = startIndex; i <= endIndex; i++)
        {
            var newNop = new CodeInstruction(OpCodes.Nop);
            newNop.labels.AddRange(codes[i].labels);  // Maintain any labels on the original instruction
            codes[i] = newNop;
        }

        sb = new StringBuilder();
        sb.AppendLine("Post-patch:");
        foreach (var code in codes)
        {
            sb.AppendLine(code.ToString());
        }

        Debug.Log(sb.ToString());

        return codes.AsEnumerable();
    }
}

