using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

        // disable healing of dying blocks
        // This one still wastes energy trying to heal the dying blocks
        /*
        [HarmonyPatch(typeof(Damageable), "Repair")]
        public static class PatchHealing
        {
            public static bool Prefix(ref Damageable __instance)
            {
                if (__instance.Health <= 0.0f)
                {
                    return false;
                }
                return true;
            }
        }
        */

        // Patch repair bubble healing to no longer work on dead blocks
        // This one will not waste energy on trying to heal the dying blocks
        [HarmonyPatch(typeof(ModuleShieldGenerator), "HealContainedVisibles")]
        public static class PatchBlockResurrection
        {
            internal static PropertyInfo IsAtFullHealth = typeof(Damageable).GetProperty("IsAtFullHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            internal static MethodInfo GetIsAtFullHealth = IsAtFullHealth.GetGetMethod();
            internal static PropertyInfo Health = typeof(Damageable).GetProperty("Health", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            internal static MethodInfo GetHealth = Health.GetGetMethod();

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilgen)
            {
                Label myLabel = ilgen.DefineLabel();
                Label targetNextBlockIteration = ilgen.DefineLabel();

                List<CodeInstruction> originalInstructions = new List<CodeInstruction>(instructions);
                List<CodeInstruction> patchedInstructions = new List<CodeInstruction>();

                bool foundVar = false;
                object index = null;
                foreach (CodeInstruction instruction in originalInstructions)
                {
                    if (instruction.opcode == OpCodes.Ldloca_S)
                    {
                        if (!foundVar)
                        {
                            foundVar = true;
                            index = instruction.operand;
                        }
                        else if (instruction.operand == index)
                        {
                            Console.WriteLine("[NoBlockDetach] FOUND JUMP TARGET");
                            targetNextBlockIteration = instruction.labels[0];
                            break;
                        }
                    }
                }

                bool skipNext = false;
                for (int i = 0; i < originalInstructions.Count; i++)
                {
                    CodeInstruction instruction = originalInstructions[i];
                    if (skipNext)
                    {
                        skipNext = false;
                        continue;
                    }
                    else if (instruction.opcode == OpCodes.Ldloca_S && instruction.operand == index && instruction.labels.Count > 0 && instruction.labels[0] == targetNextBlockIteration)
                    {
                        Console.WriteLine("[NoBlockDetach] EDITING JUMP TARGET");
                        instruction.labels.Add(myLabel);
                        patchedInstructions.Add(instruction);
                        Console.WriteLine("[NoBlockDetach] EDITED JUMP TARGET");
                    }
                    else
                    {
                        patchedInstructions.Add(instruction);
                    }
                    if (instruction.opcode == OpCodes.Callvirt && (MethodInfo)instruction.operand == GetIsAtFullHealth)
                    {
                        Console.WriteLine("[NoBlockDetach] PATCHED RESURRECTION?");
                        skipNext = true;
                        // brtrue.s IL_014C;  Go to next item in loop
                        patchedInstructions.Add(originalInstructions[i+1]);
                        // Load damageable onto stack again
                        patchedInstructions.Add(originalInstructions[i-1].Clone());
                        // Get health
                        // patchedInstructions.Add(CodeInstruction.Call(typeof(Damageable), "get_Health"));
                        patchedInstructions.Add(new CodeInstruction(OpCodes.Callvirt, GetHealth));
                        // Compare it to 0. If health is > 0, then we proceed as normal. If health is <= 0, then we break
                        // Load 0.0f onto the stack
                        patchedInstructions.Add(new CodeInstruction(OpCodes.Ldc_R4, 0.0f));
                        // ble.un.s IL_014C;  Go to next item in loop
                        patchedInstructions.Add(new CodeInstruction(OpCodes.Ble_Un_S, myLabel));
                    }
                }
                Console.WriteLine("[NoBlockDetach] GOT TO RETURN FINE");
                return patchedInstructions;
            }
        }


        // If a block is detached because it can't reach any root, then kill it if it's from an enemy
        [HarmonyPatch(typeof(BlockManager), "PreRecurseActionRemove")]
        public static class PatchRecursiveDetach
        {
            public static void Prefix(ref TankBlock block, out bool __state)
            {
                if (Singleton.Manager<ManGameMode>.inst.GetCurrentGameType().IsCoOp())
                {
                    __state = false;
                }
                else
                {
                    __state = block.tank && block.tank.IsEnemy();
                }
                return;
            }

            public static void Postfix(ref TankBlock block, bool __state)
            {
                if (__state)
                {
                    ManDamage.DamageInfo damageInfo = new ManDamage.DamageInfo(float.PositiveInfinity, ManDamage.DamageType.Standard, (Component)null, (Tank)null, block.transform.position, default(Vector3), 0.0f, 0.0f);
                    block.visible.damageable.TryToDamage(damageInfo, true);
                }
            }
        }
    }
}
