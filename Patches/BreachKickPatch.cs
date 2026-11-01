// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using EFT.Ballistics;
using EFT.HealthSystem;
using LockpickStandalone.Data;
using LockpickStandalone.Interaction;
using Comfort.Common;
using SPT.Reflection.Patching;
using UnityEngine;

namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 踹门：把原版「踹一下看几何」升级成「越踹越有把握，踹开了要付代价」。
    ///
    /// 【原版行为】
    /// Door.BreachSuccessRoll 只是一次几何判定——玩家必须站在门正前方，
    /// 视线向量与门板法线同向才算踹得开，站错位置怎么踹都踹不开。
    /// 它的失败代价是 FailBreach：踹坏门框。
    ///
    /// 【本补丁做的事】
    /// 1. 保留原版几何判定：站错位置照样踹不开（这是免费且合理的平衡，不动它）。
    /// 2. 在几何判定通过之后，叠加一层按等级计算的成功率：每踹失败一脚，
    ///    下一脚的把握递增（撬锁等级越高首脚越准，精英段三脚必开）。
    /// 3. 每踹一次都结算代价：非精英段按力量等级掷骨折；精英段改为掉血，
    ///    掉血量按门难度分档、被力量等级打折；力量练到位时不掉血只剩震荡与掉体力。
    ///
    /// 【为什么要自己写而不是改原版方法】
    /// 直接把 BreachSuccessRoll 改成恒真会把原版的几何判定一起废掉，玩家隔着门缝
    /// 就能踹开，那不是加强而是破坏。这里只在它返回 true 之后叠加概率与代价。
    /// </summary>
    internal sealed class BreachKickPatch : ModulePatch
    {
        /// <summary>取最硬档门难度计算代价：门的 KeyId 只表示「有没有钥匙」，拿不到真实难度，
        /// 统一按最高档算扣血，宁可略重也不要让玩家靠挑低级门刷无伤。</summary>
        private const int HardestDoorLevel = 5;

        /// <summary>每扇门当前已踹的脚数。key 用门的运行时标识，避免影响其它门。</summary>
        private static readonly Dictionary<object, int> Attempts = new Dictionary<object, int>();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Door), "BreachSuccessRoll");
        }

        [HarmonyPrefix]
        private static bool Prefix(ref bool __result, Door __instance, Vector3 yourPosition)
        {
            // 总开关未开：完全交还原版，一行都不改。
            if (!LpConfig.Enabled.Value || !LpConfig.BreachEnabled.Value)
                return true;

            // 第一步：站位几何判定，完全沿用原版逻辑（静态方法内直接调原方法）。
            bool geometric = __instance.BreachSuccessRoll(yourPosition);
            if (!geometric)
            {
                // 站位不对 —— 原版会直接失败。这里也不消耗「已踹脚数」，
                // 玩家走正了还能按原概率重试，不因为他之前站歪了就被惩罚。
                LpLog.Info(LpStrings.NotifyBreachWrongAngle());
                __result = false;
                return false;
            }

            // 第二步：概率判定。
            int lockLevel = LockpickEnv.CurrentLevel();
            int done = Attempts.TryGetValue(__instance, out int n) ? n : 0;
            float chance = BreachRules.KickChance(lockLevel, done);
            bool success = UnityEngine.Random.value < chance;

            // 无论成败都要累计脚数：失败才是「下一次更有把握」的来源。
            Attempts[__instance] = done + 1;

            // 第三步：每脚都要付代价（成功与否都付，踹歪了腿一样疼）。
            ApplyStrain();

            if (!success)
            {
                // 每次失败都给一条玩家可见的提示：本脚成功率 + 下一脚会涨到多少。
                // 不提示的话玩家只会觉得「这门踹不开」，看不到递增曲线就放弃了。
                float next = BreachRules.KickChance(lockLevel, done + 1);
                int strengthLevel = LockpickEnv.StrengthLevel();
                LpLog.Info(LpStrings.NotifyBreachFail(lockLevel, strengthLevel, done + 1, chance, next));
                LpLog.Debug("[Lockpick] 踹门失败："
                    + BreachRules.Describe(lockLevel, LockpickEnv.StrengthLevel(), 3));
                __result = false;
                return false;
            }

            LpLog.Info(LpStrings.NotifyBreachOpen(done + 1));
            LpLog.Debug("[Lockpick] 踹门成功（第 " + (done + 1) + " 脚）："
                + BreachRules.Describe(lockLevel, LockpickEnv.StrengthLevel(), 3));
            __result = true;
            return false;
        }

        /// <summary>结算这一脚的代价：骨折 / 掉血 / 震荡 / 掉体力。</summary>
        /// <remarks>
        /// 门本身没有生命值（EFT 的门不参与伤害结算），所以所有代价都落在踹门的玩家身上：
        /// 掉血与震荡走 <c>player.HealthController</c>，体力走 <c>Stamina</c> 单例。
        /// 门难度这里取不到精确值（KeyId 只用于判断是否有钥匙），故统一按最高档算扣血，
        /// 宁可略重也不要让玩家靠挑低级门刷无伤。
        /// </remarks>
        private static void ApplyStrain()
        {
            try
            {
                var world = Singleton<GameWorld>.Instance;
                var player = world == null ? null : world.MainPlayer;
                if (player == null || player.HealthController == null) return;

                var health = player.HealthController as ActiveHealthController;
                if (health == null || !health.IsAlive) return;

                int lockLevel = LockpickEnv.CurrentLevel();
                int strength = LockpickEnv.StrengthLevel();
                bool elite = BreachRules.IsElite(lockLevel);
                bool tookDamage = false;

                // 代价一：精英段掉血。伤害对象直接复用 EFT 自带的轻出血定义，
                // 免去自己构造 DamageInfo（其内部字段不少，构造不当会静默失效）。
                if (elite)
                {
                    float loss = BreachRules.EliteBloodLoss(strength, HardestDoorLevel);
                    if (loss > 0.01f)
                    {
                        health.ApplyDamage(EBodyPart.LeftLeg, loss, DamageHelper.LightBleedingDamage);
                        LpLog.Debug("[Lockpick] 踹门掉血 " + loss.ToString("0.#") + "（力量 Lv" + strength + "）");
                        tookDamage = true;
                    }
                }
                else if (UnityEngine.Random.value < BreachRules.FractureChance(lockLevel, strength))
                {
                    // 代价二：非精英段掷真骨折。
                    // 走官方 ActiveHealthController.DoFracture —— 与坠落伤害断肢是同一条路径，
                    // 因此会带上原版的痛感、治疗消耗与愈合时间，行为与被摔断腿完全一致。
                    // 该方法自身会校验「四肢且未残废」，无需我们重复判断。
                    health.DoFracture(EBodyPart.LeftLeg);
                    LpLog.Info(LpStrings.NotifyBreachFractured + "（力量 Lv" + strength + "）");
                    tookDamage = true;
                }

                // 代价三：震荡 —— 精英且无伤时也保留，这是「无伤」而非「无代价」的底线。
                float secs = BreachRules.ContusionSeconds(lockLevel, strength);
                float str = BreachRules.ContusionStrength(lockLevel, strength);
                health.DoContusion(secs, str);

                // 代价四：体力消耗。
                // ★ EFT 的 Stamina 单例在 4.1 反编译里没有稳定的公开入口，
                //   与其臆造一个调不通的 API，不如把「体力消耗」折算进同一笔扣血里表达：
                //   踹完腿更虚 = 力量不够扛得住。语义等价，且不引入调不通的调用。
                // EFT 的 Stamina 单例在 4.1 反编译里没有稳定公开入口，
                // 这里改用同样属于「体力」语义的原版伤害类型 Exhaustion（力竭），
                // 玩家会感到脱力——与「踹门消耗体力」语义一致，且调用确定有效。
                float stam = BreachRules.StaminaCost(lockLevel);
                health.ApplyDamage(EBodyPart.Common, stam * 0.5f, DamageHelper.ExhaustionDamage);

                LpLog.Debug("[Lockpick] 踹门代价：震荡 " + secs.ToString("0.0")
                    + "s / 体力 " + stam.ToString("0") + (tookDamage ? " / 已受伤" : " / 无伤"));
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 踹门代价结算跳过：" + ex.Message);
            }
        }

        /// <summary>门被打开后清掉它的累计脚数，防止下次再开时接着旧账算。</summary>
        internal static void Forget(Door door)
        {
            if (door != null) Attempts.Remove(door);
        }
    }
}
