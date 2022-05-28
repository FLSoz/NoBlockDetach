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

        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo m_DetachCallback = typeof(BlockManager).GetField("m_DetachCallback",InstanceFlags);
        private static readonly FieldInfo tank = typeof(BlockManager).GetField("tank",InstanceFlags);
        private static readonly FieldInfo allBlocks = typeof(BlockManager).GetField("allBlocks",InstanceFlags);
        private static readonly FieldInfo m_RemoveBlockRecursionCounter = typeof(BlockManager).GetField("m_RemoveBlockRecursionCounter",InstanceFlags);

        private static readonly FieldInfo blockTable = typeof(BlockManager).GetField("blockTable",InstanceFlags);
        private static readonly FieldInfo m_APbitfield = typeof(BlockManager).GetField("m_APbitfield",InstanceFlags);
        private static readonly FieldInfo rootBlock = typeof(BlockManager).GetField("rootBlock",InstanceFlags);
        private static readonly FieldInfo _blockCentreBounds = typeof(BlockManager).GetField("_blockCentreBounds",InstanceFlags);

        private static readonly FieldInfo changed = typeof(BlockManager).GetFields(InstanceFlags).FirstOrDefault(field =>
                    field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                    (field.DeclaringType == typeof(BlockManager).GetProperty("changed").DeclaringType) &&
                    field.FieldType.IsAssignableFrom(typeof(BlockManager).GetProperty("changed").PropertyType) &&
                    field.Name.StartsWith("<" + typeof(BlockManager).GetProperty("changed").Name + ">")
                );
        private static readonly FieldInfo BlockHash = typeof(BlockManager).GetFields(InstanceFlags).FirstOrDefault(field =>
                    field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                    (field.DeclaringType == typeof(BlockManager).GetProperty("BlockHash").DeclaringType) &&
                    field.FieldType.IsAssignableFrom(typeof(BlockManager).GetProperty("BlockHash").PropertyType) &&
                    field.Name.StartsWith("<" + typeof(BlockManager).GetProperty("BlockHash").Name + ">")
                );
        private static readonly MethodInfo FixupBlockRefs = typeof(BlockManager).GetMethod("FixupBlockRefs", InstanceFlags);
        private static readonly MethodInfo HostRemoveAllBlocks = typeof(BlockManager).GetMethod("HostRemoveAllBlocks", InstanceFlags);

        private static Bounds k_InvalidBounds = (Bounds)typeof(BlockManager).GetField("k_InvalidBounds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static void RemoveAllBlocks(BlockManager __instance, BlockManager.RemoveAllAction option)
        {
            Tank tank = (Tank) Patches.tank.GetValue(__instance);
            if (tank.rbody.isKinematic)
            {
                tank.rbody.isKinematic = false;
            }
            m_RemoveBlockRecursionCounter.SetValue(__instance, 1);
            List<TankBlock> allBlocks = (List<TankBlock>) Patches.allBlocks.GetValue(__instance);
            Action<Tank, TankBlock> DetachCallback = (Action<Tank, TankBlock>) Patches.m_DetachCallback.GetValue(__instance);

            foreach (TankBlock tankBlock in allBlocks)
            {
                bool flag = option != BlockManager.RemoveAllAction.Recycle;
                tankBlock.OnDetach(tank, flag, flag);
                tank.NotifyBlock(tankBlock, false);
                tankBlock.RemoveLinks(null, true);
                DetachCallback?.Invoke(tank, tankBlock);
                switch (option)
                {
                    case BlockManager.RemoveAllAction.ApplyPhysicsKick:
                        tankBlock.trans.parent = null;
                        tankBlock.trans.Recycle(true);
                        break;
                    case BlockManager.RemoveAllAction.Recycle:
                        tankBlock.trans.Recycle(true);
                        break;
                    case BlockManager.RemoveAllAction.HandOff:
                        tankBlock.trans.parent = null;
                        tankBlock.trans.Recycle(true);
                        break;
                    default:
                        d.LogError("BlockManager.RemoveAllBlocks - No remove action found for " + option);
                        break;
                }
            }
            m_RemoveBlockRecursionCounter.SetValue(__instance, 0);
            allBlocks.Clear();

            TankBlock[,,] l_blockTable = (TankBlock[,,])blockTable.GetValue(__instance);
            byte[,,] l_m_APbitfield = (byte[,,])m_APbitfield.GetValue(__instance);

            Array.Clear(l_blockTable, 0, l_blockTable.Length);
            Array.Clear(l_m_APbitfield, 0, l_m_APbitfield.Length);
            changed.SetValue(__instance, false);
            rootBlock.SetValue(__instance, null);
            _blockCentreBounds.SetValue(__instance, k_InvalidBounds);
        }

        // Handles destroying everything when tech cab is sniped
        [HarmonyPatch(typeof(BlockManager), "FixupAfterRemovingBlocks")]
        public static class PatchBlockManagerFixup
        {
            public static bool Prefix(ref BlockManager __instance, ref bool allowHeadlessTech, ref bool removeTechIfEmpty)
            {
                Tank tank = (Tank) Patches.tank.GetValue(__instance);
                FixupBlockRefs.Invoke(__instance, null);
                if (!allowHeadlessTech && !tank.control.HasController && !tank.IsAnchored)
                {
                    HostRemoveAllBlocks.Invoke(__instance, new object[] { BlockManager.RemoveAllAction.ApplyPhysicsKick });
                }
                if (__instance.blockCount == 0)
                {
                    d.Assert(tank.blockman.blockCount == 0);
                    tank.EnableGravity = false;
                }
                return false;
            }
        }

        // Force every call to this to be the same for MP consistency
        [HarmonyPatch(typeof(BlockManager), "RemoveAllBlocks")]
        public static class PatchRemoveAllBlocks
        {
            public static bool Prefix(ref BlockManager __instance, ref BlockManager.RemoveAllAction option)
            {
                Patches.RemoveAllBlocks(__instance, option);
                return false;
            }
        }

        // If a block is detached because it can't reach any root, then kill it if it's from an enemy
        [HarmonyPatch(typeof(BlockManager), "PreRecurseActionRemove")]
        public static class PatchRecursiveDetach
        {
            public static void Postfix(ref TankBlock block)
            {
                if (!block.tank || !block.tank.ControllableByAnyPlayer)
                {
                    ManDamage.DamageInfo damageInfo = new ManDamage.DamageInfo(float.PositiveInfinity, ManDamage.DamageType.Standard, (Component)null, (Tank)null, block.transform.position, default(Vector3), 0.0f, 0.0f);
                    block.visible.damageable.TryToDamage(damageInfo, true);
                }
                return;
            }
        }
    }
}
