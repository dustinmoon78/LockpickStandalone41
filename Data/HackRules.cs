// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using UnityEngine;

namespace LockpickStandalone.Data
{
    /// <summary>
    /// 骇入（电子破解）规则纯函数层：冷却、实验室判定、收益、滚轮转速、密码生成、受击代价。
    /// </summary>
    /// <remarks>
    /// 【与 BreachRules 的分工】BreachRules 管踹门（物理），本类管骇入（电子）。
    /// 两者共用同一套撬锁等级与力量等级，但代价体系不同：踹门伤腿，骇入不伤人只吃时间与冷却。
    ///
    /// 【为什么全做成纯函数】不读 GameWorld、不掷随机、不持有可变状态，
    /// 因此可以脱离游戏直接回归验证。全局冷却状态由调用方用一个时间戳字段保存，
    /// 本类只回答「还要等多久」。
    /// </remarks>
    internal static class HackRules
    {
        // ------------------------------------------------------------ 常量与收益

        /// <summary>默认的实验室地图 id 列表（逗号分隔，大小写不敏感包含匹配）。</summary>
        internal const string DefaultLabMaps = "laboratory,lab,the_lab,reserve,shoreline";


        /// <summary>力量满级。EFT 原版所有技能都可点满 50 级进入精英段，力量也不例外。</summary>
        internal const int MaxStrengthLevel = 50;

        /// <summary>撬锁精英段起点（本模组把撬锁等级上限开到 70，见配置「等级上限」）。</summary>
        internal const int EliteLevel = 51;

        /// <summary>
        /// 骇入最终经验倍率（已含路线倍数）。
        /// ★骇入一路读 F12 的「6.黑入/骇入经验倍率」；撬锁固定 1×（它的收益调节走全局的
        ///   「3.经验/总倍率」）。这两个数曾经一处是常量、一处是配置各写一份，
        ///   结果是玩家在界面上改骇入倍率毫无效果 —— 同一个真值绝不能有两个来源。
        /// </summary>
        internal static float XpRatio(bool hacking)
        {
            if (!hacking) return 1f;
            float v = LpConfig.HackXpRatio.Value;
            return v > 0f ? v : 1f;
        }

        // ------------------------------------------------------------ 受击代价（力量轴）

        /// <summary>
        /// 是否免伤：撬锁已达精英段（Lv51+）且力量已满（Lv50）。
        /// </summary>
        /// <remarks>
        /// 这一对条件都是玩家实际可达的：撬锁是我们自己开到 70 的，力量满 50 即原版精英段。
        /// 达成后踹门只剩震荡与掉体力，不再掉血也不再骨折 —— 练到顶的成就感落地。
        /// </remarks>
        internal static bool IsImmune(int lockpickLevel, int strengthLevel)
        {
            return lockpickLevel >= EliteLevel && strengthLevel >= MaxStrengthLevel;
        }

        /// <summary>按门难度与力量等级算这一次受多少血（0 表示无伤）。</summary>
        /// <param name="doorLevel">门难度 1~5，越硬的门掉得越多。</param>
        internal static float BloodLoss(int strengthLevel, int doorLevel)
        {
            int s = Mathf.Clamp(strengthLevel, 1, MaxStrengthLevel);
            // 力量每过 5 级减一档伤，Lv50 归零 —— 与骨折概率同一套阶梯，手感一致。
            int tier = (s - 1) / 5;
            if (tier >= (MaxStrengthLevel - 1) / 5) return 0f;
            float[] byDoor = { 20f, 15f, 12f, 9f, 6f };
            int idx = Mathf.Clamp(doorLevel, 1, 5) - 1;
            return byDoor[idx] * (1f - tier / 9f);
        }

        // ------------------------------------------------------------ 实验室判定

