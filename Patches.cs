using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using System.Collections;

namespace NoBlockDetach
{
    internal static class Patches
    {
        public static bool Debug = true;

        /* Override check on if block detaches from damage */
        [HarmonyPatch(typeof(ModuleDamage))]
        [HarmonyPatch("CheckAndHandleDetatch")]
        public static class PatchDetachCheck
        {
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        // patch fragility
        [HarmonyPatch(typeof(ModuleDamage))]
        [HarmonyPatch("OnSpawn")]
        public static class PatchDamageFragility
        {
            public static void Postfix(ref ModuleDamage __instance)
            {
                __instance.m_DamageDetachFragility = 0.0f;
                return;
            }
        }
    }
}
