// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using LockpickStandalone.Data;
using LockpickStandalone.Game;
using LockpickStandalone.Hack;
using LockpickStandalone.Interaction;
using LockpickStandalone.Rating;

namespace LockpickStandalone.Actions
{
    /// <summary>交互项被点击后的完整流程（自写实现）。</summary>
    internal static class LockpickActions
    {
        private static readonly Dictionary<string, string> LastRank = new Dictionary<string, string>();

        internal static void NotifyCannotOpen(GamePlayerOwner owner)
        {
            Notify(owner, LpStrings.CannotOpen);
        }

        internal static void Notify(GamePlayerOwner owner, string text)
        {
            if (owner == null || string.IsNullOrEmpty(text)) return;
            try { owner.DisplayPreloaderUiNotification(text); }
            catch (Exception ex) { LpLog.Warn("[Lockpick] 通知失败：" + ex.Message); }
        }

        private static bool IsIdle(GamePlayerOwner owner)
        {
            var po = owner as PlayerOwner;
            if (po == null || po.Player == null) return false;
            return po.Player.CurrentState is IdlePlayerState;
        }

        internal static void StartPickLock(WorldInteractiveObject door, GamePlayerOwner owner)
        {
            if (door == null || owner == null) return;
            if (!GameHost.LockReady) { LpLog.Warn("[Lockpick] 开锁面板尚未就绪"); return; }
            if (GameHost.LockVisible) return;
            if (LpConfig.RequireTools.Value && !LockpickEnv.GetLockPicksInInventory().GetEnumerator().MoveNext())
            {
                Notify(owner, LpText.NoTool);
                return;
            }
            if (!IsIdle(owner)) { Notify(owner, LpText.MustStandStill); return; }

            string map = LockpickEnv.CurrentMap();
            string keyId = door.KeyId;
            int level = LockpickEnv.GetLevelForDoor(map, door.Id, keyId);
            if (level < 1)
            {
                Notify(owner, LpText.UnregisteredDoor);
                return;
            }
            float sweet = LockpickEnv.SweetSpotForDoor(map, door.Id, keyId);
            if (sweet < 0f) { Notify(owner, LpText.SweetSpotNotReady); return; }

            int attempts;
            if (LockpickEnv.Attempts.TryGetValue(door.Id, out attempts) && attempts > LpConfig.Attempts)
            {
                Notify(owner, LpText.LockDestroyed);
                return;
            }
            if (attempts > 0) Notify(owner, LpText.AttemptsLeft(Math.Max(0, LpConfig.Attempts - attempts)));

            if (LpConfig.RequireTools.Value) ToolDurability.ConsumePick();
            GameHost.ShowLock();
            GameHost.LockGame.Begin(owner, door, delegate (bool won) { OnLockFinished(door, owner, won); }, sweet);
        }

        // ============================================================
        //  骇入（电子锁路线）
        // ============================================================

        /// <summary>
        /// 走骇入路线。与 StartPickLock 并列的第二条入口，判据与提示都自成一套：
        /// 骇入不吃「撬锁尝试次数」，但吃全局冷却，且用的是骇入工具那一个独立耐久池。
        /// </summary>
        internal static void StartHack(WorldInteractiveObject door, GamePlayerOwner owner)
        {
            if (door == null || owner == null) return;
            if (!LpConfig.HackEnabled.Value) { Notify(owner, LpText.HackDisabled); return; }

            // 换局清冷却：必须在读剩余时间之前
            HackCooldown.TickSession();

            // 冷却再查一次：菜单项是上一次重建时算的，可能已经过期（也可能刚被别人刷过）
            string cooling = HackCooldown.Describe();
            if (cooling != null) { Notify(owner, cooling); return; }

            if (!GameHost.HackReady) { LpLog.Warn("[Lockpick] 骇入面板尚未就绪"); return; }
            if (GameHost.HackVisible) return;
            if (LpConfig.RequireTools.Value && !ToolCatalog.HasHack())
            {
                Notify(owner, LpText.NoHackTool);
                return;
            }
            if (!IsIdle(owner)) { Notify(owner, LpText.MustStandStill); return; }

            string map = LockpickEnv.CurrentMap();
            int level = LockpickEnv.GetLevelForDoor(map, door.Id, door.KeyId);
            if (level < 1) { Notify(owner, LpText.UnregisteredDoor); return; }

            // ★开门不扣任何耐久：骇入装置是无限使用的（-1），频率限制交给全局冷却。
            //   这里只要求「背包里有装置」，判据在上面 ToolCatalog.HasHack() 那一关。
            GameHost.ShowHack();
            GameHost.HackGame.Activate(owner, door, delegate (bool won) { OnHackFinished(door, owner, won); });
        }

        private static void OnHackFinished(WorldInteractiveObject door, GamePlayerOwner owner, bool won)
        {
            if (door == null || owner == null) return;
            int clean = GameHost.HackReady ? GameHost.HackGame.CleanLocks : 0;

            if (won)
            {
                string rank;
                // 评级照常参与（骇入的「一次全对」也能拿 S），但骇入侧没有耐久可退，
                // 所以退回量这个 out 参数直接丢弃。
                float mult = LockPickingPerformance.ReportSuccess(owner, clean, out rank, out _);
                // 骇入收益高于撬锁：这是它为全局冷却与更慢节奏换来的报酬。
                XpAward.Award(owner, door, false, clean, mult * HackRules.XpRatio(true));
                try { door.Unlock(); }
                catch (Exception ex) { LpLog.Error("[Lockpick] 骇入解门失败：" + ex.Message); }
                Notify(owner, LpText.HackSuccess(rank));
                return;
            }

            // ★失败不触发全局冷却：冷却是对「成功开了一次门」的定价，
            //   把试错也罚进去只会让玩家一次都不敢碰骇入。
            Notify(owner, LpText.HackFailed);
            LockPickingPerformance.ReportFailure(owner);
            XpAward.Award(owner, door, true, 0, 0f);
        }

        private static void OnLockFinished(WorldInteractiveObject door, GamePlayerOwner owner, bool won)
        {
            if (door == null || owner == null) return;
            int perfects = GameHost.LockReady ? GameHost.LockGame.PerfectHits : 0;

            if (won)
            {
                string rank;
                int refund;
                float mult = LockPickingPerformance.ReportSuccess(owner, perfects, out rank, out refund);
                LastRank[door.Id] = rank;
                if (refund > 0) ToolDurability.RefundPick(refund, rank);
                XpAward.Award(owner, door, false, perfects, mult);
                try { door.Unlock(); }
                catch (Exception ex) { LpLog.Error("[Lockpick] 解门失败：" + ex.Message); }
                Notify(owner, LpText.PickSuccess(rank));
                return;
            }

            Notify(owner, LpText.PickFailed);
            LockPickingPerformance.ReportFailure(owner);
            XpAward.Award(owner, door, true, 0, 0f);
            AddFailedAttempt(door, owner);
        }

        private static void AddFailedAttempt(WorldInteractiveObject door, GamePlayerOwner owner)
        {
            int n;
            if (!LockpickEnv.Attempts.TryGetValue(door.Id, out n)) n = 0;
            n++;
            LockpickEnv.Attempts[door.Id] = n;
            if (n >= LpConfig.Attempts)
            {
                Notify(owner, LpText.LockBroke);
                try
                {
                    door.KeyId = string.Empty;
                    door.Operatable = false;
                    door.DoorStateChanged((EDoorState)0);
                }
                catch (Exception ex) { LpLog.Warn("[Lockpick] 锁损坏处理异常：" + ex.Message); }
            }
        }
    }
}
