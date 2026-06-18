using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace TraderGen.Client.Patches
{
    internal static class TraderPricePatches
    {
        internal static ManualLogSource Log;

        internal static void Init(ManualLogSource log) => Log = log;

        // Keep the Convert patch to reduce log noise from vanilla zero-price items
        [HarmonyPatch(typeof(Convert), nameof(Convert.ToInt32), new[] { typeof(double) })]
        internal static class ConvertToInt32Patch
        {
            static bool Prefix(double value, ref int __result)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value > int.MaxValue || value < int.MinValue)
                {
                    Log?.LogDebug($"[TraderGen] Convert.ToInt32 suppressed bad value ({value}), returning 1.");
                    __result = 1;
                    return false;
                }
                return true;
            }
        }
    }
}
