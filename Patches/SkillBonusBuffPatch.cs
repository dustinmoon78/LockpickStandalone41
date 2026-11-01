// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;
using LockpickStandalone.Compat;
using LockpickStandalone.Data;

namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 给「开锁」技能装上 6 条等级加成 Buff，让局外技能面板（EFT.UI\SkillPanel.cs:137-143
    /// 从 _skill.Buffs 建 BuffIcon 列表）真的显示出一排加成图标（规范 §3.4 / §3.5）。
    ///
    /// 为什么必须挂在 Skill 构造器上：
    ///   SkillManager.cs:2573 用空数组 Array.Empty&lt;Buff&gt;() 构造 Lockpicking，
    ///   而 Skill.cs:70 的 Locked = buffs.Length == 0 ⇒ 原版开锁恒锁定、恒无加成行。
    ///   buffs 是构造参数，只能在构造器 Prefix 里换。
    ///
    /// 时序（硬要求）：本补丁在 Plugin.Awake 装配，而 SkillManager 的 44 次 new Skill(...)
    /// 全部发生在读档 / 进战局建档时（BepInEx 插件链早已 Awake 完）⇒ 补丁必然早于技能构造链建立。
    ///   这一点与 SkillInjectPatch 的时序论证同源（那里有完整注释）。
    ///
    /// 【绝不碰原版 43 个技能】：Prefix 第一行就是 if (id != ESkillId.Lockpicking) return;
    /// 其余技能连 buffs 参数都不碰。
    /// 【F12「面板显示等级加成」关闭时】换回原版的空数组 ⇒ 行为与今天完全一致。
    ///
    /// 【为什么不用 SPT 的 ModulePatch】2026-10-08：SkillsExtended 3.2.0 的
    /// SkillClassCtorPatch 也挂 EFT.Skill 的这个 5 参构造器、也写同一个 ref buffs，
    /// 两侧都是 Harmony 默认优先级（Priority.Normal = 400）⇒ 谁后注册谁赢，
    /// 随插件加载序摇摆，表现为加成图标行「时有时无」且失败是静默的。
    /// SPT.Reflection.Patching 根本不读 [HarmonyPriority]（已用 dnfile 查证：spt-reflection.dll
    /// 内既无 HarmonyPriority 的 TypeRef，也无任何 priority 字样），所以走 ModulePatch 无法定序。
    /// 改用 Harmony 原生 API 显式指定 Priority.Last。Harmony 2.9 的定义是 Priority.Last = 0、
    /// Priority.First = 800（不是 int.MaxValue）；排序走 PriorityComparer 的 -priority.CompareTo(other)，
    /// 是**降序** —— priority 数字大的排前面、**先**执行，数字小的才**后**执行。
    /// 所以要「最后跑」必须取**小值**，Priority.Last(0) 正是此意：0 < SE 的 400，
    /// ⇒ 我们的前缀一定最后跑，写进去的 buffs 就是最终值，与加载序无关。
    /// ★这个 0 不能动：改成 int.MaxValue 或 Priority.First(800) 都会变成最先执行，
    ///   SE(400) 后写覆盖，图标行静默失效且不报错。
    /// 代价只是这一个类不再由 PatchManager 托管。
    ///
    /// 【为什么只靠优先级就够，不需要在 Postfix 里再写一遍 buffs】2026-10-08 定案（方案 i）：
    /// Harmony 把同一方法上的**全部** Prefix 排成一条链，按 priority **降序**（数字大的先跑）串行执行，
    /// 每个 Prefix 对 ref 参数的写入都立刻刷回这条链共享的参数数组，**原构造器只在这条链
    /// 全部跑完之后才被调用一次**，拿到的是链上最后一次写入的值。
    /// 所以「谁最后写 buffs」= 「谁的 priority 最小」，本模组取 Priority.Last(0) 即恒胜。
    /// 反过来看 Postfix：构造器此时**已经执行完毕**，buffs 早已被赋进 Skill 的字段，
    /// Postfix 再改 ref 参数既不会回写那个字段、也改变不了已经建好的对象状态 ——
    /// 想要「后写覆盖」的效果就必须反射改字段，那是更脏也更危险的路。
    /// 【为什么不加 [HarmonyPriority] 特性】本类不走 Harmony 的自动扫描
    /// （HarmonyPatch.Processor），而是 GetMethod 手工取 Prefix 再 new HarmonyMethod(...)，
    /// 特性根本没机会被读；就算读了，显式 priority 也会盖过它。特性在这里是纯装饰。
    /// 而且特性只能写死一个 int：想「最后跑」必须写比 400 更小的值（如 0），
    /// 写 500 这类比 400 大的值反而会排到 SE 前面去，方向正好相反。
    /// 【不卸 SE 的补丁】SE 那个构造器补丁同时负责它自己 9 个技能的 buffs（见 SeCompat 类注释），
    /// 卸掉会连带清空那些技能；我们只在自己的前缀上定序取胜，一个字节都不改它的代码。
    ///
    /// 【禁止裸整数 EBuffId】6 条全部复用真实存在的原版枚举名（LockpickEnv.Bonus*），
    /// 这样 StaticIcons.BuffIdSprites 里必有图标，不会空白。
    /// </summary>
    internal static class SkillBonusBuffPatch
    {
        // 复用缓冲：Build/Refresh 只在技能构造与升级时调用（不是每帧），但仍不每次 new List。
        private static readonly List<LockpickEnv.LevelBonus> Rows = new List<LockpickEnv.LevelBonus>(8);
        private static bool _logged;

        /// <summary>独立 Harmony 实例：与 SPT 的 PatchManager 无关，只为拿到显式优先级的控制权。</summary>
        private static readonly Harmony Hp = new Harmony("com.dsh.lockpickstandalone.skillbonusbuff");

        /// <summary>装配。目标与参数与改造前完全一致，只是把前缀钉在 Priority.Last。</summary>
        internal static void Enable()
        {
            var ctor = typeof(Skill).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(SkillManager),
                    typeof(ESkillId),
                    typeof(ESkillClass),
                    typeof(SkillManager.SkillAction[]),
                    typeof(SkillManager.Buff[])
                },
                null);
            if (ctor == null)
                throw new MissingMethodException("EFT.Skill 的 5 参构造器未找到，技能面板加成行无法装配");
            var prefix = typeof(SkillBonusBuffPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (prefix == null)
                throw new MissingMethodException("SkillBonusBuffPatch.Prefix 未找到");
            // ★定序点：Harmony 2.9 里 Priority.Last = 0、Priority.First = 800（不是 int.MaxValue）；
            //   排序比较器 PriorityComparer 返回 -priority.CompareTo(other)，是**降序**——
            //   priority 数字大的排前面、**先**执行，数字小的才**后**执行。
            //   要「最后执行」就得取小值，Priority.Last = 0 正是此意。
            //   SE 走 SPT 的 ModulePatch，而 ModulePatch.GetPatchMethods 只 new HarmonyMethod(methodInfo)、
            //   不传 priority 也不读 [HarmonyPriority] ⇒ 对方恒为 Harmony 未指定时的默认 400（Priority.Normal）；
            //   本模组 0 < 400 ⇒ 必然排在 SE 之后，两边不存在同档并列，加载序不再影响结果。
            //   ★警告：下面这一行必须保持 Priority.Last(0)。不要改成 int.MaxValue，也不要改成 Priority.First(800)
            //     —— 那两个值都 > 400，会变成最先执行，随后被 SkillsExtended(400) 写进 buffs 的值覆盖，
            //     表现为加成图标行静默失效且没有任何报错。
            //   走 HarmonyMethod(MethodInfo, int priority) 重载而不是 [HarmonyPriority] 特性：
            //   显式值优先，且本类不经过 Harmony 的特性扫描路径（见类注释「为什么不加特性」）。
            Hp.Patch(ctor, prefix: new HarmonyMethod(prefix, Priority.Last));
        }

        public static void Prefix(SkillManager skillManager, ESkillId id, ref SkillManager.Buff[] buffs)
        {
            if (id != ESkillId.Lockpicking) return;   // ← 其余 43 个技能在这里就走了
            // ★闸门：用户关了「接管技能扩展的开锁」且 SE 确实在场 ⇒ 这一行归 SE 管，
            //   本补丁一个字节都不写，buffs 原样放行（不赋值、不置空、不加空数组），
            //   由 SE 自己的 SkillClassCtorPrefix 决定这一行的内容。
            //   闸门排在 id 判据之后：其余 43 个技能在这里就走了，闸门只在开锁这一行上求值。
            //   不装 SE 时 TakeoverEffective 恒为 true ⇒ 行为与本次改造之前完全一致。
            if (!SeCompat.TakeoverEffective) return;
            try
            {
                if (LpConfig.PanelBonuses == null || !LpConfig.PanelBonuses.Value)
                {
                    // 开关关闭：换回原版的空数组（Locked 会随之回到 true，由 SkillInjectPatch 解锁）。
                    buffs = Array.Empty<SkillManager.Buff>();
                    return;
                }
                buffs = Build(skillManager);
                if (!_logged)
                {
                    _logged = true;
                    LpLog.Info("[开锁技能面板] 已挂 6 条等级加成行（游标速度 / 锁定区宽度 / 额外容错 / "
                        + "自动解锁耐久减免 / 评级经验 / 精英免磨损），等级上限 Lv" + LockpickLevel.MaxLevel);
                }
            }
            catch (Exception ex)
            {
                // 失败必须可见（Warning/Error 永不静默）；失败则退回原版空数组，不影响开锁本体。
                LpLog.Error("[开锁技能面板] 构造加成行失败（已退回原版空加成行，开锁功能不受影响）：" + ex.Message);
                buffs = Array.Empty<SkillManager.Buff>();
            }
        }

        /// <summary>按当前等级建 6 条 FloatBuff，并挂上值刷新回调。</summary>
        private static SkillManager.Buff[] Build(SkillManager skillManager)
        {
            Rows.Clear();
            LockpickEnv.LevelBonusRows(Rows, LevelOf(skillManager));

            var built = new SkillManager.FloatBuff[Rows.Count];
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                built[i] = new SkillManager.FloatBuff
                {
                    Id = row.Id,
                    // ★必须显式设 BuffType：Simple 走普通底，Elite 才有金色边框与 "ELITE" 前缀
                    //   （BuffIcon.cs:56-58 与 :63-64 都读它）。
                    BuffType = row.Type,
                    HidenForPlayers = false,
                    Value = row.Value,
                };
            }

            // 原版 Skill.UpdateRules（Skill.cs:172-192）只把 Math.Min(SummaryLevel,50) 传给
            // BaseRuleFunc，且 EliteRuleFunc 只在 SummaryLevel&gt;50 时才跑 ⇒ 两条回调都挂，
            // 且都不依赖原版传进来的参数，一律按真实等级重算（高等级也能拿到正确数值）。
            for (int i = 0; i < built.Length; i++)
            {
                var buff = built[i];
                buff.BaseRuleFunc = _ => Refresh(skillManager, built);
                if (buff.BuffType == SkillManager.EBuffType.Elite)
                    buff.EliteRuleFunc = () => Refresh(skillManager, built);
            }

            Refresh(skillManager, built);
            SkillBuffTextPatch.Register(built);   // 登记身份，供取文补丁做「这确实是我们建的」二次确认
            return built;
        }

        /// <summary>按真实等级重算 6 条 Buff 的数值（唯一数据源仍是 LockpickEnv.LevelBonusRows）。</summary>
        private static void Refresh(SkillManager skillManager, SkillManager.FloatBuff[] built)
        {
            Rows.Clear();
            LockpickEnv.LevelBonusRows(Rows, LevelOf(skillManager));
            for (int i = 0; i < built.Length; i++)
            {
                var buff = built[i];
                float v = 0f;
                for (int j = 0; j < Rows.Count; j++)
                {
                    if (Rows[j].Id == buff.Id) { v = Rows[j].Value; break; }
                }
                buff.Value = v;
            }
        }

        private static int LevelOf(SkillManager skillManager)
        {
            if (skillManager == null || skillManager.Lockpicking == null) return 0;
            return skillManager.Lockpicking.Level;
        }
    }
}
