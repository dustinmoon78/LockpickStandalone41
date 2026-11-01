// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace LockpickStandalone.Patches;

/// <summary>
/// 让「开锁」技能免受 EFT 技能疲劳机制影响。
///
/// 【为什么需要这个补丁】
/// 原版对每个技能维护一套疲劳衰减：随着本场次该技能累计获得的点数增长，
/// 有效系数会按配置里的每点衰减系数连乘下去，并设一个极低的系数下限，
/// 同时启动一个较长的冷却计时器，只有在冷却走完且期间未再触发时才复位。
/// 对绝大多数战斗技能这是合理的自限流设计；但开锁技能的收益来源是
/// 「一张地图上数量有限的门」，在局内时间有限，一次能撬的门就那么几扇。
/// 套用通用疲劳曲线会导致玩家在少数几次开锁之后就几乎拿不到经验，
/// 与本模组希望提供的「撬一把算一把」体验直接冲突。
///
/// 【这个补丁做什么】
/// 仅拦截开锁技能这一个技能 ID：
///   · 经验按原始输入全额计入，不做任何衰减；
///   · 有效系数固定为 1，避免被服务端或其它路径回写成一个偏低的值；
///   · 疲劳计时器推到极大值，让其它路径（例如存档回写）没有机会
///     把已累计的点数重新钳回新鲜阈值。
/// 其余所有技能完全不受影响，走原版逻辑。
///
/// 【开关】
/// F12 配置项「忽略开锁技能疲劳」关闭时，本补丁直接放行、行为与原版一致。
/// </summary>
internal class LockPickingFatiguePatch : ModulePatch
{
    /// <summary>已豁免的累计次数，仅用于稀疏打点。</summary>
    private static int _exemptedCount;

    /// <summary>
    /// 定位原版结算经验的入口。
    /// 按「类型 + 方法名 + 参数表」三重锁定，避免将来新增同名重载导致歧义异常；
    /// 方法名用 nameof 写死，上游一旦改名，本项目会直接在编译期失败而不是运行期静默失效。
    /// </summary>
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Skill), nameof(Skill.UseEffectiveness), new[] { typeof(float) });
    }

    /// <summary>
    /// 命中开锁技能且开关打开时，代替原版完成经验结算。
    /// </summary>
    /// <param name="__instance">被结算的技能实例。</param>
    /// <param name="input">本次应当获得的原始经验点数。</param>
    /// <param name="__result">原版方法的返回值，这里写入「未衰减」的原始点数。</param>
    /// <returns>true 放行走原版；false 表示已接管、跳过原方法体。</returns>
    [PatchPrefix]
    public static bool Prefix(Skill __instance, float input, ref float __result)
    {
        // 作用域限定：只有开锁技能享受豁免
        if (__instance == null || __instance.Id != ESkillId.Lockpicking)
            return true;

        // 开关关闭时行为必须与原版逐位一致
        if (!LpConfig.IgnoreFatigue.Value)
            return true;

        // 点数照单全收 —— 技能面板的累计值与结算面板的显示都读这个字段，不能丢
        __instance._pointsEarned += input;

        // 把有效系数钉死为 1，挡住任何「写回一个更低系数」的路径
        __instance._effectiveness = 1f;

        // 疲劳计时器推到极大：让本局内不再有复位时机，也避免存档回写把点数钳回新鲜阈值
        __instance._fatigueTimer = float.MaxValue;

        // 原始实现最终返回的是「衰减后实际入账量」，此处等价返回未衰减的入账量
        __result = input;

        // 稀疏打点：只在前四次留下证据行，其余静默，避免刷爆日志
        int seen = ++_exemptedCount;
        if (seen == 1 || seen == 10 || seen == 100 || seen == 1000)
        {
            LpLog.LogWarning($"[开锁疲劳豁免] 生效中：第 {seen} 把，本次入账 {input:0.###} 点（疲劳衰减已对本技能关闭）");
        }

        return false;
    }
}
