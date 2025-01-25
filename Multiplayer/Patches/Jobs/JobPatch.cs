using DV.Interaction;
using DV.Logic.Job;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Patches.Jobs;

//[HarmonyPatch(typeof(Job), nameof(Job.ExpireJob))]
//public static class JobPatch
//{
//    private static bool Prefix(Job __instance)
//    {
//        Multiplayer.LogWarning($"Trying to expire {__instance.ID}\r\n"+ new System.Diagnostics.StackTrace());
//        return false;
//    }
//}

