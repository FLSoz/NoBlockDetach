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

        private static void DebugPrint(String prefix, String contents)
        {
            if (Patches.Debug)
            {
                d.Log(prefix + contents);
            }
        }

        private static FieldInfo m_DetachCallback = typeof(BlockManager).GetField("m_DetachCallback", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo tank = typeof(BlockManager).GetField("tank", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo allBlocks = typeof(BlockManager).GetField("allBlocks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo m_RemoveBlockRecursionCounter = typeof(BlockManager).GetField("m_RemoveBlockRecursionCounter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static FieldInfo blockTable = typeof(BlockManager).GetField("blockTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo m_APbitfield = typeof(BlockManager).GetField("m_APbitfield", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo rootBlock = typeof(BlockManager).GetField("rootBlock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo _blockCentreBounds = typeof(BlockManager).GetField("_blockCentreBounds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo m_BlockTableCentre = typeof(BlockManager).GetField("m_BlockTableCentre", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static FieldInfo changed = typeof(BlockManager).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(field =>
                    field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                    (field.DeclaringType == typeof(BlockManager).GetProperty("changed").DeclaringType) &&
                    field.FieldType.IsAssignableFrom(typeof(BlockManager).GetProperty("changed").PropertyType) &&
                    field.Name.StartsWith("<" + typeof(BlockManager).GetProperty("changed").Name + ">")
                );
        private static FieldInfo BlockHash = typeof(BlockManager).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(field =>
                    field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                    (field.DeclaringType == typeof(BlockManager).GetProperty("BlockHash").DeclaringType) &&
                    field.FieldType.IsAssignableFrom(typeof(BlockManager).GetProperty("BlockHash").PropertyType) &&
                    field.Name.StartsWith("<" + typeof(BlockManager).GetProperty("BlockHash").Name + ">")
                );

        private static FieldInfo s_LastRemovedBlocks = typeof(BlockManager).GetField("s_LastRemovedBlocks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static FieldInfo s_ActiveMgr = typeof(BlockManager).GetField("s_ActiveMgr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static Bounds k_InvalidBounds = (Bounds)typeof(BlockManager).GetField("k_InvalidBounds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static void FixupBlockRefs(BlockManager __instance)
        {
            int num = 0;
            List<TankBlock> blockList = (List<TankBlock>)Patches.allBlocks.GetValue(__instance);
            for (int i = 0; i < blockList.Count; i++)
            {
                if (blockList[i].tank == (Tank)Patches.tank.GetValue(__instance))
                {
                    if (i != num)
                    {
                        blockList[num] = blockList[i];
                    }
                    num++;
                }
            }
            if (num != blockList.Count)
            {
                blockList.RemoveRange(num, blockList.Count - num);
            }
            // PatchBlockManager.m_allBlocks.SetValue(__instance, blockList);
        }
        private static void RemoveAllBlocks(BlockManager __instance, BlockManager.RemoveAllAction option)
        {
            Tank tank = (Tank)Patches.tank.GetValue(__instance);
            if (tank.rbody.isKinematic)
            {
                tank.rbody.isKinematic = false;
            }
            Patches.m_RemoveBlockRecursionCounter.SetValue(__instance, 1);
            List<TankBlock> allBlocks = (List<TankBlock>)Patches.allBlocks.GetValue(__instance);
            Action<Tank, TankBlock> DetachCallback = (Action<Tank, TankBlock>)Patches.m_DetachCallback.GetValue(__instance);

            foreach (TankBlock tankBlock in allBlocks)
            {
                bool flag = option != BlockManager.RemoveAllAction.Recycle;
                tankBlock.OnDetach(tank, flag, flag);
                tank.NotifyBlock(tankBlock, false);
                tankBlock.RemoveLinks(null, true);
                if (DetachCallback != null)
                {
                    DetachCallback(tank, tankBlock);
                }
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
            Patches.m_RemoveBlockRecursionCounter.SetValue(__instance, 0);
            allBlocks.Clear();

            TankBlock[,,] l_blockTable = (TankBlock[,,])Patches.blockTable.GetValue(__instance);
            byte[,,] l_m_APbitfield = (byte[,,])Patches.m_APbitfield.GetValue(__instance);

            Array.Clear(l_blockTable, 0, l_blockTable.Length);
            Array.Clear(l_m_APbitfield, 0, l_m_APbitfield.Length);
            Patches.changed.SetValue(__instance, false);
            Patches.rootBlock.SetValue(__instance, null);
            Patches._blockCentreBounds.SetValue(__instance, Patches.k_InvalidBounds);
        }
        private static void HostRemoveAllBlocks(BlockManager __instance, BlockManager.RemoveAllAction option)
        {
            d.Assert(ManNetwork.IsHost, "Can't call HostRemoveAllBlocks on client");
            Tank tank = (Tank)Patches.tank.GetValue(__instance);
            if (ManNetwork.IsNetworked && tank.netTech != null)
            {
                RemoveAllBlocksMessage message = new RemoveAllBlocksMessage
                {
                    m_Action = option
                };
                Singleton.Manager<ManNetwork>.inst.SendToAllExceptClient(-1, TTMsgType.RemoveAllBlocksFromTech, message, tank.netTech.netId, true);
                Patches.RemoveAllBlocks(__instance, option);
                return;
            }
            Patches.RemoveAllBlocks(__instance, option);
        }
        private static void AddLastRemovedBlock(BlockManager __instance, TankBlock removed)
        {
            List<TankBlock> lastRemovedBlocks = (List<TankBlock>)Patches.s_LastRemovedBlocks.GetValue(null);
            BlockManager activeMgr = (BlockManager)Patches.s_ActiveMgr.GetValue(null);

            if (activeMgr != __instance)
            {
                lastRemovedBlocks.Clear();
                Patches.s_ActiveMgr.SetValue(null, __instance);
            }
            lastRemovedBlocks.Add(removed);
        }

        // Handles destroying everything when tech cab is sniped
        [HarmonyPatch(typeof(BlockManager))]
        [HarmonyPatch("FixupAfterRemovingBlocks")]
        public static class PatchBlockManagerFixup
        {
            public static bool Prefix(ref BlockManager __instance, ref bool allowHeadlessTech, ref bool removeTechIfEmpty)
            {
                Tank tank = (Tank)Patches.tank.GetValue(__instance);
                Patches.FixupBlockRefs(__instance);
                if (!allowHeadlessTech && !tank.control.HasController && !tank.IsAnchored)
                {
                    Patches.HostRemoveAllBlocks(__instance, BlockManager.RemoveAllAction.ApplyPhysicsKick);
                }
                if (__instance.blockCount == 0)
                {
                    d.Assert(tank.blockman.blockCount == 0);
                    tank.EnableGravity = false;
                }
                return false;
            }
        }


        /* Don't change DetachSingleBlock, because it's used in DetachBlockAndRestructure
         * Make the change in Detach, which holds both */
        [HarmonyPatch(typeof(BlockManager))]
        [HarmonyPatch("DetachSingleBlock")]
        public static class PatchDetachSingleBlock
        {
            public static void Postfix(ref BlockManager __instance, ref TankBlock block, ref bool isPropogating, ref bool rootTransfer)
            {
                ManDamage.DamageInfo damageInfo = new ManDamage.DamageInfo(float.PositiveInfinity, ManDamage.DamageType.Standard, (Component)null, (Tank)null, block.transform.position, default(Vector3), 0.0f, 0.0f);
                block.visible.damageable.TryToDamage(damageInfo, true);
                return;
            }
        }

        /* [HarmonyPatch(typeof(BlockManager))]
        [HarmonyPatch("Detach")]
        public static class PatchDetach
        {
            public static void Postfix(ref ModuleDamage __instance, ref TankBlock block, ref bool allowHeadlessTech, ref bool rootTransfer, ref bool propogate, ref Action<Tank, TankBlock> detachCallback)
            {
                if (!propogate)
                {
                    block.trans.parent = null;
                    block.trans.Recycle(true);
                }
                return;
            }
        }

        [HarmonyPatch(typeof(BlockManager))]
        [HarmonyPatch("DetachBlockAndRestructure")]
        public static class PatchDetachRecurseBlock
        {
            public static void Postfix(ref ModuleDamage __instance, ref TankBlock block, ref bool rootTransfer, ref bool allowHeadlessTech, ref TechSplitNamer techSplitNamer)
            {
                block.trans.parent = null;
                block.trans.Recycle(true);
                return;
            }
        } */

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
