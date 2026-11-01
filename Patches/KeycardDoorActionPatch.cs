// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using LockpickStandalone.Interaction;
using SPT.Reflection.Patching;
using EFT.UI;

namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 门禁卡门：第三方模组给 WorldInteractiveObject 与 KeycardDoor 各挂了一个后置补丁，
    /// 而这两个重载共用**同一个** AvailableInteractionState（门禁卡版转发到 Door 版，Door 版再转发到
    /// WorldInteractiveObject 版并沿用它的 Actions 列表），于是同一条交互项被追加两次。
    /// 本补丁挂在整个链路的收口处，把连续重复的同名交互项收敛成一条。
    /// </summary>
    internal sealed class KeycardDoorActionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionContextHelper), "GetAvailableActions", new Type[]
            {
                typeof(GamePlayerOwner),
                typeof(KeycardDoor),
                typeof(bool)
            });
        }

        [PatchPostfix]
        private static void Postfix(ref AvailableInteractionState __result, GamePlayerOwner owner, bool isProxy)
        {
            if (__result == null || !LpConfig.Enabled.Value || !LpConfig.DedupeActions.Value) return;
            if (isProxy) return;   // 代理视角（准星打在门的代理碰撞体上）：不转发、也不会有重复项
            if (owner == null || owner.Player == null || !owner.Player.IsYourPlayer) return;   // 只动自己的交互列表
            try
            {
                LockpickInteractions.RemoveDuplicateActions(__result);
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 门禁卡门交互项去重异常：" + ex.Message);
            }
        }
    }
}
