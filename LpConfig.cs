// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace LockpickStandalone
{
    /// <summary>F12 配置。全部为本模组自有默认值（不复制任何上游数据）。</summary>
    internal static class LpConfig
    {
        private const string G = "万能钥匙";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<string> LockpickIds;
        internal static ConfigEntry<bool> RequireTools;
        internal static ConfigEntry<int> AttemptsBeforeBreak;
        internal static ConfigEntry<bool> PriceTiers;
        internal static ConfigEntry<string> KeyTableFile;
        internal static ConfigEntry<float> SweetSpotRangeBase;
        internal static ConfigEntry<float> FailureLockXpRatio;
        internal static ConfigEntry<float> XpMultiplier;
        internal static ConfigEntry<float> PerfectXpBonus;
        internal static ConfigEntry<float> PerfectZoneRatio;
        internal static ConfigEntry<float> ZoneWidthMultiplier;
        internal static ConfigEntry<float> SliderSpeedMultiplier;
        internal static ConfigEntry<int> BonusErrors;
        internal static ConfigEntry<int> AutoUnlockSeconds;
        internal static ConfigEntry<int> AutoUnlockDurabilityCost;
        internal static ConfigEntry<bool> IgnoreFatigue;
        internal static ConfigEntry<bool> PerformanceEnabled;
        internal static ConfigEntry<float> RankXpBonus;
        internal static ConfigEntry<int> DurabilityRefund;
        internal static ConfigEntry<bool> TakeOverSe;
        internal static ConfigEntry<bool> SeLevelContinuity;
        internal static ConfigEntry<bool> InjectSkillSlot;
        internal static ConfigEntry<bool> DedupeActions;
        internal static ConfigEntry<bool> OutbreakEnabled;
        internal static ConfigEntry<float> OutbreakDifficulty;
        internal static ConfigEntry<float> OutbreakXpBonus;
        internal static ConfigEntry<bool> TierRewards;
        internal static ConfigEntry<int> TierErrorBonus;
        internal static ConfigEntry<int> TierDurabilityRelief;
        internal static ConfigEntry<float> TierXpBonus;
        internal static ConfigEntry<bool> PanelBonuses;
        internal static ConfigEntry<float> EliteForgivenessStep;
        internal static ConfigEntry<bool> EliteToolExempt;
        // 电子锁（门禁卡门 / 实验室卡）接管：默认全部关闭，由玩家在 F12 里自行打开
        internal static ConfigEntry<bool> KeycardAsLock;
        internal static ConfigEntry<bool> BreachEnabled;
        internal static ConfigEntry<float> BreachKickBase;
        internal static ConfigEntry<float> BreachKickStep;
        internal static ConfigEntry<float> BreachEliteKickBase;
        internal static ConfigEntry<float> BreachFractureBase;
        internal static ConfigEntry<float> BreachFractureStep;
        internal static ConfigEntry<int> BreachStrengthPerTier;
        internal static ConfigEntry<bool> HackEnabled;
        internal static ConfigEntry<string> HackToolIds;
        internal static ConfigEntry<float> HackSpeedMultiplier;
        internal static ConfigEntry<int> HackErrorBonus;
        internal static ConfigEntry<int> HackAutoSeconds;
        internal static ConfigEntry<float> HackCooldownNormal;
        internal static ConfigEntry<float> HackCooldownLab;
        internal static ConfigEntry<string> HackLabMaps;
        internal static ConfigEntry<int> HackPasswordLength;
        internal static ConfigEntry<float> HackXpRatio;
        internal static ConfigEntry<int> HackParityLevel;
        internal static ConfigEntry<float> HackPenaltyPerLevel;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind(G, "0.总开关/启用开锁", true, "关闭后本模组不再向门注入任何交互项");
            DebugLog = cfg.Bind(G, "0.总开关/调试日志", false, "详细日志（排查用）");
            LockpickIds = cfg.Bind(G, "1.工具/万能钥匙模板 id", "6a1f01aa01bb02cc03dd0001", "逗号分隔的模板 id；留空则不识别任何工具（此时请把上一项「需要工具」关掉）");
            RequireTools = cfg.Bind(G, "1.工具/需要工具", true, "关闭后任意门都可直接撬、且不消耗工具耐久（适合没有对应工具物品的环境）");
            AttemptsBeforeBreak = cfg.Bind(G, "1.工具/门锁损坏前最大失败次数", 5, "超过后该门的锁损坏，无法再开");
            PriceTiers = cfg.Bind(G, "2.开锁/按钥匙价格分级", true, "门难度按该门钥匙的贵重程度分级：钥匙越贵，对应的门越难；价格档位见 keys.json。关闭后改用 doors.json 的 mapBase / doorOverrides");
            KeyTableFile = cfg.Bind(G, "2.开锁/钥匙价表文件", "keys.json", "相对 BepInEx\\config\\com.dsh.lockpickstandalone\\ 的文件名（由 SPT 自身的手册价换算，可自行编辑）");
            SweetSpotRangeBase = cfg.Bind(G, "2.开锁/锁定区基准宽度", 0.16f, "数值越小越难（区间越窄）");
            ZoneWidthMultiplier = cfg.Bind(G, "2.开锁/区间宽度系数", 1f, "大于 1 更容易");
            PerfectZoneRatio = cfg.Bind(G, "2.开锁/完美区比例", 0.3f, "锁定区中心窄条占比，0 关闭完美判定");
            SliderSpeedMultiplier = cfg.Bind(G, "2.开锁/游标速度系数", 1f, "游标在扫描轨道上的移动速度；大于 1 更快更难");
            BonusErrors = cfg.Bind(G, "2.开锁/额外容错次数", 0, "在等级公式之上追加");
            AutoUnlockSeconds = cfg.Bind(G, "2.开锁/自动解锁秒数", 90, "0 = 关闭；挂机会自动推进并额外消耗工具耐久");
            AutoUnlockDurabilityCost = cfg.Bind(G, "2.开锁/自动解锁耐久代价", 4, "自动完成时额外扣除的工具使用次数");
            XpMultiplier = cfg.Bind(G, "3.经验/总倍率", 1.5f, "成功与失败两路统一乘，1 = 原速。发布版默认 1.5 倍（早期内测曾给 3 倍，升级过快故砍半）");
            PerfectXpBonus = cfg.Bind(G, "3.经验/每个完美锁定加成", 0.1f, "0.1 表示每个完美 +10%");
            FailureLockXpRatio = cfg.Bind(G, "3.经验/失败经验比", 0.25f, "");
            IgnoreFatigue = cfg.Bind(G, "3.经验/忽略技能疲劳", true, "同一场开锁越多经验越少的原版疲劳机制关闭");
            PerformanceEnabled = cfg.Bind(G, "4.表现/启用评级", true, "按完美锁定与连击给出 S/A/B/C 评级");
            RankXpBonus = cfg.Bind(G, "4.表现/每档评级经验加成", 0.25f, "");
            DurabilityRefund = cfg.Bind(G, "4.表现/高评级退回耐久", 1, "A/S 评级退回的工具使用次数");
            TakeOverSe = cfg.Bind(G, "7.兼容/接管技能扩展的开锁", true, "装了 SkillsExtended 时：卸掉它的开锁补丁并关闭其开锁开关，由本模组独家提供（它的其它技能不受影响）");
            SeLevelContinuity = cfg.Bind(G, "7.兼容/沿用技能扩展的门等级", true, "存在 SkillsExtended 时优先使用它登记过的门等级，难度与你以前一致；未登记的门仍走 doors.json");
            InjectSkillSlot = cfg.Bind(G, "7.兼容/注入开锁技能槽位", true, "把「开锁」技能插进游戏技能面板并使其进度可存档（原版不含该槽位，不装技能扩展时开锁进度出图即丢）；关闭后回到原版行为");
            // ---- 6.门禁卡 / 6.黑入：电子锁接管（卡门接管默认开，其余默认关）----
            KeycardAsLock = cfg.Bind(G, "6.门禁卡/按门禁锁处理", true, "默认开启。门禁卡门不再逐张列出背包里的卡，改由本模组按开锁流程接管：只给一条「开锁」，之后可走撬锁 / 骇入 / 无动作；缺工具等情况在点击时提示。关闭则回到 EFT 原生刷卡行为（逐张列出能开这扇门的卡）");
            BreachEnabled = cfg.Bind(G, "6.门禁卡/启用踹门", false, "默认关闭。打开后锁着的门可以硬踹开：成功率随每踹一次递增，撬锁等级越高越容易踹开。关闭则踹门仍走原版判定（站位不对就踹不开）");
            BreachKickBase = cfg.Bind(G, "6.门禁卡/踹门首脚成功率", 0.10f, "0.10 = 10%。撬锁越高此值越高，Lv50 封顶 0.20");
            BreachKickStep = cfg.Bind(G, "6.门禁卡/每次递增", 1.0f, "每踹失败一脚，下一脚把握的放大倍率；1.0 表示与首脚同幅度递增");
            BreachEliteKickBase = cfg.Bind(G, "6.门禁卡/精英首脚成功率", 0.50f, "撬锁 Lv51 起的首脚成功率；之后每脚 +25%，三脚封顶");
            BreachFractureBase = cfg.Bind(G, "6.门禁卡/骨折概率基准", 10f, "力量 Lv1 时的单次踹门骨折概率（百分数）。精英撬锁段不再骨折");
            BreachFractureStep = cfg.Bind(G, "6.门禁卡/骨折每级递减", 1f, "力量每跨一个台阶，骨折概率下降的百分点数");
            BreachStrengthPerTier = cfg.Bind(G, "6.门禁卡/力量台阶跨度", 5, "多少级力量算一个台阶（默认每 5 级一档）");
            HackEnabled = cfg.Bind(G, "6.黑入/启用骇入", false, "默认关闭。打开后门禁卡门的开锁流程里额外提供「骇入」路线（滚轮输密码，独立面板）");
            HackToolIds = cfg.Bind(G, "1.工具/骇入工具模板 id", "6a1f01aa01bb02cc03dd0002", "逗号分隔的模板 id；默认值即本模组自带的「骇入装置」（外观沿用金色 1GPhone，需在工作台做出来或自行补齐）");
            HackSpeedMultiplier = cfg.Bind(G, "6.黑入/滚动速度系数", 1f, "骇入滚轮转速；大于 1 更快更难");
            HackErrorBonus = cfg.Bind(G, "6.黑入/额外容错次数", 0, "在门难度之上追加的容错次数");
            HackAutoSeconds = cfg.Bind(G, "6.黑入/自动解锁秒数", 0, "0 = 关闭；骇入的自动破解通道，耗时远长于撬锁（骇入本身是安静的、不惊动 AI；「自动破解的噪声」尚未实现）");
            HackCooldownNormal = cfg.Bind(G, "6.黑入/全局冷却秒数", 30f, "骇入成功开一次门后进入的冷却秒数（全局，全场任何门都不能再骇入）。0 = 不冷却。撬锁与踹门不受影响");
            HackCooldownLab = cfg.Bind(G, "6.黑入/实验室冷却秒数", 150f, "命中实验室地图 id 时改用这一档（2~3 分钟）。实验室是战利品核心区，冷却更长，逼玩家规划而不是推门就进");
            HackLabMaps = cfg.Bind(G, "6.黑入/实验室地图 id", "laboratory,lab,the_lab,reserve,shoreline", "逗号分隔的地图 id 列表；对当前地图做大小写不敏感的包含匹配，命中任一即按实验室算");
            HackPasswordLength = cfg.Bind(G, "6.黑入/密码位数", 5, "电子锁密码长度，3~8 位有效。位数越多滚轮越宽");
            HackXpRatio = cfg.Bind(G, "6.黑入/骇入经验倍率", 3f, "骇入相对撬锁的经验倍率；3 = 撬锁的 3 倍。配合全局冷却，让两条路线各有位置");
        HackParityLevel = cfg.Bind(G, "6.黑入/难度追平等级", 25, "撬锁练到这一级时，骇入的手感才追平「撬锁 1 级」。调大 = 骇入整体更难（越低越难骇入）");
        HackPenaltyPerLevel = cfg.Bind(G, "6.黑入/等级惩罚斜率", 2f, "每低于「难度追平等级」1 级，滚轮转速再快这个百分比（默认 2）。调大 = 低级时更难，转速上限锁死在 1.6 倍");

            DedupeActions = cfg.Bind(G, "7.兼容/清理重复的交互项", true, "第三方模组在门禁卡门上会把同一条交互项追加两次；开启后同一段里同名的重复项只保留一条");
            OutbreakEnabled = cfg.Bind(G, "5.联动/疫潮环境联动", true, "检测到疫潮环境时提高难度并补偿经验");
            OutbreakDifficulty = cfg.Bind(G, "5.联动/疫潮难度加成", 0.25f, "");
            OutbreakXpBonus = cfg.Bind(G, "5.联动/疫潮经验加成", 0.5f, "");
            TierRewards = cfg.Bind(G, "6.技能台阶/启用台阶奖励", true, "每 10 级一个台阶：额外容错、自动解锁耐久减免、评级经验加成；关闭后退回纯曲线（只剩扫描速度与锁定区宽度随等级变化）");
            TierErrorBonus = cfg.Bind(G, "6.技能台阶/每档额外容错", 1, "每个台阶追加的容错次数（Lv10 一档 / Lv20 二档，以此类推）");
            TierDurabilityRelief = cfg.Bind(G, "6.技能台阶/每档自动解锁耐久减免", 1, "每个台阶减少的自动解锁耐久代价，最低不低于 1");
            TierXpBonus = cfg.Bind(G, "6.技能台阶/每档评级经验加成", 0.05f, "每个台阶追加的评级经验加成，0.05 表示 +5%");
            PanelBonuses = cfg.Bind(G, "6.技能台阶/面板显示等级加成", true, "在局外技能面板的「开锁」行下方显示一排加成图标（悬停出中文说明）；关闭后退回原版的空加成行");
            // 值域钳位不在 BepInEx 的 AcceptableValueRange 上做（BepInEx 5.4 的 Bind 没有
            // (default, desc, params range) 那个重载），统一由 LockpickLevel.MaxLevel 兜底钳到 50~70。
            EliteToolExempt = cfg.Bind(G, "6.技能台阶/精英工具免磨损", true, "Lv51 起开锁工具不再消耗使用次数；关闭后退回照常磨损");
        }

        /// <summary>
        /// 历史上存在过、但功能已被删除的配置键（终端黑入整条线、检视经验比）。
        /// BepInEx 的只补不删策略会把读不到的旧键留在 cfg 里，用户看到一堆按不动的死键
        /// （最久远的那个记着「开关=true」却毫无作用）。
        /// 注意：ConfigFile.OrphanedEntries 是 internal，外部取不到；
        /// 这里先把旧键 Bind 进内存让其变成「受管条目」，再 Remove + Save，
        /// 从而真正从磁盘 cfg 中移除；异常一律吞掉，不得影响加载与开锁功能。
        /// 【限定】该表只放「功能已删除、值没有去处」的键；**改了名的键必须走下面的
        /// Renamed 迁移**，否则外部用户升级时会被连值一起删掉（发布包里不含 cfg，
        /// 迁移只能在运行期做）。
        /// </summary>
        private static readonly string[] DeadKeys =
        {
            "3.经验/检视经验比",
            "6.黑入/启用终端黑入",
            "6.黑入/自动解锁耐久代价"
        };

        /// <summary>
        /// 改过名的配置键：{ 旧键, 新键, 默认值 }。先把旧值迁到新键，再删旧键，
        /// 用户调过的值不会因为改名而丢失。
        /// </summary>
        private static readonly object[][] Renamed =
        {
            new object[] { "2.开锁/甜区基准宽度", "2.开锁/锁定区基准宽度", 0.16f },
            new object[] { "2.开锁/滑条速度系数", "2.开锁/游标速度系数", 1f },
        };

        private const float NoOldValue = -1f;   // 哨兵：两个迁入键的合法值域都不含负数

        internal static void PruneDeadKeys(ConfigFile cfg)
        {
            try
            {
                int removed = 0;
                for (int i = 0; i < DeadKeys.Length; i++)
                {
                    string key = DeadKeys[i];
                    // 先 Bind 一次：已存在则采纳旧值、缺失则写入默认，两种情况下都成为受管条目
                    var entry = cfg.Bind(G, key, string.Empty, string.Empty);
                    if (cfg.Remove(entry.Definition)) removed++;
                }

                int migrated = 0;
                for (int i = 0; i < Renamed.Length; i++)
                {
                    string oldKey = (string)Renamed[i][0];
                    string newKey = (string)Renamed[i][1];
                    float fallback = (float)Renamed[i][2];

                    var probe = cfg.Bind(G, oldKey, NoOldValue, string.Empty);
                    bool hadOld = probe.Value != NoOldValue;
                    if (hadOld)
                    {
                        // 旧键有值 ⇒ 迁到新键（客户端 cfg 里新键此时已由 Bind() 建好）
                        cfg.Bind(G, newKey, fallback, string.Empty).Value = probe.Value;
                        migrated++;
                    }
                    if (cfg.Remove(probe.Definition)) removed++;
                }

                if (removed > 0)
                {
                    cfg.Save();
                    LpLog.Info("[Lockpick] 已清理 " + removed + " 个历史遗留配置键（对应功能已删除），"
                        + "另迁移 " + migrated + " 项改名键的旧值");
                }
            }
            catch (Exception ex)
            {
                LpLog.Warn("[Lockpick] 清理历史遗留配置键失败（不影响功能）：" + ex.Message);
            }
        }

        internal static float SP => SweetSpotRangeBase.Value;
        internal static float FailureRatio => FailureLockXpRatio.Value;
        internal static int Attempts => AttemptsBeforeBreak.Value;

        // 台阶三件套：开关关闭时一律归零，实现「一键退回纯曲线」。
        internal static float TierErrorPerStep => TierRewards.Value ? TierErrorBonus.Value : 0f;
        internal static float TierDurabilityPerStep => TierRewards.Value ? TierDurabilityRelief.Value : 0f;
        internal static float TierXpPerStep => TierRewards.Value ? TierXpBonus.Value : 0f;
    }
}