        /// <summary>
        /// 当前所在地图是否算实验室环境。对 mapId 与配置列表做大小写不敏感的包含匹配。
        /// mapId 为空一律判否（宁可按普通环境算，也不误触长冷却）。
        /// </summary>
        internal static bool IsLab(string mapId, string labMapList)
        {
            if (string.IsNullOrEmpty(mapId)) return false;
            string list = string.IsNullOrEmpty(labMapList) ? DefaultLabMaps : labMapList;
            string lower = mapId.ToLowerInvariant();
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i].Trim().ToLowerInvariant();
                if (key.Length > 0 && lower.Contains(key)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ 冷却

        /// <summary>本次开门应触发的冷却时长（秒）。isLab 决定用哪一档；配置为 0 则不冷却。</summary>
        internal static float CooldownFor(bool isLab, float normalSeconds, float labSeconds)
        {
            float v = isLab ? labSeconds : normalSeconds;
            return v < 0f ? 0f : v;
        }

        /// <summary>剩余冷却秒数；小于等于 0 表示不在冷却中。</summary>
        internal static float Remaining(float cooldownUntil, float now)
        {
            float left = cooldownUntil - now;
            return left > 0f ? left : 0f;
        }

        /// <summary>把秒数格式化成「2分18秒」这样的中文短串，用于交互项提示。</summary>
        internal static string FormatRemaining(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            if (s < 60) return s + "秒";
            int m = s / 60;
            int rest = s % 60;
            return rest > 0 ? (m + "分" + rest + "秒") : (m + "分");
        }

        // ------------------------------------------------------------ 滚轮难度

        // ------------------------------------------------------------ 等级门槛

        /// <summary>
        /// 骇入难度追平等级（默认 25，可由 F12 调）。
        /// 用户定稿：「等级越低，越难骇入，25 级是撬锁 1 级水平」——
        /// 即练到这一级，骇入的手感才追平「撬锁 1 级」。
        /// </summary>
        internal static int ParityLevel()
        {
            int v = LpConfig.HackParityLevel != null ? LpConfig.HackParityLevel.Value : 25;
            if (v < 2) return 2;
            return v > 70 ? 70 : v;
        }

        /// <summary>骇入的等效撬锁等级：整体下移 (追平等级 - 1) 级，负数按 0 级算。</summary>
        internal static int HackEquivalentLevel(int lockpickLevel)
        {
            int eff = lockpickLevel - (ParityLevel() - 1);
            return eff < 0 ? 0 : eff;
        }

        /// <summary>惩罚倍率的硬上限。不论斜率调多大，转速都不会超过基准的这个倍数。</summary>
        internal const float PenaltyCap = 1.6f;

        /// <summary>
        /// 追平等级以下的额外难度惩罚：每低 1 级，滚轮转速再快「斜率」个百分点
        /// （F12「6.黑入/等级惩罚斜率」，默认 2），封顶 1.6 倍。
        ///
        /// 默认 2% 下：25 级 → 1.000，24 级 → 1.020，10 级 → 1.300，1 级 → 1.480。
        ///
        /// ★为什么非要有这一项：等效等级在追平等级以下会一路钳到 0，而宽容度曲线在 0 级那里是平的
        /// （0.024 上下），单靠宽容度会得到「1 级和 24 级一样难」—— 正好违背「等级越低越难骇入」。
        ///
        /// 【斜率的取值经过三轮】
        ///   1%  → 1 级只到 1.24，实机体感 1 级与 24 级几乎没区别（判定窗口只差 23%）；
        ///   2.5%→ 拉开到 1.56 倍，用户看过数值后定为 2%；
        ///   2%  → 拉开到 1.45 倍，是当前默认。
        /// 判定窗口（含每列 ±30% 抖动的最快列，默认 2%）：
        ///   撬锁 Lv1  → 门Lv1 252ms / 门Lv5 176ms（最快列 194 / 136ms）
        ///   撬锁 Lv10 → 门Lv1 287ms / 门Lv5 201ms
        ///   撬锁 Lv24 → 门Lv1 365ms / 门Lv5 256ms
        ///   撬锁 Lv25 → 门Lv1 376ms / 门Lv5 263ms（追平点）
        ///
        /// ★封顶是必需的不是保守：斜率调到 4%（且不封顶到 1.9）会让「1 级打最高难门」掉到
        ///   137ms / 最快列 106ms —— 低于人手可反应的底线，那是不可玩而不是难。
        ///   所以斜率给了玩家自己调，但上限锁死在 1.6 倍。
        /// </summary>
        internal static float HackPenalty(int lockpickLevel)
        {
            int below = ParityLevel() - lockpickLevel;
            if (below <= 0) return 1f;
            float perLevel = LpConfig.HackPenaltyPerLevel != null ? LpConfig.HackPenaltyPerLevel.Value : 2f;
            if (perLevel < 0f) perLevel = 0f;
            float p = 1f + below * (perLevel / 100f);
            return p > PenaltyCap ? PenaltyCap : p;
        }

        /// <summary>
        /// 本次骇入每一列的滚轮转速（圈/秒，一圈 = 高亮格走完整列一次）。
        /// 门难度抬高基速，撬锁等级（宽容度）压下它 —— 与撬锁面板共用同一条成长曲线。
        ///
        /// 判定带宽约 ±0.12，即一圈里约 24% 是可点窗口，故 窗口毫秒 ≈ 240 / 圈每秒。
        /// 实算（已含每列 ±30% 抖动后的**最快列**，那才是真正的下限）：
        ///   撬锁 Lv1  × 门 Lv1 → 0.64 圈/秒、窗口 375ms（最快列 289ms）
        ///   撬锁 Lv1  × 门 Lv5 → 0.91 圈/秒、窗口 263ms（最快列 202ms）
        ///   撬锁 Lv50 × 门 Lv5 → 0.62 圈/秒、窗口 387ms（最快列 298ms）
        ///   撬锁高等级 × 门 Lv1 → 0.39 圈/秒、窗口 612ms（最快列 471ms）
        /// 全部落在 200~612ms —— 反应得过来但不从容，正是想要的手感。
        ///
        /// ★soften 用**线性**映射，不用 1/forgiveness：后者在低等级会爆炸（Lv1 的宽容度只有
        ///   0.024，1/F = 41 倍），只能靠 clamp 压住，而任何 clamp 上限都会把一整段等级压成
        ///   同一档手感（试过上限 1.5 与 1.15，分别使 Lv0~Lv27 / Lv0~Lv35 完全一样）。
        ///   线性式在 0~1.2 全程单调可辨。
        /// </summary>
        internal static float ReelSpeed(int doorLevel, float forgiveness, float speedMultiplier)
        {
            float baseSpeed = 0.50f + doorLevel * 0.06f;                              // 门 Lv1 0.56 → 门 Lv5 0.80
            float soften = 1.15f - 0.45f * Mathf.Clamp01(forgiveness / 1.2f);         // 0.70 ~ 1.15，全程单调
            float mul = speedMultiplier <= 0f ? 1f : speedMultiplier;
            return Mathf.Max(0.12f, baseSpeed * soften * mul);
        }

        /// <summary>密码长度（可由配置下调，上限 8 位免得面板太宽）。</summary>
        internal static int PasswordLength(int configured)
        {
            if (configured < 3) return 5;
            return configured > 8 ? 8 : configured;
        }

        /// <summary>密码可用的字符集（数字 + 常用字母 + 符号，刻意避开易混淆的 0/O 与 1/l/I）。</summary>
        internal static readonly char[] Charset =
        {
            '2', '3', '4', '5', '6', '7', '8', '9',
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'M', 'N', 'P', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
            '#', '%', '&', '*', '?', '@',
        };

        // 说明：早期设计里还有一套「按种子给每扇门派生固定密码、玩家输入密码」的玩法，
        // 对应 GeneratePassword / KeyOf 两个函数。定稿的玩法是**滚轮对齐** ——
        // 列在滚动、玩家在正确时机点中判定带，根本不存在「预设密码」这个对象，
        // 两个函数因此从未被调用。留着只会让人以为这儿真有一张密码表，故删除。
    }
}
