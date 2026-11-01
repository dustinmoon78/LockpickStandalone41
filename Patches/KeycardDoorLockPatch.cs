// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;
using LockpickStandalone.Interaction;
using SPT.Reflection.Patching;

namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 门禁卡门（实验室卡门）按「开锁目标」接管。
    ///
    /// 【背景】
    /// EFT 原生的门禁卡门会遍历背包，把**每一张能开这扇门的卡**各列成一条交互项。
    /// 背包里有十张卡，菜单里就有十条几乎一样的条目，玩家要在一堆同义项里找。
    ///
    /// 【本补丁做什么】
    /// 打开 F12 的「6.门禁卡/按门禁锁处理」后，门禁卡门不再逐张列卡，
    /// 而是被当成一把普通的锁：只给一条「开锁」入口，后续的撬锁 / 骇入 / 无动作
    /// 由本模组自己的流程接管，与机械锁走同一套逻辑、同一套评级、同一份经验。
    ///
    /// 【为什么用 Postfix 而不是替换整个返回值】
    /// KeycardDoor 与 Door 共用同一个交互状态对象（门禁卡版转发到 Door 版），
    /// 直接替换 __result 会把机械锁那边已经算好的内容一并抹掉。
    /// 这里只把「逐张列卡」的那部分换成一条入口，其余原样保留，
    /// 与同文件里 KeycardDoorActionPatch 的去重思路一致但作用面不同。
    ///
    /// 【安全】
    /// 全部功能由 F12 开关控制且默认关闭；关闭时本补丁不打补丁，
    /// 门禁卡门保持 EFT 原生行为。作用域限定为自己的玩家、非代理视角。
    /// </summary>
    internal sealed class KeycardDoorLockPatch : ModulePatch
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
        private static void Postfix(ref AvailableInteractionState __result, GamePlayerOwner owner, KeycardDoor __1, bool isProxy)
        {
            // 开关默认关闭；关闭即完全不动门禁卡门
            if (__result == null || !LpConfig.Enabled.Value || !LpConfig.KeycardAsLock.Value) return;
            // ★必须**只在** isProxy 为真时接管，而不是相反。
            //   看 Player.cs:29374 的保留条件「(!InteractableObjectIsProxy || doorState == Locked)」：
            //   门锁着时只有准星打在门的代理碰撞体上，这条交互才会被保留下来。
            //   反过来在「非 proxy」时早退，等于把唯一会真正出现刷卡条目的那一支整个让掉 ——
            //   这条补丁曾经就是这样，从未生效过。
            if (!isProxy) return;
            if (owner == null || owner.Player == null || !owner.Player.IsYourPlayer) return;

            try
            {
                // 一次做完两件事：把原生「背包里每张卡各列一条」收敛掉，
                // 再补上本模组自己的「撬锁 / 骇入」两条路线。放在同一个调用里，
                // 是为了让「删什么」和「补什么」读同一个列表、不留中间态。
                LockpickInteractions.TakeOverKeycardDoor(__1, __result, owner);
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 门禁卡门接管异常：" + ex.Message);
            }
        }
    }

}
