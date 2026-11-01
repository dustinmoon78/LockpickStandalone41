// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using UnityEngine;

namespace LockpickStandalone.Compat
{
    /// <summary>
    /// 与 SkillsExtended（技能扩展）共存：接管它的开锁，避免两边同时往门交互列表里注项。
    /// 本类只管「门交互注入」这一条线，三层手段（互为兜底，与两个补丁的执行先后无关）：
    ///   1) 反射把它的开锁补丁 Disable() 掉 —— 它根本不再注入；
    ///   2) 仅当第 1 层失败时，才反射把 LockPicking.Enabled 置 false 兜底。
    ///      ★不能每次都关：关了会让 SE 把开锁技能标记为未解锁，
    ///      技能面板会把它显示成变暗的未解锁态（经验仍照常入账，但显示会误导）；
    ///   3) 兜底清扫：按它的英文文案删掉它已经加进去的交互项。
    /// 它不在场或反射失败时安静降级（只影响是否出现重复项，不影响本模组功能）。
    ///
    /// 【绝不把 SkillClassCtorPatch 加进上面的清单】2026-10-08 取证：该补丁不只管开锁。
    /// 已部署的 SkillsExtended 3.2.0（G:\EFT4.1CS\BepInEx\plugins\SkillsExtended\SkillsExtended.dll）
    /// 里 SkillClassCtorPatch::Prefix 的 IL 依次调用 UsecArBuffs / SilentOpsBuffs / ShadowConnectionsBuffs /
    /// BearAkBuffs / LockPickingBuffs / UsecNegotiationsBuffs / BearRawPowerBuffs / ProneMovementBuffs /
    /// FirstAidBuffs（ldc.i4 分支常量 42 / 33 / 42 / 43 / 50 / 54 / 52 / 57），并写 EFT.SkillManager.Buff、
    /// AdjustStimulatorBuff 与 set_BonusController
    /// ⇒ 卸掉它会连带清空 SE 自己的 9 个技能（急救 / 战地医疗 / 匍匐移动 / 静默行动 / 暗影连线 /
    /// 北约步枪 / 东欧步枪 / 东欧蛮力 / 北约谈判）的 buffs 与 actions，是严重回归。
    /// 开锁加成行的争用改由 SkillBonusBuffPatch 自己在 Harmony 优先级上取胜（见该文件）。
    /// 类型在「程序集名含 SkillsExtended」的程序集里按简单名查找（该插件类在全局命名空间，
    /// 写死带命名空间的全名会在换版本时失效）；探测失败会持续重试，直到它出现为止。
    /// </summary>
    internal static class SeCompat
    {
        private static readonly string[] SePatchSimpleNames =
        {
            "DoorActionPatch",
            "KeycardDoorTerminalPatch",
            "KeyCardDoorActionPatch",
        };

        internal static readonly string[] ForeignActionNames =
        {
            "Pick lock",
            "Inspect Lock",
            "Hack terminal",
            "Door cannot be opened",
        };

        private const string PluginSimpleName = "SkillsExtendedPlugin";
        private const string HelperSimpleName = "LockPickingHelpers";

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static bool _present;
        private static bool _done;
        private static float _nextProbe;
        private static float _nextAttempt;
        private static int _attempts;
        private static bool _gaveUpLogged;

        private static PropertyInfo _skillDataProp;
        private static PropertyInfo _lockPickingProp;
        private static PropertyInfo _enabledProp;
        private static MethodInfo _seGetLevel;

        internal static bool TakeoverActive { get { return LpConfig.TakeOverSe.Value; } }

        /// <summary>
        /// 「本模组此刻是否该接管开锁」：SkillsExtended 不在场，或用户没关「接管技能扩展的开锁」。
        /// SkillBonusBuffPatch 的构造器 Prefix 用它当闸门 —— 用户明确选择不接管时，
        /// 本补丁必须原样放行 buffs，否则就会和 SE 的 SkillClassCtorPatch 抢同一个 ref 参数。
        /// SE 不在场时恒为真 ⇒ 不装 SE 的用户拿到的行为与改造前完全一致。
        /// </summary>
        internal static bool TakeoverEffective
        {
            get { Probe(); return !_present || TakeoverActive; }
        }
        internal static bool Finished { get { return _done; } }

        /// <summary>在 SE 的程序集里按简单名找类型（只缓存成功结果，便于它晚加载时重试）。</summary>
        private static Type FindSeType(string simpleName)
        {
            Type cached;
            if (TypeCache.TryGetValue(simpleName, out cached) && cached != null) return cached;
            Type found = null;
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length && found == null; i++)
            {
                Assembly asm = asms[i];
                string an;
                try { an = asm.GetName().Name; } catch { continue; }
                if (string.IsNullOrEmpty(an) || an.IndexOf("SkillsExtended", StringComparison.OrdinalIgnoreCase) < 0) continue;
                try { found = asm.GetType(simpleName, false); } catch { }
                if (found != null) break;
                try
                {
                    var types = asm.GetTypes();
                    for (int j = 0; j < types.Length; j++)
                        if (types[j].Name == simpleName) { found = types[j]; break; }
                }
                catch { }
            }
            if (found != null) TypeCache[simpleName] = found;
            return found;
        }

