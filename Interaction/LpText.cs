// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using EFT;
using UnityEngine;

namespace LockpickStandalone.Interaction
{
    /// <summary>
    /// 面向玩家的文案层（中 / 英双轨）。
    /// 语言只解析一次并缓存：优先读游戏自身的界面语言（EFT.LocalizationManager.Culture），
    /// 读不到时回落到「UI 字体是不是 Arial」这一旧判据，再失败则按中文处理。
    /// 说明：本模组不打包任何语言文件、不注册任何 locale 键，全部为自写文案，故中英两套都在本文件内联。
    /// 日志（BepInEx LogOutput.log / 服务端日志）不走这里，保持中文，便于与既有排查记录对账。
    /// </summary>
    internal static class LpText
    {
        // EFT 现有语言码（LocalizationManager 内部映射）：ru / en / fr / ge / ch / jp / kr。
        // 不在这个集合里的取值一律当「还没读到真语言」——GameSettingsGroup 的 Language 默认值
        // 是按系统语言猜的、要等后端下发才被覆盖，所以「非空」不等于「可信」。
        private static readonly string[] Known = { "ru", "en", "fr", "ge", "ch", "jp", "kr" };

        private static bool? _chinese;
        private static bool _localeResolved;
        private static int _resolveCalls;
        private static bool _overridden;
        private static float _nextRetryAt;

        /// <summary>界面是否中文。解析到可信取值后固定；读不到时随字体兜底，并允许再重试两次。</summary>
        internal static bool Chinese
        {
            get
            {
                if (_chinese.HasValue) return _chinese.Value;
                _chinese = Resolve();
                return _chinese.Value;
            }
        }

        /// <summary>
        /// 丢掉缓存、允许重新解析（由面板的每帧回调调用；已确定或已被字体兜底则不再重试）。
        /// 按【时间窗】限流而不是按调用次数——面板每帧都会调它，按次数会在 3 帧内耗尽机会，
        /// 而「后端语言表还没下发」这个状态可能要等若干秒。最多 5 次、每次间隔 5 秒。
        /// </summary>
        internal static void Invalidate()
        {
            if (_localeResolved || _overridden) return;
            if (_resolveCalls >= 5) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextRetryAt) return;
            _nextRetryAt = now + 5f;
            _chinese = null;
        }

        /// <summary>界面语言是否真的从游戏里读到了（false 表示当前值来自字体兜底）。</summary>
        internal static bool LocaleResolved { get { var _ = Chinese; return _localeResolved; } }

        /// <summary>供 UI 构建器在字体已确定时覆盖判定（仅在读不到可信语言时才有意义，且只认第一次）。</summary>
        internal static void Override(bool chinese)
        {
            if (_localeResolved || _overridden) return;
            _overridden = true;
            _chinese = chinese;
        }

        private static bool Resolve()
        {
            _resolveCalls++;
            try
            {
                var mgr = LocalizationManager.Instance;
                if (mgr != null)
                {
                    string culture = mgr.Culture;
                    if (IsTrusted(culture))
                    {
                        // 只有落在已知语言集合里才认账；否则可能是后端尚未下发时的系统语言猜测值
                        _localeResolved = true;
                        return IsChinese(culture);
                    }
                }
            }
            catch { /* 读不到游戏语言不是错误：下面的字体兜底会接手 */ }
            return true;   // 最终兜底=中文（本模组的母语环境）
        }

