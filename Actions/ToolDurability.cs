// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Linq;
using EFT.InventoryLogic;
using LockpickStandalone.Data;

namespace LockpickStandalone.Actions
{
    /// <summary>工具耐久的消耗与退回（自写实现；4.1 的钥匙类物品通过 KeyComponent 读写使用次数）。</summary>
    internal static class ToolDurability
    {
        // 只记「最后一次消耗的那件撬锁工具」，供高评级退回用。
        // ★骇入装置不消耗（-1 无限），所以骇入侧既没有消耗池也没有退回池 ——
        //   早先这里是个 _lastHack，去耐久后已随之删除。
        private static Item _lastPick;

        /// <summary>
        /// 精英段免磨损（规范 §3.6）：判据从原版的 Skill.IsEliteLevel（= Level &gt; 50，与放开钳位后自然生效）
        /// 换成 LockpickLevel.IsElite(skill.Level)，好让判定只剩一处、不再依赖第三方语义 ——
        /// 上限退回 50 时 IsElite 恒 false，行为与改造前逐字节一致。
        /// ★生效时机：ConsumePick() 的第一件事就是它，早于任何 NumberOfUsages 写入；
        /// CanAfford() 同样先问它，否则「不磨工具」的精英在自动解锁通道上仍会被耐久检查挡住。
        /// （骇入侧已无耐久，本判据对它不再适用。）
        /// </summary>
        private static bool EliteExempt()
        {
            var skill = LockpickEnv.LockpickSkill();
            return skill != null && LockpickEnv.EliteToolExempt(skill.Level);
        }

        private static KeyComponent ComponentOf(Item item)
        {
            if (item == null) return null;
            try { return item.GetItemComponent<KeyComponent>(); }
            catch { return null; }
        }

        private static int MaxUse(KeyComponent kc)
        {
            if (kc == null || kc.Template == null) return 0;
            return kc.Template.MaximumNumberOfUsage;
        }

        internal static bool CanAfford(int charges)
        {
            if (!LpConfig.RequireTools.Value) return true;
            // 精英段根本不磨工具，耐久检查对它没有意义（否则自动解锁会被误判为「耐久不足」）。
            if (EliteExempt()) return true;
            if (charges <= 0) return true;
            var kc = ComponentOf(LockpickEnv.GetLockPicksInInventory().FirstOrDefault());
            if (kc == null) return false;
            int max = MaxUse(kc);
            if (max <= 0) return true;
            return (max - kc.NumberOfUsages) >= charges;
        }

        internal static void ConsumePicks(int times)
        {
            for (int i = 0; i < times; i++)
            {
                if (LockpickEnv.GetLockPicksInInventory().FirstOrDefault() == null) return;
                ConsumePick();
            }
        }

        internal static void ConsumePick()
        {
            _lastPick = null;
            // ★精英豁免在第一次真正扣耐久之前就返回：此后 _lastPick 保持 null，
            //   RefundPick() 自然退 0 次，不会出现「没扣却退」的负数。
            if (EliteExempt()) return;
            var pick = LockpickEnv.GetLockPicksInInventory().FirstOrDefault();
            if (pick == null) return;
            _lastPick = pick;
            UseOnce(pick);
        }

        private static void UseOnce(Item item)
        {
            var kc = ComponentOf(item);
            if (kc == null) return;
            kc.NumberOfUsages++;
            int max = MaxUse(kc);
            if (max <= 0 || kc.NumberOfUsages < max) return;
            try
            {
                var owner = item.Parent == null ? null : item.Parent.GetOwner();
                ItemManipulator.Discard(item, (ItemController)owner, false);
            }
            catch (Exception ex)
            {
                LpLog.Warn("[Lockpick] 工具用尽后丢弃失败：" + ex.Message);
            }
        }

        /// <summary>撬锁侧高评级退回耐久。</summary>
        internal static void RefundPick(int times, string rank) { RefundOne(ref _lastPick, times, rank); }

        /// <summary>只退本次真正扣掉的那一件，且不制造负数。target 由调用方按池子选定。</summary>
        private static void RefundOne(ref Item slot, int times, string rank)
        {
            if (times <= 0) return;
            var target = slot;
            slot = null;
            int back = 0;
            try
            {
                var kc = ComponentOf(target);
                if (kc != null && target.Parent != null && kc.NumberOfUsages > 0)
                {
                    back = Math.Min(times, kc.NumberOfUsages);
                    kc.NumberOfUsages -= back;
                }
            }
            catch (Exception ex)
            {
                LpLog.Warn("[Lockpick] 退回工具耐久失败：" + ex.Message);
            }
            LpLog.Info("[开锁表现] 工具耐久退回 " + back + "/" + times + "（" + rank + " 级）");
        }
    }
}