        private static void Probe()
        {
            if (_present) return;
            if (_nextProbe > 0f && Time.realtimeSinceStartup < _nextProbe) return;
            _nextProbe = Time.realtimeSinceStartup + 3f;
            try
            {
                var plugin = FindSeType(PluginSimpleName);
                var helper = FindSeType(HelperSimpleName);
                if (plugin == null && helper == null) return;
                _present = true;
                if (plugin != null)
                    _skillDataProp = plugin.GetProperty("SkillData", BindingFlags.Public | BindingFlags.Static);
                if (helper != null)
                    _seGetLevel = helper.GetMethod("GetLevelForDoor", BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(string), typeof(string) }, null);
                LpLog.Info("[Lockpick] 检测到 SkillsExtended（接管模式 " + (TakeoverActive ? "开" : "关")
                    + "，插件类 " + (plugin != null ? "在位" : "未找到") + "，助手类 " + (helper != null ? "在位" : "未找到") + "）");
            }
            catch (Exception ex)
            {
                LpLog.Warn("[Lockpick] 探测 SkillsExtended 失败（不影响本模组）：" + ex.Message);
            }
        }

        /// <summary>按需执行接管（2 秒节流；成功一次即停）。启动期与每次门交互都会调用。</summary>
        internal static void EnsureTakeover()
        {
            if (_done) return;
            Probe();
            if (!_present) return;
            if (!TakeoverActive) return;
            if (_attempts > 0 && Time.realtimeSinceStartup < _nextAttempt) return;
            _nextAttempt = Time.realtimeSinceStartup + 2f;
            _attempts++;

            bool patchOk = DisableSePatches();
            // 仅当卸补丁失败时才动它的开关：关 Enabled 会连带把开锁技能标成未解锁。
            bool flagOk = patchOk ? false : SuppressSeLockpicking();

            if (patchOk || flagOk)
            {
                _done = true;
                LpLog.Info("[Lockpick] 已接管 SkillsExtended 的门交互注入（卸补丁=" + patchOk
                    + (patchOk ? "；未动它的开关，开锁技能在技能面板上保持正常显示" : " / 关开关=" + flagOk)
                    + "）；本模组另在技能构造器上独占「开锁」这一行的加成（Harmony 最低优先级，后写即最终值），"
                    + "它的其它技能与它的构造器补丁一律不碰");
            }
            else if (_attempts >= 30 && !_gaveUpLogged)
            {
                _gaveUpLogged = true;
                LpLog.Warn("[Lockpick] 未能接管 SkillsExtended（多次尝试失败），继续用兜底去重处理重复交互项");
            }
        }

        private static bool DisableSePatches()
        {
            bool any = false;
            for (int i = 0; i < SePatchSimpleNames.Length; i++)
            {
                var t = FindSeType(SePatchSimpleNames[i]);
                if (t == null) continue;
                try
                {
                    var inst = Activator.CreateInstance(t, true);
                    var m = t.GetMethod("Disable", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (m == null && t.BaseType != null)
                        m = t.BaseType.GetMethod("Disable", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (m == null) continue;
                    m.Invoke(inst, null);
                    any = true;
                }
                catch (Exception ex)
                {
                    LpLog.Debug("[Lockpick] 卸载 " + SePatchSimpleNames[i] + " 失败：" + ex.Message);
                }
            }
            return any;
        }

        private static bool SuppressSeLockpicking()
        {
            try
            {
                if (_skillDataProp == null) return false;
                var cfg = _skillDataProp.GetValue(null, null);
                if (cfg == null) return false;
                if (_lockPickingProp == null)
                    _lockPickingProp = cfg.GetType().GetProperty("LockPicking", BindingFlags.Public | BindingFlags.Instance);
                if (_lockPickingProp == null) return false;
                var lp = _lockPickingProp.GetValue(cfg, null);
                if (lp == null) return false;
                if (_enabledProp == null)
                    _enabledProp = lp.GetType().GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
                if (_enabledProp == null || !_enabledProp.CanWrite) return false;
                _enabledProp.SetValue(lp, false, null);
                return true;
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 关闭 SkillsExtended 开锁开关失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>兜底：把对方已注入的交互项从列表里摘掉。</summary>
        internal static void StripForeign(AvailableInteractionState state)
        {
            if (state == null || state.Actions == null) return;
            for (int i = state.Actions.Count - 1; i >= 0; i--)
            {
                var a = state.Actions[i];
                if (a == null) continue;
                for (int j = 0; j < ForeignActionNames.Length; j++)
                {
                    if (string.Equals(a.Name, ForeignActionNames[j], StringComparison.Ordinal))
                    {
                        state.Actions.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>沿用对方的门等级表（无则返回 -1，走本模组的 doors.json）。</summary>
        internal static int ForeignLevel(string map, string doorId)
        {
            if (!LpConfig.SeLevelContinuity.Value) return -1;
            Probe();
            if (_seGetLevel == null || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(doorId)) return -1;
            try
            {
                object ret = _seGetLevel.Invoke(null, new object[] { map, doorId });
                if (ret is int) return (int)ret;
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 读取 SkillsExtended 门等级失败：" + ex.Message);
            }
            return -1;
        }
    }
}
