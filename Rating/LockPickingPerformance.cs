// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using EFT;
using LockpickStandalone.Interaction;

namespace LockpickStandalone.Rating;

/// <summary>
/// 开锁表现与疫潮联动的汇总入口。
///
/// 职责分为两块，互相独立：
///   一、表现系统：统计连击与本局战绩，按完美次数与连击给出评级，
///       并据此产出经验倍率与耐久退回；
///   二、疫潮联动：检测到 CONTAGION 时调整三项系数（速度、锁定区宽度、收益）。
///
/// 两块各自受独立开关控制：关掉表现提示不会连带关掉疫潮玩法，反之亦然。
/// 全程纯客户端计算，不写服务端数据。
/// </summary>
public static class LockPickingPerformance
{
    /// <summary>上一次结算时的玩家实例；换人即视为换局，所有统计归零。</summary>
    private static Player _scopedPlayer;

    /// <summary>当前连击数：连续成功且至少含一次完美锁定的次数。</summary>
    private static int _combo;

    /// <summary>本局累计成功开锁次数。</summary>
    private static int _raidPicks;

    /// <summary>本局累计完美锁定次数。</summary>
    private static int _raidPerfects;

    /// <summary>已经弹过疫潮提示的玩家实例，保证每个战局只提示一次。</summary>
    private static Player _hintedPlayer;

    /// <summary>评级档位由低到高的序号：0=C、1=B、2=A、3=S。</summary>
    private const int RankCount = 4;

    /// <summary>连击上限，防止极端情况下数值溢出，也让界面显示不至于过长。</summary>
    private const int ComboCap = 99;

    /// <summary>当前连击数（供界面读取）。</summary>
    public static int Combo => _combo;

    /// <summary>表现系统开关：控制评级提示、评级经验加成与 A/S 档的耐久退回。</summary>
    private static bool Enabled => LpConfig.PerformanceEnabled.Value;

    /// <summary>
    /// 疫潮联动是否生效：需要开关打开，且磁盘上确实存在 CONTAGION。
    /// 两者缺一即视为未联动。
    /// </summary>
    private static bool OutbreakLinked => LpConfig.OutbreakEnabled.Value && ContagionPresence.IsOutbreakEnvironment;

    /// <summary>
    /// 确认当前统计归属于本战局。
    /// 依据玩家实例而非局内计时：换局会换 Player 实例，以此判定比分界最省事也最可靠。
    /// </summary>
    private static void ScopeToRaid(Player player)
    {
        if (ReferenceEquals(player, _scopedPlayer))
            return;

        _scopedPlayer = player;
        _combo = 0;
        _raidPicks = 0;
        _raidPerfects = 0;
    }

    /// <summary>疫潮环境下的滑条速度系数。1 表示与常规环境一致。</summary>
    public static float OutbreakSpeedMultiplier
    {
        get
        {
            if (!OutbreakLinked)
                return 1f;
            return 1f + LpConfig.OutbreakDifficulty.Value;
        }
    }

    /// <summary>
    /// 疫潮环境下的锁定区宽度系数。1 表示与常规环境一致；
    /// 下限 0.2，避免配置极端时锁定区缩到无法点击。
    /// </summary>
    public static float OutbreakSweetSpotMultiplier
    {
        get
        {
            if (!OutbreakLinked)
                return 1f;
            return Math.Max(0.2f, 1f - LpConfig.OutbreakDifficulty.Value);
        }
    }

    /// <summary>疫潮环境下的经验收益系数。1 表示与常规环境一致。</summary>
    public static float OutbreakXpMultiplier
    {
        get
        {
            if (!OutbreakLinked)
                return 1f;
            return 1f + LpConfig.OutbreakXpBonus.Value;
        }
    }

    /// <summary>
    /// 由「本次完美次数」与「结算前的连击」推出评级档位。
    /// 完美次数是主因，连击是放大器：同样的完美次数，连击越高越容易冲高档。
    /// </summary>
    private static int ResolveRank(int perfectHits, int comboBefore)
    {
        if (perfectHits >= 3)
            return RankCount - 1;                     // S：单把三次完美
        if (perfectHits == 2)
            return 2;                                 // A：单把两次完美
        if (perfectHits == 1)
            return comboBefore >= 2 ? 2 : 1;          // A（有连击）/ B（无连击）

        // 本把一个完美都没有：有连击说明是断档，给个 B 保住手感
        return comboBefore >= 2 ? 1 : 0;              // B（有连击）/ C（无连击）
    }

    /// <summary>评级序号转显示字母。</summary>
    private static string RankLetter(int index)
    {
        switch (index)
        {
            case 3: return "S";
            case 2: return "A";
            case 1: return "B";
            default: return "C";
        }
    }

