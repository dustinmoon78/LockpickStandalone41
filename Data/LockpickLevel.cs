// SPDX-License-Identifier: AGPL-3.0-or-later
namespace LockpickStandalone.Data
{
    /// <summary>
    /// 开锁技能「等级钳位」的唯一入口（规范 §3.1）。
    ///
    /// 4.1 的等级天花板写死在原版代码里，本类只做一件事：把「上限是多少」收口到一处，
    /// LockpickEnv 的宽容度 / 台阶、ToolDurability 的精英判定
    /// 全部读这里 —— 谁想改上限只改这一个类，不会出现两处算出两个上限。
    ///
    /// 取证：EFT\Skill.cs:10 MAX_LEVEL=50；EFT\BaseSkill.cs:85-88 ClampCurrent 把 Current 钳 0..5100
    /// （5100/100 = 等级 51，即原版事实上钉死在 51）；EFT\BaseSkill.cs:35 SummaryLevel 封 51；
    /// EFT\SkillsDescriptor.cs:55 与 :88 两处写档钳位同样 0..5100。
    /// </summary>
    internal static class LockpickLevel
    {
        /// <summary>原版硬上限（EFT.Skill::MAX_LEVEL = 50）。宽容度曲线在这一级正好 = 1.000，是精英段的起点。</summary>
        internal const int VanillaMaxLevel = 50;

        /// <summary>
        /// 本模组曲线的参考上限，只用于「宽容度曲线能算到第几级」这一件事。
        /// ★不是等级上限：原版把进度钳 0..5100（即 51 级），本模组不放开也不收窄；
        ///   装了「技能上限 100」这类模组时等级才会真的超过 51，曲线在上限内的取值照常成立。
        /// </summary>
        internal const int CurveReferenceLevel = 100;


        /// <summary>原版的 Current 钳位（其余 43 个技能永远沿用它，本模组一个字节都不改）。</summary>
        internal const float VanillaProgressCap = 5100f;

        /// <summary>本模组自家曲线用到的参考上限（固定 100）。★不再对外钳位——等级上限由 SkillCap100 决定。</summary>
        internal static int MaxLevel
        {
            get
            {
                // ★2026-10-17 用户拍板：等级上限不再由本模组管，全交给 SkillCap100（默认 100）。
                //   这里只保留一个「撬锁自家曲线的参考上限」，用于宽容度/精英判定的形状，不参与钳位。
                return CurveReferenceLevel;
            }
        }


        /// <summary>把任意等级钳进 0..MaxLevel。</summary>
        internal static int Clamp(int level)
        {
            if (level < 0) return 0;
            int cap = MaxLevel;
            return level > cap ? cap : level;
        }

        /// <summary>
        /// 精英判定：与原版 Skill.IsEliteLevel 完全一致 —— EFT\Skill.cs:38 写的是 Level > 50，
        /// 即 51 级起算精英。开锁和其它 43 个技能用的是同一把尺子，不另立标准。
        /// </summary>
        internal static bool IsElite(int level) { return level > VanillaMaxLevel; }

        /// <summary>
        /// 精英段的「工具免磨损」是否生效（F12 可关）。
        /// 触发条件刻意与原版精英完全对齐：升到 51 级即生效，不需要更高的等级。
        /// </summary>
        internal static bool EliteToolExempt(int level)
        {
            if (LpConfig.EliteToolExempt != null && !LpConfig.EliteToolExempt.Value) return false;
            return IsElite(level);
        }

        /// <summary>Current 的参考上限 = 等级上限 × 100。仅曲线内部参考，不对外钳位（钳位已交给游戏 / SkillCap100）。</summary>
        internal static float ProgressCap { get { return MaxLevel * 100f; } }
    }
}
