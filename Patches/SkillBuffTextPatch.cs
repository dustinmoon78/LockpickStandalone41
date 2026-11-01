// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.UI;
using HarmonyLib;
using LockpickStandalone.Data;
using LockpickStandalone.Interaction;
using SPT.Reflection.Patching;
using UnityEngine.EventSystems;

namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 技能面板加成行的中文文案（规范 §3.5）。
    ///
    /// ★与规范的一处必要偏离（已在实现报告里标注）：规范写的是挂
    ///   BuffIcon.GetBuffDescription 的 Postfix，按「该 BuffIcon 的 Skill.Id == Lockpicking」取文。
    /// 实际不可行 —— BuffIcon.cs:34 的真实签名是
    ///   public static string GetBuffDescription(SkillManager.Buff buff)
    /// 它是 **static**，只有一个 buff 参数：Harmony 拿不到 __instance、更拿不到 Skill。
    /// 退而求其次挂「静态作用域变量」又会被面板构建顺序污染
    /// （SkillPanel.cs:137-143 一次建一整排图标，最后一次 Show 的技能未必是鼠标悬停的那个），
    /// 会误伤别的技能 —— 这正是规范 §3.5 要防的事。
    ///
    /// 实际做法：改挂两个**实例**方法的 Postfix —— 全工程里调 GetBuffDescription 的只有两处，
    /// 且两处手里都握着 Skill：
    ///   • BuffIcon.OnPointerEnter(PointerEventData)（BuffIcon.cs:61-65）—— 技能面板图标悬停提示
    ///   • BuffPanel.UpdateBuff()（BuffPanel.cs:22-27）—— 技能说明面板里的加成列表（SkillTooltip 用它）
    /// 作用域判据与规范一致且更强：BuffThumb.Skill.Id == ESkillId.Lockpicking
    /// **并且**该 Buff 实例确实登记在 Ours（由本模组 SkillBonusBuffPatch 自建）。
    /// Lockpicking 的 Buffs 里只可能有本模组建的 6 条（原本是空数组），
    /// 其余 43 个技能的 Buffs 本模组一个字节都没碰 ⇒ 原版文案零风险。
    ///
    /// ★绝对禁止改 locale 键：复用的 EBuffId 同时属于原版别的技能（如 SearchSpeed 属于 Search），
    ///   改键会连带改掉 Strength 等所有技能的文案。这里只做运行期字符串替换。
    /// </summary>
    internal static class SkillBuffTextPatch
    {
        // 性能红线：反射字段引用一律静态缓存，绝不在调用点现取。
        private static FieldInfo _skillField;
        private static FieldInfo _buffField;
        private static FieldInfo _tooltipField;
        private static FieldInfo _descriptionField;
        private static bool _fieldsReady;

        /// <summary>本模组自建 Buff 的身份登记（Buff 未重写 Equals ⇒ 走引用相等）。</summary>
        private static readonly HashSet<SkillManager.Buff> Ours = new HashSet<SkillManager.Buff>();

        internal static void Register(SkillManager.FloatBuff[] built)
        {
            if (built == null) return;
            for (int i = 0; i < built.Length; i++) Ours.Add(built[i]);
        }

        private static void EnsureFields()
        {
            if (_fieldsReady) return;
            _fieldsReady = true;
            _skillField = AccessTools.Field(typeof(BuffThumb), "Skill");
            _buffField = AccessTools.Field(typeof(BuffThumb), "Buff");
            _tooltipField = AccessTools.Field(typeof(BuffIcon), "_tooltip");
            _descriptionField = AccessTools.Field(typeof(BuffPanel), "_description");
            if (_skillField == null || _buffField == null || _tooltipField == null || _descriptionField == null)
                LpLog.Error("[开锁技能面板] 原版 Buff UI 私有字段未找到，加成文案无法替换（其余 43 个技能不受影响）");
        }

        internal static Skill SkillOf(object icon)
        {
            EnsureFields();
            if (icon == null || _skillField == null) return null;
            return _skillField.GetValue(icon) as Skill;
        }

        internal static SimpleTooltip TooltipOf(BuffIcon icon)
        {
            EnsureFields();
            if (icon == null || _tooltipField == null) return null;
            return _tooltipField.GetValue(icon) as SimpleTooltip;
        }

        internal static CustomTextMeshProUGUI DescriptionOf(BuffPanel panel)
        {
            EnsureFields();
            if (panel == null || _descriptionField == null) return null;
            return _descriptionField.GetValue(panel) as CustomTextMeshProUGUI;
        }

        /// <summary>取出本模组自建的 FloatBuff；不是我们的 / 不是开锁技能的，一律返回 null 放行原版。</summary>
        internal static SkillManager.FloatBuff OursOf(object icon)
        {
            var skill = SkillOf(icon);
            if (skill == null || skill.Id != ESkillId.Lockpicking) return null;   // ← 作用域判据（与规范 §3.5 一致）
            EnsureFields();
            if (_buffField == null) return null;
            var buff = _buffField.GetValue(icon) as SkillManager.FloatBuff;
            if (buff == null || !Ours.Contains(buff)) return null;              // ← 身份二次确认
            return buff;
        }

        /// <summary>原版把 "[...]" 染成技能蓝（BuffIcon.cs:36-37）；我们沿用同一套标记。</summary>
        internal static string Colorize(string text)
        {
            return text.Replace("[", "<color=#54c1ff>").Replace("]", "</color>");
        }
    }

    /// <summary>图标悬停提示：开锁技能的那 6 条用本模组文案，其余原样（Postfix 只在命中时覆写）。</summary>
    internal sealed class BuffIconTextPatch : ModulePatch
    {
        // 一次性告警守卫：tooltip.Show 反复抛时只记第一行，避免玩家每悬停一次刷一行。
        // 首次之后仍会把异常吞掉并放行原版文案（失败可见性由第一次的 Warning 保证）。
        private static bool _warned;

        protected override MethodBase GetTargetMethod()
        {
            return typeof(BuffIcon).GetMethod("OnPointerEnter", new[] { typeof(PointerEventData) });
        }

        [PatchPostfix]
        public static void Postfix(BuffIcon __instance)
        {
            var buff = SkillBuffTextPatch.OursOf(__instance);
            if (buff == null) return;
            string body = LpText.BonusLine(buff.Id, buff.Value);
            if (string.IsNullOrEmpty(body)) return;
            try
            {
                var tooltip = SkillBuffTextPatch.TooltipOf(__instance);
                if (tooltip == null) return;
                var skill = SkillBuffTextPatch.SkillOf(__instance);
                // ★判据不用 skill.IsEliteLevel：第三方 SkillCap100 会把这个属性的 getter
                //   整段改成「Level >= 它的 MaxSkillLevel(默认 100)」，与它的 EliteMode 无关；
                //   本模组的开锁封顶 70 永远够不到 100 ⇒ Lv51~70 的 ELITE 前缀会静默消失。
                //   改回本模组自己的判据（Lv51 起算），与 ToolDurability / XpAward 同源；
                //   不装第三方时行为与改造前逐字节一致。
                bool elite = buff.BuffType != SkillManager.EBuffType.Simple
                    && skill != null && LockpickLevel.IsElite(skill.Level);
                tooltip.Show((elite ? "<color=#c78b2c>ELITE</color> " : "") + SkillBuffTextPatch.Colorize(body));
            }
            catch (Exception ex)
            {
                // 一次性告警守卫：tooltip.Show 反复抛时只记第一行，避免玩家每悬停一次刷一行。
                // 首次之后仍吞掉异常并放行原版文案（失败可见性由第一次的 Warning 保证）。
                if (!_warned)
                {
                    _warned = true;
                    LpLog.Warn("[开锁技能面板] 替换悬停文案失败（退回原版文案）：" + ex.Message);
                }
            }
        }
    }

    /// <summary>技能说明面板（SkillTooltip 用 BuffPanel）里的加成列表文案。</summary>
    internal sealed class BuffPanelTextPatch : ModulePatch
    {
        // 一次性告警守卫：与 BuffIconTextPatch 对称。这两个 Postfix 都会随面板反复重建而高频调用，
        // 字段（_description 为 null 等）取不到时抛的异常若每次都打一行，会在打开技能面板时刷屏。
        // 首次之后仍吞掉异常并放行原版文案（失败可见性由第一次的 Warning 保证）。
        private static bool _warned;

        protected override MethodBase GetTargetMethod()
        {
            return typeof(BuffPanel).GetMethod("UpdateBuff", Type.EmptyTypes);
        }

        [PatchPostfix]
        public static void Postfix(BuffPanel __instance)
        {
            var buff = SkillBuffTextPatch.OursOf(__instance);
            if (buff == null) return;
            string body = LpText.BonusLine(buff.Id, buff.Value);
            if (string.IsNullOrEmpty(body)) return;
            try
            {
                var text = SkillBuffTextPatch.DescriptionOf(__instance);
                if (text == null) return;
                var skill = SkillBuffTextPatch.SkillOf(__instance);
                // 同上：不用 skill.IsEliteLevel（会被第三方改写成 Level>=其上限），用本模组自己的判据。
                bool elite = buff.BuffType == SkillManager.EBuffType.Elite
                    || (skill != null && LockpickLevel.IsElite(skill.Level) && buff.BuffType == SkillManager.EBuffType.Switching);
                text.text = (elite ? "<color=#c78b2c>ELITE</color> " : "") + SkillBuffTextPatch.Colorize(body);
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LpLog.Warn("[开锁技能面板] 替换加成列表文案失败（退回原版文案）：" + ex.Message);
                }
            }
        }
    }
}
