// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using LockpickStandalone.Data;
using LockpickStandalone.Interaction;

namespace LockpickStandalone
{
    /// <summary>与游戏世界交互的环境层（取代上游 helpers 文件，全部本模组自写）。</summary>
    internal static class LockpickEnv
    {
        private static readonly Dictionary<string, float> SweetSpotCache = new Dictionary<string, float>();
        internal static readonly Dictionary<string, int> Attempts = new Dictionary<string, int>();

        internal static void ResetSession()
        {
            SweetSpotCache.Clear();
            Attempts.Clear();
            // 骇入冷却按战局论，不跨局继承
            LockpickStandalone.Hack.HackCooldown.Reset();
        }


        /// <summary>玩家所在地图 id；Player.Location 在部分游戏类型下为空，回落 GameWorld.LocationId。</summary>
        internal static string ResolveMap(string locationId)
        {
            if (!string.IsNullOrEmpty(locationId)) return locationId;
            var world = Singleton<GameWorld>.Instance;
            return world == null ? null : world.LocationId;
        }

        internal static string CurrentMap()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null) return null;
            var player = world.MainPlayer;
            return ResolveMap(player == null ? null : player.Location);
        }

        internal static bool IsKnownMap(string locationId) { return LpData.IsKnownMap(ResolveMap(locationId)); }

        internal static int GetLevelForDoor(string locationId, string doorId, string keyId)
        {
            string map0 = ResolveMap(locationId);
            int foreign = Compat.SeCompat.ForeignLevel(map0, doorId);
            if (foreign > 0) return Math.Min(foreign, LpData.MaxLevel);
            return LpData.GetLevel(map0, doorId, keyId);
        }
        internal static int GetLevelFromJson(string locationId, string doorId, string keyId)
        {
            return LpData.GetLevel(ResolveMap(locationId), doorId, keyId);
        }

        internal static string KeyName(string keyId) { return LpData.KeyName(keyId); }

        /// <summary>技能等级的事实上限：口径唯一入口在 LockpickLevel（默认 70，可经 F12 退回原版 50）。</summary>
        internal static int MaxLevel { get { return LockpickLevel.MaxLevel; } }

        /// <summary>每 10 级一个台阶。</summary>
        private const int TierSpan = 10;

        /// <summary>台阶总数（Lv10 起一档，Lv100 共 10 档）。</summary>
        internal static int MaxTier { get { return LockpickEnv.MaxLevel / TierSpan; } }

        /// <summary>连续段的拐点：Lv30 起改走延展段（与旧实现在 Lv30 的取值逐点相同）。</summary>
        private const float RampKnee = 0.6f;

        /// <summary>Lv30 之后每级追加的连续段增量。</summary>
        private const float RampSlopeAfterKnee = 0.01f;

        /// <summary>台阶斜率：每级 0.004，即每 10 级跳 +0.04。</summary>
        private const float StepPerLevel = 0.004f;

        /// <summary>宽容度上限：参考上限级（Lv100）恰好触顶 1.2；原版 50 级时为 1.000。</summary>
        private const float ForgivenessCeiling = 1.2f;

        /// <summary>游标速度的分母系数（与 DeltaLockGame.BuildSpeedAndLimits 的 skillBuff = 1 + F*2 同源）。</summary>
        private const float SliderSpeedDivisor = 2f;

        // ---- 六条加成行各自复用的原版 EBuffId（图标免费获得；全部在 EBuffId 枚举里真实存在）----
        // 选法三条：① 语义最近；② 各自来自 6 个不同技能，避免面板上一眼看过去全是同一个图标；
        //        ③ 都在 SkillManager 构造器里被真的 new 出来过（⇒ StaticIcons 里有图，不会空白）。
        internal const EBuffId BonusSlider = EBuffId.SearchSpeed;                        // Search「搜索速度」
        internal const EBuffId BonusZone = EBuffId.MagDrillsInventoryCheckAccuracy;       // MagDrills「装填检查准确率」
        internal const EBuffId BonusErrors = EBuffId.StressPainChance;                   // StressResistance「压力疼痛概率」
        internal const EBuffId BonusDurable = EBuffId.WeaponDurabilityLossOnShotReduce;  // WeaponTreatment「武器耐久损耗降低」
        internal const EBuffId BonusXp = EBuffId.IntellectLearningSpeed;                 // Intellect「学习速度」
        internal const EBuffId BonusElite = EBuffId.TroubleFixingDurElite;               // TroubleShooting「耐久·精英」

        /// <summary>一条加成行（面板图标行、悬停文案、局内提示行共用的同一份数据）。</summary>
        internal readonly struct LevelBonus
        {
            internal readonly EBuffId Id;
            internal readonly SkillManager.EBuffType Type;
            internal readonly float Value;

            internal LevelBonus(EBuffId id, SkillManager.EBuffType type, float value)
            {
                Id = id;
                Type = type;
                Value = value;
            }
        }

        /// <summary>当前开锁技能等级（已钳位）；技能不存在时按 0 级处理。</summary>
        internal static int CurrentLevel()
        {
            var skill = LockpickSkill();
            if (skill == null) return 0;
            return LockpickLevel.Clamp(skill.Level);
        }

        /// <summary>精英段的「工具免磨损」是否对当前等级生效。</summary>
        internal static bool EliteToolExempt() { return LockpickLevel.EliteToolExempt(CurrentLevel()); }

        /// <summary>精英段的「工具免磨损」是否对指定等级生效（纯函数版）。</summary>
        internal static bool EliteToolExempt(int level) { return LockpickLevel.EliteToolExempt(level); }

        /// <summary>台阶档位（Lv10 一档）。</summary>
        internal static int Tier() { return TierFor(CurrentLevel()); }

        /// <summary>台阶档位的纯函数版（面板 Buff 与局内提示行都走它，避免两处算出不同的档）。</summary>
        internal static int TierFor(int level) { return LockpickLevel.Clamp(level) / TierSpan; }

        /// <summary>台阶带来的额外容错次数（纯函数版）。</summary>
        internal static float TierErrorsFor(int level) { return TierFor(level) * LpConfig.TierErrorPerStep; }

        /// <summary>台阶带来的自动解锁耐久代价减免（纯函数版）。</summary>
        internal static float TierDurabilityReliefFor(int level) { return TierFor(level) * LpConfig.TierDurabilityPerStep; }

        /// <summary>台阶带来的评级经验加成（纯函数版，比例值，0.05 = +5%）。</summary>
        internal static float TierXpBonusFor(int level) { return TierFor(level) * LpConfig.TierXpPerStep; }

        /// <summary>台阶带来的额外容错次数。</summary>
        internal static float TierErrors() { return TierErrorsFor(CurrentLevel()); }

        /// <summary>台阶带来的自动解锁耐久代价减免。</summary>
        internal static float TierDurabilityRelief() { return TierDurabilityReliefFor(CurrentLevel()); }

        /// <summary>台阶带来的评级经验加成。</summary>
        internal static float TierXpBonus() { return TierXpBonusFor(CurrentLevel()); }

        /// <summary>台阶档位的一句话说明（提示行与升档播报共用；数值全部取自上面的纯函数版）。</summary>
        internal static string TierBanner()
        {
            int level = CurrentLevel();
            int tier = TierFor(level);
            if (tier <= 0) return LpText.TierNone(MaxTier);   // 面向玩家 → 走语言层
            return LpText.TierBanner(tier, MaxTier, tier * TierSpan,
                TierErrorsFor(level),
                TierDurabilityReliefFor(level),
                TierXpBonusFor(level) * 100f);
        }

        /// <summary>
        /// 游标速度相对「零级技能」的降低比例（1 - 1/(1+2F)）。
        /// 局内口径：DeltaLockGame.BuildSpeedAndLimits 用 skillBuff = 1 + F*2 做分母，
        /// F 越大游标越慢越容易锁 ⇒ 把它报成「降低百分比」比报一个 <1 的倍率好读。
        /// </summary>
        internal static float SliderSlowdown(float forgiveness)
        {
            float divisor = 1f + (forgiveness < 0f ? 0f : forgiveness) * SliderSpeedDivisor;
            if (divisor <= 0f) return 0f;
            return 1f - 1f / divisor;
        }

        /// <summary>锁定区宽度相对「零级技能」的倍率（局内口径：ZoneWidth = … × (1+F)）。</summary>
        internal static float ZoneWidthFactor(float forgiveness)
        {
            return 1f + (forgiveness < 0f ? 0f : forgiveness);
        }

        /// <summary>
        /// 【六条加成行的唯一数据源】面板 Buff 图标、Buff 悬停文案、局内台阶提示行全部读这里，
        /// 保证「面板上显示的数」与「局内实际生效的数」永远是同一个数。
        /// 每次调用会先清空 rows，返回写入条数（固定 6）。
        /// </summary>
        internal static int LevelBonusRows(List<LevelBonus> rows, int level)
        {
            if (rows == null) return 0;
            rows.Clear();
            level = LockpickLevel.Clamp(level);
            float forgiveness = ForgivenessFor(level);

            rows.Add(new LevelBonus(BonusSlider, SkillManager.EBuffType.Simple, SliderSlowdown(forgiveness)));
            rows.Add(new LevelBonus(BonusZone, SkillManager.EBuffType.Simple, ZoneWidthFactor(forgiveness)));
            rows.Add(new LevelBonus(BonusErrors, SkillManager.EBuffType.Simple, TierErrorsFor(level)));
            rows.Add(new LevelBonus(BonusDurable, SkillManager.EBuffType.Simple, TierDurabilityReliefFor(level)));
            rows.Add(new LevelBonus(BonusXp, SkillManager.EBuffType.Simple, TierXpBonusFor(level)));
            rows.Add(new LevelBonus(BonusElite, SkillManager.EBuffType.Elite,
                LockpickLevel.EliteToolExempt(level) ? 1f : 0f));
            return rows.Count;
        }

        /// <summary>
        /// 技能宽容度（0~1.2）：连续段 + 台阶段 + 精英段合成，全等级区间严格单调不减。
        ///
        /// 连续段（Lv0~50，公式一个字没动，改造前后逐点完全一致）：
        ///   Lv1~30 每级 +0.02；Lv30~50 每级再 +0.01；台阶段每级 +0.004（每 10 级跳 +0.04）。
        ///   Lv50 = 0.80 + 0.20 = 1.000。
        /// 精英段（Lv51~上限）：在 Lv50 的 1.000 基础上每级 +「精英段每级宽容度」（默认 0.004），封顶 1.2。
        ///   Lv100 = 1.000 + 50×0.004 = 1.200，正好触顶。
        /// </summary>
        internal static float Forgiveness() { return ForgivenessFor(CurrentLevel()); }

        /// <summary>宽容度的纯函数版（不读 GameWorld，回归自检与面板数据源共用）。</summary>
        internal static float ForgivenessFor(int level)
        {
            level = LockpickLevel.Clamp(level);
            if (level <= LockpickLevel.VanillaMaxLevel) return LegacyForgiveness(level);

            float step = LpConfig.EliteForgivenessStep != null ? LpConfig.EliteForgivenessStep.Value : 0.004f;
            if (step < 0f) step = 0f;
            float elite = LegacyForgiveness(LockpickLevel.VanillaMaxLevel)
                + (level - LockpickLevel.VanillaMaxLevel) * step;
            // 封顶点落在曲线参考上限（Lv100）。原版 50 级时宽容度就是 1.000，本模组不提高也不降低。
            //   每级增量默认 0.004 ⇒ Lv100 = 1.000 + 50×0.004 = 1.200；
            //   51~100 段每级 +0.4%：只有装了等级上限模组时才会真的被跑到。
            //   ★没有等级上限模组时，原版把 Current 钳在 5100 ⇒ 实际封顶 51 级，本段自然走不到；
            //     曲线本身单调，不影响 0~51 的手感。
            return elite > ForgivenessCeiling ? ForgivenessCeiling : elite;
        }

        /// <summary>Lv0~50 的原公式：改造前后必须逐点相同，禁止在此处做任何"优化"。</summary>
        private static float LegacyForgiveness(int level)
        {
            float ramp = level <= 30 ? level * 0.02f : RampKnee + (level - 30) * RampSlopeAfterKnee;
            float total = ramp + level * StepPerLevel;
            return total > ForgivenessCeiling ? ForgivenessCeiling : total;
        }

        internal static Skill LockpickSkill()
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world == null ? null : world.MainPlayer;
            if (player == null || player.Skills == null) return null;
            return player.Skills.Lockpicking;
        }

        /// <summary>
        /// 力量等级（1~50）。原版技能封顶 50，这里同样按 50 钳位。
        /// 读取路径与上面读撬锁完全一致（同一个 Skills 集合），因此不存在额外的兼容风险。
        /// 取不到时返回 1（最低），宁可按最坏情况算代价，也不要让玩家免伤。
        /// </summary>
        internal static int StrengthLevel()
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world == null ? null : world.MainPlayer;
            if (player == null || player.Skills == null) return 1;
            int level = player.Skills.Strength.Level;
            return level < 1 ? 1 : (level > BreachRules.MaxStrengthLevel ? BreachRules.MaxStrengthLevel : level);
        }

        internal static float SweetSpotForDoor(string locationId, string doorId, string keyId)
        {
            float cached;
            if (!string.IsNullOrEmpty(doorId) && SweetSpotCache.TryGetValue(doorId, out cached)) return cached;
            int level = GetLevelForDoor(locationId, doorId, keyId);
            if (level < 0) return -1f;
            float value = Rewards.SweetSpotRange(level, Forgiveness());
            if (!string.IsNullOrEmpty(doorId)) SweetSpotCache[doorId] = value;
            return value;
        }

        /// <summary>该 owner 是否不是本地玩家（观察者/人机视角不注入交互）。</summary>
        internal static bool IsBotInteraction(GamePlayerOwner owner)
        {
            if (owner == null) return true;
            var po = owner as PlayerOwner;
            if (po == null || po.Player == null) return true;
            var world = Singleton<GameWorld>.Instance;
            var main = world == null ? null : world.MainPlayer;
            if (main == null) return true;
            return po.Player.Id != main.Id;
        }

        internal static IEnumerable<Item> GetLockPicksInInventory() { return ToolCatalog.Picks(); }
    }
}
