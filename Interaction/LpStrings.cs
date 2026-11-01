// SPDX-License-Identifier: AGPL-3.0-or-later
namespace LockpickStandalone.Interaction
{
    /// <summary>交互项文案（本模组自定文案；中英双轨由 LpText 解析界面语言后给出）。</summary>
    internal static class LpStrings
    {
        internal static string PickLock { get { return LpText.PickLock; } }
        internal static string NoUsableKeycard { get { return LpText.NoUsableKeycard; } }
        internal static string CannotOpen { get { return LpText.CannotOpen; } }
        internal static string LevelPrefix { get { return LpText.LevelPrefix; } }

        // ---- 踹门提示 ----
        internal static string NotifyBreachWrongAngle() { return LpText.BreachWrongAngle; }
        internal static string NotifyBreachFail(int lockLv, int strengthLv, int attempt, float chance, float next)
        {
            return LpText.BreachFail(lockLv, strengthLv, attempt, chance, next);
        }
        internal static string NotifyBreachOpen(int attempt) { return LpText.BreachOpen(attempt); }
        internal static string NotifyBreachFractured { get { return LpText.BreachFractured; } }

        // ---- 骇入 ----
        internal static string Hack { get { return LpText.Hack; } }
        internal static string NoHackTool { get { return LpText.NoHackTool; } }
    }
}
