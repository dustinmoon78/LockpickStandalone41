// SPDX-License-Identifier: AGPL-3.0-or-later
using EFT;
using EFT.Interactive;
using LockpickStandalone.Data;

namespace LockpickStandalone.Actions
{
    /// <summary>
    /// 经验结算。两条路都要走，缺一条技能等级就不动：
    /// ① Skill.UseEffectiveness（EFT\Skill.cs:134）——只累计 _pointsEarned 与 _effectiveness
    ///     （技能面板的「本场 +N」依赖它），**从不写 Current**；
    /// ② Skill.SetCurrent（EFT\BaseSkill.cs:90）——真正推进等级数值的那一步。原版只有
    ///     Skill.OnTrigger（EFT\Skill.cs:218 → BaseSkill.OnTrigger :75）会调它，而原版从不为开锁
    ///     触发该动作（SkillManager.cs:2573 用空数组构造 Lockpicking），所以必须由本模组自己推进。
    ///     此前只调 ① —— 那正是「技能等级恒 0、进度恒 0/10」的成因。
    /// 原版 4.1 不把开锁放进 Skills/DisplayList（SkillManager.cs:2088-2096），故槽位由本模组的
    /// Patches\SkillInjectPatch.cs 自行注入；技能扩展在位时它已提供、本模组不重复注入。
    ///
    /// 【Lv51~70 精英段的可达性】此处原先按原版的 IsEliteLevel（Level &gt; 50）直接 return，
    /// 上限交给游戏与可选的上限模组决定。
    /// 现在上限改由 LockpickLevel 决定（默认 70，上限=50 时行为与改造前完全一致）。
    /// 注：原版 Skill.UpdateRules 在 Level&gt;50 时会 Unsubscribe()，但 Lockpicking 的
    /// SkillAction 数组是空的（SkillManager.cs:2573），退订无对象可退，不影响这里推经验。
    /// </summary>
    internal static class XpAward
    {
        private static int _awardCount;
        private static bool _capLogged;
        private static bool _eliteLogged;

        internal static void Award(GamePlayerOwner owner, WorldInteractiveObject door, bool failure, int perfects, float extraMultiplier)
        {
            var skill = LockpickEnv.LockpickSkill();
            if (skill == null) return;

            int level = skill.Level;
            int cap = LockpickEnv.MaxLevel;

            // 到等级上限才停收经验；精英福利（工具免磨损）全程保留。
            if (level >= cap)
            {
                if (!_capLogged)
                {
                    _capLogged = true;
                    LpLog.LogWarning("[开锁技能] 已达等级上限 Lv" + cap + "：经验不再累积");
                }
                return;
            }

            if (LockpickEnv.EliteToolExempt(level) && !_eliteLogged)
            {
                _eliteLogged = true;
                LpLog.LogWarning("[开锁技能] 进入精英段（Lv" + (LockpickLevel.VanillaMaxLevel + 1) + " 起）：开锁工具不再消耗耐久");
            }

            string keyId = door == null ? null : door.KeyId;
            string map = null;
            var po = owner as PlayerOwner;
            if (po != null && po.Player != null) map = po.Player.Location;
            if (string.IsNullOrEmpty(map)) map = LockpickEnv.CurrentMap();

            int doorLevel = LockpickEnv.GetLevelForDoor(map, door == null ? null : door.Id, keyId);
            if (doorLevel < 1) return;

            // 不衰减：这里只有「乘法倍率」，没有任何随局数/场地递减的项。
            // 原版的技能疲劳（UseEffectiveness 里按本场已赚点数把 _effectiveness 逐点掉到 0.0001）
            // 已由 Patches\FatiguePatch.cs（LockPickingFatiguePatch）对 Lockpicking 豁免
            // （F12「3.经验/忽略技能疲劳」默认开）。
            float xp = Rewards.BaseXp(doorLevel);
            if (failure) xp *= LpConfig.FailureRatio;
            if (!failure && perfects > 0) xp *= 1f + perfects * LpConfig.PerfectXpBonus.Value;
            if (extraMultiplier > 0f) xp *= extraMultiplier;
            if (LpConfig.XpMultiplier.Value > 0f) xp *= LpConfig.XpMultiplier.Value;
            if (xp <= 0f) return;

            // ① 疲劳口径与「本场已赚点数」（面板显示用）
            skill.UseEffectiveness(xp);
            // ② 等级入口：不写 Current，等级与进度就永远不动
            skill.SetCurrent(skill.Current + xp, silent: false);

            // 稀疏证据行（磁盘日志只收 Warning 及以上）：第 1/10/100/1000 把各留一条
            int count = ++_awardCount;
            if (count == 1 || count == 10 || count == 100 || count == 1000)
            {
                LpLog.LogWarning("[开锁经验] 第 " + count + " 把：门难度 Lv." + doorLevel + "，入账 " + xp.ToString("0.###")
                    + " 点，技能 Lv." + skill.Level + "（进度 " + skill.Current.ToString("0.#") + "/" + skill.LevelExp + "）");
            }
            LpLog.Debug("[Lockpick] 经验 +" + xp.ToString("0.##") + "（门等级 " + doorLevel
                + (failure ? " / 失败" : (" / 完美 " + perfects)) + "）");
        }
    }
}
