// SPDX-License-Identifier: AGPL-3.0-or-later
namespace LockpickStandalone.Data
{
    /// <summary>经验与难度数值（本模组自定，不沿用任何第三方表格）。</summary>
    internal static class Rewards
    {
        /// <summary>门等级 -> 基础经验（等级越高收益越高，本模组自有取值）。</summary>
        internal static float BaseXp(int doorLevel)
        {
            if (doorLevel < 1) doorLevel = 1;
            return 4f + doorLevel * 2f;
        }

        /// <summary>锁定区宽度：等级越高越窄，技能宽容度按比例加宽。</summary>
        internal static float SweetSpotRange(int doorLevel, float forgiveness)
        {
            float lv = doorLevel < 1 ? 1 : doorLevel;
            float penalty = Clamp(lv / 35f, 0.05f, 1.5f);
            float f = 1f + (forgiveness < 0f ? 0f : forgiveness);
            return Clamp((LpConfig.SP - penalty) * f, 0.02f, 0.6f);
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            return v > hi ? hi : v;
        }
    }
}