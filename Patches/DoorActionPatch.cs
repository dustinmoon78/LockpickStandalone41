// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using LockpickStandalone.Data;
using LockpickStandalone.Compat;
using LockpickStandalone.Interaction;
using SPT.Reflection.Patching;
using EFT.UI;

namespace LockpickStandalone.Patches
{
    /// <summary>给「门」的可用交互列表补上撬锁（锚点与上游相同，代码为本模组自写）。</summary>
    internal sealed class DoorActionPatch : ModulePatch
    {
        private static string _lastMap;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionContextHelper), "GetAvailableActions", new Type[]
            {
                typeof(GamePlayerOwner),
                typeof(WorldInteractiveObject)
            });
        }

        [PatchPostfix]
        private static void Postfix(ref AvailableInteractionState __result, GamePlayerOwner owner, WorldInteractiveObject worldInteractiveObject)
        {
            if (__result == null || worldInteractiveObject == null) return;
            if (!LpConfig.Enabled.Value) return;
            try
            {
                if (Singleton<GameWorld>.Instance == null || Singleton<GameWorld>.Instance.MainPlayer == null) return;
                if (LockpickEnv.IsBotInteraction(owner)) return;
                SeCompat.EnsureTakeover();
                if (LpConfig.TakeOverSe.Value) SeCompat.StripForeign(__result);
                EnsureSession();
                LockpickInteractions.AddLockpick(worldInteractiveObject, __result, owner, LpConfig.DedupeActions.Value);
                if (__result.Actions.Count > 0) __result.Error = null;
            }
            catch (Exception ex)
            {
                if (LpLog.Verbose) LpLog.Error("[Lockpick] 门交互注入异常：" + ex.Message);
            }
        }

        /// <summary>地图变化即视为新战局：清空本场统计（不依赖任何游戏事件锚点）。</summary>
        private static void EnsureSession()
        {
            string map = LockpickEnv.CurrentMap();
            if (map == _lastMap) return;
            _lastMap = map;
            LockpickEnv.ResetSession();
            LpLog.Info("[Lockpick] 当前地图「" + map + "」" + (LpData.IsKnownMap(map) ? "已登记" : "未登记 -> 本图不提供开锁"));
        }
    }
}