        /// <summary>取值是否落在 EFT 的已知语言集合里。</summary>
        private static bool IsTrusted(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return false;
            for (int i = 0; i < Known.Length; i++)
                if (culture.Equals(Known[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>已知语言码是否中文界面。zh* 也一并认（游戏若改用带地区的码同样成立）。</summary>
        private static bool IsChinese(string culture)
        {
            return culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                || culture.Equals("ch", StringComparison.OrdinalIgnoreCase);
        }

        private static string Pick(string chinese, string english) { return Chinese ? chinese : english; }

        // ---- 交互项（LpStrings 使用） ----
        internal static string PickLock { get { return Pick("撬锁", "Pick Lock"); } }
        internal static string CannotOpen { get { return Pick("此门无法开启", "This door cannot be opened"); } }
        internal static string LevelPrefix { get { return Pick("门难度 Lv.", "Difficulty Lv."); } }

        // ---- 踹门提示（BreachKickPatch）----
        internal static string BreachWrongAngle { get { return Pick("站位不对，要正对门才能踹开", "Stand facing the door to kick it"); } }
        internal static string BreachFail(int lockLv, int strengthLv, int attempt, float chance, float next)
        {
            return Chinese
                ? ("踹门没开（第 " + attempt + " 次，把握 " + Pct(chance) + "），下一脚涨到 " + Pct(next)
                   + "　力量 Lv" + strengthLv + " / 撬锁 Lv" + lockLv)
                : ("Kick failed (attempt " + attempt + ", " + Pct(chance) + " chance); next " + Pct(next)
                   + "　Str " + strengthLv + " / Pick " + lockLv);
        }
        internal static string BreachOpen(int attempt)
        {
            return Chinese ? ("门被踹开了（第 " + attempt + " 脚）") : ("Door kicked open (kick " + attempt + ")");
        }
        internal static string BreachFractured
        {
            get { return Pick("腿被踹伤了", "Your leg took the hit"); }
        }
        /// <summary>把 0~1 的概率显示成整百分数。</summary>
        private static string Pct(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }

        // ---- 开锁流程提示（LockpickActions） ----
        internal static string NoTool { get { return Pick("背包里没有可用的开锁工具", "No usable lockpick in your inventory"); } }
        internal static string NoUsableKeycard { get { return Pick("现在无法用门卡开门", "Cannot use the keycard right now"); } }
        internal static string MustStandStill { get { return Pick("移动中无法撬锁", "Stop moving to pick this lock"); } }
        internal static string UnregisteredDoor { get { return Pick("此门未登记难度（可在 keys.json 或 doors.json 中补录）", "No difficulty registered for this door (add it in keys.json or doors.json)"); } }
        internal static string SweetSpotNotReady { get { return Pick("锁定区参数未就绪，稍后再试", "Lock zone not ready yet, try again shortly"); } }
        internal static string LockDestroyed { get { return Pick("这把锁已经废了", "This lock is already broken"); } }
        internal static string LockBroke { get { return Pick("锁被撬坏了", "The lock has been forced and broken"); } }
        internal static string AttemptsLeft(int n) { return Chinese ? ("本门还剩 " + n + " 次机会") : (n + " attempt(s) left on this door"); }
        internal static string PickSuccess(string rank) { return Chinese ? ("开锁成功（评级 " + rank + "）") : ("Unlocked (rank " + rank + ")"); }
        internal static string PickFailed { get { return Pick("开锁失败", "Lockpicking failed"); } }

        // ---- 骇入流程提示（LockpickActions） ----
        internal static string Hack { get { return Pick("骇入", "Hack"); } }
        internal static string NoHackTool { get { return Pick("背包里没有可用的骇入装置", "No hacking device in your inventory"); } }
        internal static string HackDisabled { get { return Pick("骇入未启用（F12：6.黑入/启用骇入）", "Hacking is disabled (F12: 6.黑入/启用骇入)"); } }
        internal static string HackSuccess(string rank) { return Chinese ? ("骇入成功（评级 " + rank + "）") : ("Access granted (rank " + rank + ")"); }
        internal static string HackFailed { get { return Pick("骇入失败，可以立刻再试", "Hack failed — you may try again right away"); } }

        // ---- 小游戏面板（DeltaLockGame） ----
        internal static string AutoUnlockUnavailable { get { return Pick("自动解锁不可用：工具耐久不足", "Auto unlock unavailable: not enough tool durability"); } }
        internal static string ClickToLock { get { return Pick("单击锁定白色区间", "Click to lock the white zone"); } }
        internal static string ExitHint { get { return Pick("Esc / 右键退出", "Esc / RMB to exit"); } }
        internal static string PerfectLock { get { return Pick(">>完美锁定!", ">>PERFECT LOCK!"); } }
        internal static string AutoInProgress { get { return Pick(">>自动解锁同步进行中", ">>AUTO UNLOCK IN PROGRESS"); } }
        internal static string UnlockInProgress { get { return Pick(">>解锁同步进行中", ">>UNLOCK IN PROGRESS"); } }
        internal static string ErrorsLeft(int n) { return Chinese ? ("剩余容错: " + n) : ("Tolerance left: " + n); }
        internal static string TierUpBanner(string banner) { return Pick("[开锁技能台阶] ", "[Lockpick tier] ") + banner; }

        // ---- 技能台阶（LockpickEnv.TierBanner） ----
        internal static string TierNone(int maxTier)
        {
            return Pick("技能台阶 0/" + maxTier + "（Lv10 起生效）", "Skill tier 0/" + maxTier + " (starts at Lv10)");
        }
        internal static string TierBanner(int tier, int maxTier, int level, float err, float dur, float xpPct)
        {
            if (Chinese)
                return "技能台阶 " + tier + "/" + maxTier + "（Lv" + level + "）· 容错 +" + err.ToString("0.#")
                    + " · 自动解锁代价 -" + dur.ToString("0.#") + " · 评级经验 +" + xpPct.ToString("0") + "%";
            return "Skill tier " + tier + "/" + maxTier + " (Lv" + level + ") · Tolerance +" + err.ToString("0.#")
                + " · Auto-unlock cost -" + dur.ToString("0.#") + " · Rank XP +" + xpPct.ToString("0") + "%";
        }

        // ---- 技能面板加成行（Patches\SkillBuffTextPatch 按 Skill.Id==Lockpicking 取文）----
        // 格式风格照抄原版 BuffIcon.GetBuffDescription（BuffIcon.cs:34-43）：方括号会被上色，
        // 比例型数值沿用原版的 {0:0.#%} 写法。
        // ★绝不改 locale 键：复用的 EBuffId 同时属于原版别的技能，改键会连带改坏 Strength 等 43 个技能的文案。
        internal static string BonusSlider(float v)
        {
            return Chinese
                ? string.Format("[游标速度降低 {0:0.#%}]（扫描变慢，更容易命中）", v)
                : string.Format("[Cursor speed -{0:0.#%}] (slower sweep, easier to hit)", v);
        }
        internal static string BonusZone(float v)
        {
            return Chinese
                ? string.Format("[锁定区宽度 ×{0:0.#}]（宽容度越高，区间越宽）", v)
                : string.Format("[Lock zone width x{0:0.#}] (wider = more forgiving)", v);
        }
        internal static string BonusErrors(float v)
        {
            return Chinese
                ? string.Format("[额外容错 +{0:0.#} 次]", v)
                : string.Format("[Extra tolerance +{0:0.#}]", v);
        }
        internal static string BonusDurable(float v)
        {
            return Chinese
                ? string.Format("[自动解锁耐久代价 -{0:0.#} 次]", v)
                : string.Format("[Auto-unlock tool cost -{0:0.#}]", v);
        }
        internal static string BonusXp(float v)
        {
            return Chinese
                ? string.Format("[评级经验 +{0:0.#%}]", v)
                : string.Format("[Rank XP +{0:0.#%}]", v);
        }
        internal static string BonusElite(float v)
        {
            return v > 0f
                ? Pick("[精英专属：开锁工具永久免磨损]", "[ELITE: lockpicks never wear out]")
                : Pick("[精英专属：Lv51 起解锁（工具永久免磨损）]", "[ELITE: unlocks at Lv51 (lockpicks never wear out)]");
        }

        /// <summary>加成行的取值分派（EBuffId → 文案）；认不出来的返回 null，调用方须原样放行。</summary>
        internal static string BonusLine(EFT.EBuffId id, float value)
        {
            if (id == LockpickEnv.BonusSlider) return BonusSlider(value);
            if (id == LockpickEnv.BonusZone) return BonusZone(value);
            if (id == LockpickEnv.BonusErrors) return BonusErrors(value);
            if (id == LockpickEnv.BonusDurable) return BonusDurable(value);
            if (id == LockpickEnv.BonusXp) return BonusXp(value);
            if (id == LockpickEnv.BonusElite) return BonusElite(value);
            return null;
        }

        // ---- 表现与评级（LockPickingPerformance） ----
        internal static string RankTag(string rank) { return Chinese ? ("[" + rank + " 级开锁]") : ("[" + rank + " Lockpick]"); }
        internal static string PerfectTag(int n) { return Chinese ? ("完美 ×" + n) : ("Perfect x" + n); }
        internal static string ComboTag(int combo) { return Chinese ? (" · 连击 ×" + combo) : (" · Combo x" + combo); }
        internal static string XpTag(string mul) { return Chinese ? (" | 经验 ×" + mul) : (" | XP x" + mul); }
        internal static string TierXpTag(string pct) { return Chinese ? (" · 技能台阶 +" + pct + "%") : (" · Skill tier +" + pct + "%"); }
        internal static string RefundTag(int times) { return Chinese ? (" · 退回耐久 " + times + " 次") : (" · Durability refunded " + times); }
        internal static string OutbreakXpTag(string mul) { return Chinese ? (" · 疫潮收益 ×" + mul) : (" · Outbreak yield x" + mul); }
        internal static string RaidTag(int picks, int perfects) { return Chinese ? ("  (本局 " + picks + " 次 / 完美 " + perfects + ")") : ("  (raid " + picks + " picks / " + perfects + " perfect)"); }
        internal static string ComboBreak(int combo) { return Chinese ? ("连击中断（×" + combo + "）") : ("Combo broken (x" + combo + ")"); }
        internal static string OutbreakHint(float diffPct, float xpPct)
        {
            return Chinese
                ? ("疫潮侵蚀：锁具锈死 —— 难度 +" + diffPct.ToString("0") + "% / 经验 +" + xpPct.ToString("0") + "%")
                : ("Outbreak corrosion: seized locks -- difficulty +" + diffPct.ToString("0") + "% / XP +" + xpPct.ToString("0") + "%");
        }

        // ---- 面板静态元素（DeltaLockUIBuilder） ----
        internal static string Working { get { return Pick("开锁中", "WORKING"); } }
        internal static string DotSuffix { get { return Pick("。", "."); } }
        internal static string HoldLmb { get { return Pick("按住鼠标左键锁定白色区间", "Hold LMB to lock the white zone"); } }
        internal static string InitialProgress { get { return Pick(">>解锁同步进行中", ">>UNLOCK IN PROGRESS"); } }
        internal static string HitStat(int hits, int required, int perfects)
        {
            return Chinese
                ? ("命中 " + hits + "/" + required + " · 完美 " + perfects)
                : ("Hits " + hits + "/" + required + " · Perf " + perfects);
        }

        // ---- 门信息行（LpData / LockpickEnv） ----
        internal static string NoKey { get { return "-"; } }
        internal static string UnknownKey { get { return Pick("未知钥匙", "Unknown key"); } }
        internal static string UnknownKeyPrefix(string head) { return Chinese ? ("钥匙·" + head) : ("Key " + head); }
    }
}
