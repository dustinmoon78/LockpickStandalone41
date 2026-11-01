// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using UnityEngine;

namespace LockpickStandalone.Data
{
    /// <summary>
    /// 踹门（破门）规则的纯函数实现。
    ///
    /// 【设计上的两条轴，互不干扰】
    /// 轴一 · 撬锁等级 → 只决定「踹不踹得开、踹几脚才开」：成功率随每踹一次递增，
    ///       撬锁等级越高越容易踹开。力量不参与这一轴。
    /// 轴二 · 力量等级 → 只决定「代价多重」：骨折概率与掉血量随力量升高而减轻，
    ///       力量不参与踹开的成功率与脚数。
    ///
    /// 【为什么拆成两条轴】
    /// 若让两个技能同时影响踹开概率，玩家就只需把两者一起堆高，力量对撬锁就变成了
    /// 纯副产物；拆开后「练撬锁决定能不能踹开、练力量决定踹了多疼」，
    /// 两个技能各有各的用处，练级路线才需要选择。
    ///
    /// 【为什么全部做成纯函数】
    /// 本类不持有任何状态、不读 GameWorld、不做随机数抽取，输入只有两个等级与脚数，
    /// 因此可以脱离游戏直接回归验证（见 LocksmithRegression）。
    /// 随机与伤害的落地由调用方完成，本类只回答「该不该成功」「该受多重的伤」。
    /// </summary>
    internal static class BreachRules
    {
        /// <summary>撬锁达到该等级即进入精英段（Lv51 起）。</summary>

        /// <summary>力量等级上限（EFT 原版技能封顶值）。</summary>
        internal const int MaxStrengthLevel = 50;

        /// <summary>骨折概率的起始百分数（力量 Lv1~5 适用）。</summary>
        internal const float FractureBasePct = 10f;

        /// <summary>力量每跨过一个 5 级台阶，骨折概率下降的百分点数。</summary>
        internal const float FractureStepPerTier = 1f;

        /// <summary>一个台阶覆盖多少级力量。</summary>
        internal const int StrengthPerTier = 5;

        /// <summary>骨折概率的下限：力量练满也仍有 1%，永远不会完全免疫骨折。</summary>
        internal const float FractureFloorPct = 1f;

        // ---------------------------------------------------------------- 轴一：踹开

        /// <summary>本脚踹门的成功率（0~1）。attempt 从 0 起表示第一脚。</summary>
        /// <param name="lockpickLevel">撬锁等级。</param>
        /// <param name="attempt">第几次踹，第一次为 0。</param>
        internal static float KickChance(int lockpickLevel, int attempt)
        {
            if (attempt < 0) attempt = 0;
            if (lockpickLevel < 0) lockpickLevel = 0;

            // 精英段单独一套：首脚 50%，之后每脚再涨 25%，三脚封顶。
            // 与普通段的差距刻意压在「期望两脚多一点」——不至于一脚白给，
            // 也不至于练满级就能无脑踹开取代撬锁。
            // 精英段：首脚与递增都取配置，递增按首脚的 1/2 计算以保持「三脚封顶」的手感。
            if (IsElite(lockpickLevel))
            {
                float eliteBase = Cfg(LpConfig.BreachEliteKickBase, 0.50f);
                return Mathf.Clamp(eliteBase + attempt * eliteBase * 0.5f, 0f, 1f);
            }

            // 普通段随撬锁等级线性提高首脚成功率：Lv1 起基准值，到 Lv50 封顶两倍基准。
            float low = Cfg(LpConfig.BreachKickBase, FractureBasePct / 100f);
            float first = low + Mathf.Min(lockpickLevel, 50) / 50f * low;

            // 每失败一脚按首脚同幅度递增；「每次递增」配置作为放大倍率。
            float step = first * Cfg(LpConfig.BreachKickStep, 1f);

            return Mathf.Clamp(first + attempt * step, 0f, 1f);
        }

        /// <summary>该撬锁等级下最多踹几脚必定踹开（用于 UI 提示与自检）。</summary>
        internal static int MaxAttempts(int lockpickLevel)
        {
            if (IsElite(lockpickLevel)) return 3;
            // 普通段：首脚 10~20%，按同幅度递增，100% 封顶时所需的脚数。
            float low = Cfg(LpConfig.BreachKickBase, FractureBasePct / 100f);
            float first = low + Mathf.Min(lockpickLevel, 50) / 50f * low;
            if (first <= 0f) return 99;
            return Mathf.CeilToInt(1f / first);
        }

        // ---------------------------------------------------------------- 轴二：代价

        /// <summary>本次踹门触发骨折的概率（0~1）。精英撬锁段不再骨折。</summary>
        /// <param name="lockpickLevel">撬锁等级，决定是否已进精英段。</param>
        /// <param name="strengthLevel">力量等级，1~50，每 5 级降一个百分点。</param>
        internal static float FractureChance(int lockpickLevel, int strengthLevel)
        {
            // 精英段取消骨折，代价全部转成扣血（见 EliteBloodLoss）。
            if (IsElite(lockpickLevel)) return 0f;

            int span = Cfg(LpConfig.BreachStrengthPerTier, StrengthPerTier);
            if (span < 1) span = StrengthPerTier;
            int tiers = (Mathf.Clamp(strengthLevel, 1, MaxStrengthLevel) - 1) / span;
            float basePct = Cfg(LpConfig.BreachFractureBase, FractureBasePct);
            float dec = Cfg(LpConfig.BreachFractureStep, FractureStepPerTier);
            float pct = basePct - tiers * dec;
            return Mathf.Clamp(pct, FractureFloorPct, basePct) / 100f;
        }

