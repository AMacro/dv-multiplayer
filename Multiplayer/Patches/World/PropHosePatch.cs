using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(PropHose))]
public static class PropHosePatch
{
    static readonly CodeInstruction targetUnplugMethod = CodeInstruction.Call(typeof(PluggableObject), nameof(PluggableObject.Unplug), [], null);
    static readonly CodeInstruction override_UnplugMethod = CodeInstruction.Call(typeof(PropHosePatch), nameof(PropHosePatch.Override_Unplug), [typeof(PluggableObject)], null);

    static readonly CodeInstruction targetYankOutOfHandMethod = CodeInstruction.Call(typeof(PluggableObject), nameof(PluggableObject.YankOutOfHand), [], null);
    static readonly CodeInstruction override_YankOutOfHandMethod = CodeInstruction.Call(typeof(PropHosePatch), nameof(PropHosePatch.Override_YankOutOfHand), [typeof(PluggableObject)], null);

    static readonly CodeInstruction targetAddForceMethod = CodeInstruction.Call(typeof(Rigidbody), nameof(Rigidbody.AddForce), [typeof(Vector3), typeof(ForceMode)], null);
    static readonly CodeInstruction override_AddForceMethod = CodeInstruction.Call(typeof(PropHosePatch), nameof(PropHosePatch.Override_AddForce), [typeof(Rigidbody), typeof(Vector3), typeof(ForceMode), typeof(PluggableObject)], null);

    static readonly CodeInstruction targetInstantSnapToMethod = CodeInstruction.Call(typeof(PluggableObject), nameof(PluggableObject.InstantSnapTo), [typeof(PlugSocket)], null);
    static readonly CodeInstruction override_InstantSnapToMethod = CodeInstruction.Call(typeof(PropHosePatch), nameof(PropHosePatch.Override_InstantSnapTo), [typeof(PluggableObject), typeof(PlugSocket)], null);

    private static readonly string UnplugOperand = targetUnplugMethod.operand?.ToString();
    private static readonly string YankOutOfHandOperand = targetYankOutOfHandMethod.operand?.ToString();
    private static readonly string AddForceOperand = targetAddForceMethod.operand?.ToString();
    private static readonly string InstantSnapToOperand = targetInstantSnapToMethod.operand?.ToString();

