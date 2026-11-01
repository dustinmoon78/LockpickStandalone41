// SPDX-License-Identifier: AGPL-3.0-or-later
using LockpickStandalone.Ui;
using UnityEngine;

namespace LockpickStandalone.Game
{
    /// <summary>
    /// 小游戏宿主：按需在自有 Canvas 下构建开锁面板（只建一次，之后只开关可见性）。
    /// 上游从 bundle 取 Canvas 与模型；本模组全部运行期构建。
    /// </summary>
    internal static class GameHost
    {
        private static DeltaLockGame _lockGame;
        private static GameObject _lockGo;

        internal static DeltaLockGame LockGame
        {
            get
            {
                if (_lockGame == null) BuildLock();
                return _lockGame;
            }
        }

        // 触发式就绪：调用方先问 LockReady 再决定是否开锁，故这里必须自己触发构建，
        // 否则「检查」永远为假、BuildLock 永不被调用（面板永远不存在）。
        internal static bool LockReady { get { if (_lockGame == null) BuildLock(); return _lockGame != null; } }

        private static void BuildLock()
        {
            try
            {
                // 兜底：补丁装配可能晚于 SkillManager 构造（后者发生在进游戏建档时），此处首次打开面板前补插一次。
                Patches.SkillInjectPatch.EnsureInjected();
                var canvas = UiRoot.Canvas;
                var ui = DeltaLockUIBuilder.Build(canvas);
                var panel = ui.Panel != null ? ui.Panel.gameObject : null;
                if (panel == null) { LpLog.Error("[Lockpick] 开锁面板构建失败：面板为空"); return; }
                _lockGo = panel;
                _lockGame = panel.AddComponent<DeltaLockGame>();
                _lockGame.Bind(ui);
                panel.SetActive(false);
                LpLog.Info("[Lockpick] 开锁面板已就绪（等级区间 / 完美锁定 / 自动解锁）");
            }
            catch (System.Exception ex)
            {
                LpLog.Error("[Lockpick] 开锁面板构建异常：" + ex);
            }
        }

        // ---------------- 骇入面板 ----------------

        private static Hack.TerminalHackGame _hackGame;
        private static GameObject _hackGo;

        internal static Hack.TerminalHackGame HackGame { get { if (_hackGame == null) BuildHack(); return _hackGame; } }
        internal static bool HackReady { get { if (_hackGame == null) BuildHack(); return _hackGame != null; } }

        private static void BuildHack()
        {
            try
            {
                var canvas = UiRoot.Canvas;
                var ui = DeltaHackUIBuilder.Build(canvas);
                var panel = ui.Panel != null ? ui.Panel.gameObject : null;
                if (panel == null) { LpLog.Error("[Lockpick] 骇入面板构建失败：面板为空"); return; }
                // ★先关掉再挂组件：AddComponent 到激活中的物体会立刻触发 OnEnable，
                //   那时面板还没 Bind，OnEnable 里对玩家的姿态操作会白做一遍。
                panel.SetActive(false);
                _hackGo = panel;
                _hackGame = panel.AddComponent<Hack.TerminalHackGame>();
                _hackGame.SetHackUI(ui);
                LpLog.Info("[Lockpick] 骇入面板已就绪（滚轮密码 / 成功环 / 全局冷却）");
            }
            catch (System.Exception ex)
            {
                LpLog.Error("[Lockpick] 骇入面板构建异常：" + ex);
            }
        }

        internal static void ShowHack() { if (_hackGo != null) _hackGo.SetActive(true); }
        internal static void HideHack() { if (_hackGo != null) _hackGo.SetActive(false); }
        internal static bool HackVisible { get { return _hackGo != null && _hackGo.activeSelf; } }

        internal static void ShowLock() { if (_lockGo != null) _lockGo.SetActive(true); }
        internal static void HideLock() { if (_lockGo != null) _lockGo.SetActive(false); }
        internal static bool LockVisible { get { return _lockGo != null && _lockGo.activeSelf; } }
    }
}