    /// <summary>
    /// 开锁成功后的结算入口：更新连击与战绩、给出评级、返回最终经验倍率。
    /// </summary>
    /// <param name="owner">开锁者，用于弹出提示。</param>
    /// <param name="perfectHits">本把锁中的完美锁定次数。</param>
    /// <param name="rank">输出评级字母。</param>
    /// <param name="durabilityRefund">输出本次应退回的开锁工具耐久次数。</param>
    /// <returns>最终经验倍率（已含评级、技能台阶与疫潮收益）。</returns>
    public static float ReportSuccess(GamePlayerOwner owner, int perfectHits, out string rank, out int durabilityRefund)
    {
        Player player = owner?.Player;

        // 输出参数必须无条件赋值，调用方不做 null 检查
        rank = "C";
        durabilityRefund = 0;

        // 拿不到玩家就只返回疫潮倍率：宁可信息少，也不能在结算路径上抛异常
        if (player == null)
            return OutbreakXpMultiplier;

        ScopeToRaid(player);

        int comboBefore = _combo;
        int rankIndex = ResolveRank(perfectHits, comboBefore);

        // 只有含完美锁定的成功才续连击；一次完美都没有则断连击
        if (perfectHits > 0)
        {
            _combo = Math.Min(_combo + 1, ComboCap);
            _raidPerfects += perfectHits;
        }
        else
        {
            _combo = 0;
        }
        _raidPicks++;

        rank = RankLetter(rankIndex);

        // 评级加成与技能台阶加成叠在同一层里，再整体乘疫潮收益系数
        float tierBonus = LockpickEnv.TierXpBonus();
        float ratingBonus = Enabled ? LpConfig.RankXpBonus.Value * rankIndex + tierBonus : 0f;
        float multiplier = (1f + ratingBonus) * OutbreakXpMultiplier;

        if (Enabled)
        {
            // A 档（S 档自然也满足 idx >= 2）起才退回耐久
            int configuredRefund = LpConfig.DurabilityRefund.Value;
            durabilityRefund = rankIndex >= 2 ? Math.Max(0, configuredRefund) : 0;

            // 提示串按「评级 → 完美 → 连击 → 倍率 → 台阶 → 退回 → 疫潮 → 本局战绩」拼装，
            // 每段都只在有内容时追加，避免出现空的括号或多余的竖线
            string comboTag = _combo > 1 ? LpText.ComboTag(_combo) : string.Empty;
            string tierTag = tierBonus > 0f
                ? LpText.TierXpTag((tierBonus * 100f).ToString("0"))
                : string.Empty;
            string refundTag = durabilityRefund > 0 ? LpText.RefundTag(durabilityRefund) : string.Empty;
            string outbreakTag = OutbreakXpMultiplier > 1f
                ? LpText.OutbreakXpTag(OutbreakXpMultiplier.ToString("0.##"))
                : string.Empty;

            owner.DisplayPreloaderUiNotification(
                LpText.RankTag(rank) + " " + LpText.PerfectTag(perfectHits) + comboTag
                + LpText.XpTag(multiplier.ToString("0.##")) + tierTag + refundTag + outbreakTag
                + LpText.RaidTag(_raidPicks, _raidPerfects));
        }

        LpLog.LogInfo($"[开锁表现] rank={rank} perfect={perfectHits} combo={_combo} "
            + $"xpMul={multiplier:0.###} refund={durabilityRefund} raid={_raidPicks}/{_raidPerfects}");

        return multiplier;
    }

    /// <summary>开锁失败：连击清零，并在连击曾大于 1 时给出断连提示。</summary>
    public static void ReportFailure(GamePlayerOwner owner)
    {
        Player player = owner?.Player;
        if (player == null)
            return;

        ScopeToRaid(player);

        if (_combo > 1 && Enabled)
            owner.DisplayPreloaderUiNotification(LpText.ComboBreak(_combo));

        _combo = 0;
    }

    /// <summary>
    /// 取本局的疫潮环境说明文案。
    /// 以「首次调用」为界返回一次，之后返回 null，保证每个战局只提示一次。
    /// </summary>
    public static string TakeOutbreakHint(GamePlayerOwner owner)
    {
        if (!Enabled || !OutbreakLinked)
            return null;

        Player player = owner?.Player;
        if (player == null || ReferenceEquals(player, _hintedPlayer))
            return null;

        _hintedPlayer = player;
        float difficultyPct = LpConfig.OutbreakDifficulty.Value;
        float xpPct = LpConfig.OutbreakXpBonus.Value;
        return LpText.OutbreakHint(difficultyPct * 100f, xpPct * 100f);
    }
}