        /// <summary>
        /// 精英段的扣血量（门难度 1~5）。力量越高扣得越少，力量满级（Lv50）归零 ——
        /// 此时只剩震荡与掉体力，即「双修到顶踹门不受伤」。
        /// </summary>
        /// <param name="strengthLevel">力量等级，1~50。</param>
        /// <param name="doorLevel">门难度等级，1~5（越硬的门扣得越多）。</param>
        internal static float EliteBloodLoss(int strengthLevel, int doorLevel)
        {
            int s = Mathf.Clamp(strengthLevel, 1, MaxStrengthLevel);

            // 力量满级或接近满级（最后两个台阶）时不再掉血：把「练到位」的成就感留给玩家。
            int span = Cfg(LpConfig.BreachStrengthPerTier, StrengthPerTier);
            if (span < 1) span = StrengthPerTier;
            int tiers = (s - 1) / span;
            // 最后两个台阶内已视为练到位，不再掉血。
            if (tiers * span >= MaxStrengthLevel - span) return 0f;

            // 基准曲线：门难度 1→20，2→15，3→12，4→9，5→6。
            float[] byDoor = { 20f, 15f, 12f, 9f, 6f };
            int idx = Mathf.Clamp(doorLevel, 1, 5) - 1;
            float baseLoss = byDoor[idx];

            // 再按力量打折：力量每过一个台阶减 12.5%，到顶正好归零。
            return baseLoss * (1f - tiers / 8f);
        }

        // ---------------------------------------------------------------- 震荡与体力

        /// <summary>脑震荡持续秒数。精英+满力量也保留（这是「无伤」而非「无代价」）。</summary>
        internal static float ContusionSeconds(int lockpickLevel, int strengthLevel)
        {
            // 撬锁等级越高震得越轻；力量也略有抗性，但都留一个下限，避免完全无感。
            float byLockpick = Mathf.Lerp(0.8f, 0.3f, Mathf.Clamp01((lockpickLevel - 1) / 70f));
            float byStrength = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((strengthLevel - 1) / (MaxStrengthLevel - 1)));
            return Mathf.Clamp(byLockpick * byStrength, 0.2f, 0.8f);
        }

        /// <summary>脑震荡强度。范围与 EFT 原版一致，便于直接套用。</summary>
        internal static float ContusionStrength(int lockpickLevel, int strengthLevel)
        {
            float t = Mathf.Clamp01((lockpickLevel + strengthLevel - 2) / 100f);
            return Mathf.Lerp(0.7f, 0.3f, t);
        }

        /// <summary>本次踹门消耗的体力（EFT 的 Stamina）。精英段因动作更熟练而略省。</summary>
        internal static float StaminaCost(int lockpickLevel)
        {
            return IsElite(lockpickLevel) ? 8f : 14f;
        }

        /// <summary>取配置值；配置项未绑定（极早期）或读取失败时退回默认值。</summary>
        private static float Cfg(BepInEx.Configuration.ConfigEntry<float> entry, float fallback)
        {
            if (entry == null || entry.Value <= 0f) return fallback;
            return entry.Value;
        }

        /// <summary>取整型配置值；同上兜底。</summary>
        private static int Cfg(BepInEx.Configuration.ConfigEntry<int> entry, int fallback)
        {
            if (entry == null || entry.Value < 1) return fallback;
            return entry.Value;
        }

        // ---------------------------------------------------------------- 杂项

        /// <summary>
        /// 撬锁是否已达精英段。★唯一来源：LockpickLevel.IsElite（等价于原版 Skill.IsEliteLevel 的 Level &gt; 50）。
        /// 以前这里自己写 EliteLevel=51 再 >=，与那边 &gt; 50 是同一结果却两套写法，容易改一处漏一处。
        /// </summary>
        internal static bool IsElite(int lockpickLevel)
        {
            return LockpickLevel.IsElite(lockpickLevel);
        }

        /// <summary>把两个等级拼成一行可读摘要，供调试日志单点输出（不刷屏）。</summary>
        internal static string Describe(int lockpickLevel, int strengthLevel, int doorLevel)
        {
            return string.Format(
                "Lv{0}撬/力Lv{1}/门Lv{2} 首脚{3:P0} 封顶{4}脚 骨折{5:P0} 精英扣血{6:0.#}",
                lockpickLevel, strengthLevel, doorLevel,
                KickChance(lockpickLevel, 0), MaxAttempts(lockpickLevel),
                FractureChance(lockpickLevel, strengthLevel),
                EliteBloodLoss(strengthLevel, doorLevel));
        }
    }
}
