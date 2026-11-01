// SPDX-License-Identifier: AGPL-3.0-or-later
namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 【已退役 2026-10-17】本文件原含四个补丁，把开锁技能的等级天花板从原版 50 放开到 70：
    ///   ① SkillClampCurrentPatch   → BaseSkill.ClampCurrent
    ///   ② SkillSummaryLevelPatch   → BaseSkill.SummaryLevel
    ///   ③ SkillSaveProgressPatch   → SkillsDescriptor 两处写档钳位
    ///   ④ SkillSaveDescriptorCtorPatch
    ///
    /// 【为什么退役】用户拍板：等级上限不再由本模组管，全交给 SkillCap100
    /// （它同样挂这四个方法，默认 MaxSkillLevel=100、EliteUnlockLevel=100）。
    /// 两边同时挂同一批方法时谁生效取决于 Harmony 注册顺序，等于状态不可预测 —— 退役我方是唯一的确定解。
    ///
    /// 保留本文件而不删，是为了让后来者知道「这里曾经管过等级上限、为什么现在不管」。
    /// 撬锁自家的精英判定（工具免磨损 / 踹门段 / 骇入段）仍在 Data\LockpickLevel.IsElite，
    /// 那是本模组自己的曲线，与等级上限无关。
    /// </summary>
    internal static class SkillLevelCapRetiredNotice
    {
        internal const string RetiredOn = "2026-10-17";
        internal const string Reason = "等级上限交给 SkillCap100，避免两边挂同一批方法互相覆盖";
    }
}