    [HarmonyPatch(nameof(PropHose.OnEnable))]
    [HarmonyPrefix]
    public static bool OnEnable()
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        //prevent client from snapping manual service plug to home position
        return false;
    }

    [HarmonyPatch(nameof(PropHose.Update))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Update(IEnumerable<CodeInstruction> instructions)
    {
        var newCode = new List<CodeInstruction>();

        //Multiplayer.LogDebug(() =>
        //{
        //    var code = new List<CodeInstruction>(instructions);

        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine("Starting transpiler PropHose.Update");
        //    sb.AppendLine("IL Before:");
        //    for (int i = 0; i < code.Count; i++)
        //        sb.AppendLine($"{i:D4}: {code[i]}");
        //    return sb.ToString();
        //});


        foreach (CodeInstruction instruction in instructions)
        {

            if (instruction.opcode == OpCodes.Callvirt)
            {
                string operand = instruction.operand?.ToString();

                if (operand == UnplugOperand)
                {
                    //We are switching from a 'CallVirt' to a 'Call'.
                    //we already have a reference to PluggableObject on the stack as `this.plug` (first param of CallVirt)
                    //the next item on the stack is the PlugSocket

                    //Multiplayer.LogDebug(() => $"PropHose.Update() {instruction}, replacing: {override_UnplugMethod}");

                    //call our override method 
                    newCode.Add(override_UnplugMethod);

                }
                else if (operand == YankOutOfHandOperand)
                {
                    //We are switching from a 'CallVirt' to a 'Call'.
                    //we already have a reference to PluggableObject on the stack as `this.plug` (first param of CallVirt)
                    //the next item on the stack is the PlugSocket

                    //Multiplayer.LogDebug(() => $"PropHose.Update() {instruction}, replacing: {override_YankOutOfHandMethod}");

                    //call our override method 
                    newCode.Add(override_YankOutOfHandMethod);
                }
                else if (operand == AddForceOperand)
                {
                    //We are switching from a 'CallVirt' to a 'Call'.
                    //we already have a reference to rb on the stack as `this.plugBody` (first param of CallVirt)
                    //the next item on the stack is the force, then the mode
                    //we will manually add the plug instance

                    //Multiplayer.LogDebug(() => $"PropHose.Update() {instruction}, replacing: {override_AddForceMethod}");

                    //load instance/"this" to the stack
                    newCode.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    //load PropHose.plug reference on to the stack ("this.plug")
                    newCode.Add(new CodeInstruction(OpCodes.Ldfld, typeof(PropHose).GetField(nameof(PropHose.plug), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)));
                    //call our override method 
                    newCode.Add(override_AddForceMethod);

                }
                else if (operand == InstantSnapToOperand)
                {
                    //We are switching from a 'CallVirt' to a 'Call'.
                    //we already have a reference to PluggableObject on the stack as `this.plug` (first param of CallVirt)
                    //the next item on the stack is the PlugSocket

                    //Multiplayer.LogDebug(() => $"PropHose.Update() {instruction}, replacing: {override_InstantSnapToMethod}");

                    //call our override method 
                    newCode.Add(override_InstantSnapToMethod);
                }
                else
                {
                    //Multiplayer.LogDebug(() => $"PropHose.Update() {instruction}");
                    newCode.Add(instruction);
                }
            }
            else
            {
                //Multiplayer.LogDebug(() => $"PropHose.Update() {instruction}");
                newCode.Add(instruction);
            }
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

    private static void Override_Unplug(PluggableObject instance)
    {
        Multiplayer.LogDebug(() => $"Override_Unplug({instance.GetObjectPath()})");

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        Multiplayer.LogDebug(() => $"Override_Unplug({instance.GetObjectPath()}) Unplugging");
        instance.Unplug();
    }

    private static bool Override_YankOutOfHand(PluggableObject instance)
    {
        Multiplayer.LogDebug(() => $"Override_YankOutOfHand({instance.GetObjectPath()})");

        if (!NetworkLifecycle.Instance.IsHost())
            return false; // result is unused by Update(), we can return true or false

        if (NetworkedPluggableObject.Get(instance, out var netPlug))
            netPlug.DropPlug();

        Multiplayer.LogDebug(() => $"Override_YankOutOfHand({instance.GetObjectPath()}) Yanking");
        return instance.YankOutOfHand();
    }

    private static void Override_AddForce(Rigidbody rb, Vector3 force, ForceMode mode, PluggableObject instance)
    {
        Multiplayer.LogDebug(() => $"Override_AddForce() station: {instance.GetObjectPath()}, force: {force}, mode: {mode}");

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (NetworkedPluggableObject.Get(instance, out var netPlug))
        {
            Multiplayer.LogDebug(() => $"Override_AddForce() station: {netPlug.Station.StationName}, force: {force}, mode: {mode}");

            //The force will be applied when the packet is processed as the host
            //rb.AddForce(force, mode);
            netPlug.YankedByRope(force, mode);
        }
    }

    private static bool Override_InstantSnapTo(PluggableObject instance, PlugSocket socket)
    {
        Multiplayer.LogDebug(() => $"Override_InstantSnapTo({instance.GetObjectPath()}, {socket.GetObjectPath()}) instance.yankOutOfHand: {instance.yankOutOfHand}");

        if (!NetworkLifecycle.Instance.IsHost())
            return false; // result is unused by Update(), we can return true or false

        if(!instance.yankOutOfHand)
        {
            Multiplayer.LogDebug(() => $"Override_InstantSnapTo({instance.GetObjectPath()}, {socket.GetObjectPath()}) Blocked by yank settlement");
            return false;
        }

        Multiplayer.LogDebug(() => $"Override_InstantSnapTo({instance.GetObjectPath()}, {socket.GetObjectPath()}) Snapping");

        if (NetworkedPluggableObject.Get(instance, out var netPlug))
        {
            netPlug.SnappedByRope();
            return false;
        }

        // no player holding, we can allow the snap
        return instance.InstantSnapTo(socket);
    }
}
