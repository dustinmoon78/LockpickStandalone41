// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace LockpickStandalone.Patches
{
    /// <summary>
    /// 自注入「开锁」技能槽位（F12 可关）。
    ///
    /// 原版 4.1 的 SkillManager 无参构造函数虽然 new 了 Lockpicking 字段，却只把 43 项技能放进
    /// DisplayList / Skills，开锁不在任何数组里 —— 而技能面板只读 DisplayList（EFT.UI\SkillContainer.cs:46,82,88），
    /// 存档只遍历 Skills（SkillsDescriptor.cs:81-92），读档也只在 Skills 里找（SkillManager.cs:2267-2273）。
    /// 因此开锁技能「面板上看不见、进度出图即丢」。这里在构造函数返回后补挂两处数组并把技能解锁。
    ///
    /// 1) 补丁装配（Plugin.Awake）解决「补丁早于建档」的时序；
    /// 2) EnsureInjected 兜底解决「补丁装配晚于 SkillManager 构造」的反向时序。
    ///
    /// 与技能扩展共存的判据：从「数组里是否已有 ESkillId.Lockpicking」换成
    /// 「SkillsExtended 程序集是否已加载」（SePresent）。旧判据与补丁执行顺序相关 —— 技能扩展的
    /// SkillManagerConstructorPatch 与本补丁同一 Harmony 锚点（SkillManager 无参构造）且**不幂等**：
    /// 它无条件 new Skill[skills.Length + 6] 整表重建并把 skillManager.Lockpicking 再写一次。
    /// 于是若我们的 Postfix 先执行，我们追加的那份会被它连同自己那份一起留着 ⇒ Skills/DisplayList
    /// 里各出现两条开锁槽位（面板两行「开锁」，存档 Common 里两条同 Id 条目）。
    /// 新判据与顺序无关：SkillManager 的构造只发生在读档/进战局时，而 BepInEx 插件链加载在所有
    /// BaseUnityPlugin.Awake 阶段就已完成 ⇒ 本 Postfix 执行时技能扩展程序集必然已在 AppDomain 里。
    /// </summary>
    internal sealed class SkillInjectPatch : ModulePatch
    {
        private static bool _patchedLogged;
        private static bool _existingLogged;
        private static bool _fallbackLogged;
        private static bool _seLogged;

        protected override MethodBase GetTargetMethod()
        {
            return typeof(SkillManager).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Array.Empty<Type>(), null);
        }

        [PatchPostfix]
        public static void Postfix(SkillManager __instance)
        {
            if (!InjectionWanted) return;
            try
            {
                if (__instance == null) return;
                if (SePresent())
                {
                    // 技能扩展在位：开锁槽位由它整表重建时提供，我们任何写入都会被它覆盖或叠加成重复条目。
                    if (!_seLogged)
                    {
                        _seLogged = true;
                        LpLog.LogWarning("[开锁技能槽位] 检测到 SkillsExtended，技能槽位由其提供，本模组不重复注入");
                    }
                    return;
                }
                if (AlreadyHasLockpicking(__instance.Skills))
                {
                    // 已有其它来源（技能扩展以外）提供开锁槽位：不动它，也不重复插。
                    if (!_existingLogged)
                    {
                        _existingLogged = true;
                        LpLog.LogWarning("[开锁技能槽位] 已存在（由其它来源提供），本模组不重复注入");
                    }
                    return;
                }
                if (__instance.Lockpicking == null) return;
                if (Inject(__instance, __instance.Lockpicking) && !_patchedLogged)
                {
                    _patchedLogged = true;
                    LpLog.LogWarning("[开锁技能槽位] 已在技能管理构造后注入（技能面板可见 / 战局经验与存档正常）");
                }
            }
            catch (Exception ex)
            {
                LpLog.LogWarning("[开锁技能槽位] 构造后注入失败（不影响其余技能）：" + ex.Message);
            }
        }

        /// <summary>
        /// 兜底：战局内首次打开开锁面板前确认一次 —— 补丁装配可能晚于 SkillManager 的构造（后者发生在进游戏建档时）。
        /// 幂等且只做一次数组扫描，无每帧开销；不在战局内（局外菜单）直接跳过。
        /// </summary>
        internal static void EnsureInjected()
        {
            if (!InjectionWanted) return;
            if (SePresent()) return;
            if (_fallbackLogged) return;
            try
            {
                if (!Singleton<GameWorld>.Instantiated) return;
                var world = Singleton<GameWorld>.Instance;
                var player = world == null ? null : world.MainPlayer;
                var manager = player == null ? null : player.Skills;
                if (manager == null) return;
                if (AlreadyHasLockpicking(manager.Skills)) { _fallbackLogged = true; return; }
                if (manager.Lockpicking == null) return;
                if (!Inject(manager, manager.Lockpicking)) return;
                _fallbackLogged = true;
                LpLog.LogWarning("[开锁技能槽位] 兜底注入完成（补丁装配晚于技能管理构造，已在本场战局补齐）");
            }
            catch (Exception ex)
            {
                LpLog.LogWarning("[开锁技能槽位] 兜底注入失败（不影响其余技能）：" + ex.Message);
            }
        }

        private static bool InjectionWanted
        {
            get { return LpConfig.InjectSkillSlot != null && LpConfig.InjectSkillSlot.Value; }
        }

        /// <summary>
        /// 技能扩展 SkillsExtended 是否已在位（顺序无关判据）。
        /// 只看程序集是否已加载，不看补丁谁先 Enable、也不看 Harmony 排序方向：
        /// SkillManager 的构造发生在读档 / 进战局（new Profile → new SkillManager(descriptor)），
        /// 而所有 BepInEx 插件此时早已 Awake 完、程序集早已进入 AppDomain。
        /// </summary>
        private static bool SePresent()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    var asm = assemblies[i];
                    if (asm == null) continue;
                    var name = asm.GetName().Name;
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("SkillsExtended", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch (Exception ex)
            {
                LpLog.LogWarning("[开锁技能槽位] 技能扩展探测失败（按不在位处理）：" + ex.Message);
            }
            return false;
        }

        /// <summary>Skills 数组里已有开锁即视为「已注入」（第二道判据：技能扩展不在位、但别的来源已提供时仍然生效）。</summary>
        private static bool AlreadyHasLockpicking(Skill[] skills)
        {
            if (skills == null) return false;
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null && skills[i].Id == ESkillId.Lockpicking) return true;
            }
            return false;
        }

        /// <summary>把技能追加进 Skills 与 DisplayList（各自已含则不动），并解除 Locked。返回 Skills 是否真的被改动。</summary>
        private static bool Inject(SkillManager manager, Skill skill)
        {
            bool changed = Append(manager, "Skills", manager.Skills, skill);
            Append(manager, "DisplayList", manager.DisplayList, skill);
            Unlock(skill);
            return changed;
        }

        private static bool Append(SkillManager manager, string fieldName, Skill[] array, Skill skill)
        {
            if (array == null) return false;
            if (AlreadyHasLockpicking(array)) return false;
            var grown = new Skill[array.Length + 1];
            Array.Copy(array, grown, array.Length);
            grown[array.Length] = skill;
            var field = AccessTools.Field(typeof(SkillManager), fieldName);
            if (field == null) throw new MissingFieldException("SkillManager", fieldName);
            // Skills/DisplayList 都是 readonly 字段：Mono 运行期反射写 initonly 字段有效（技能扩展同款做法）。
            field.SetValue(manager, grown);
            return true;
        }

        /// <summary>Skill.Locked 是 readonly（构造时按 buffs.Length == 0 决定，Skill.cs:70），这里置回解锁态。</summary>
        private static void Unlock(Skill skill)
        {
            try
            {
                var field = AccessTools.Field(typeof(Skill), "Locked");
                if (field != null) field.SetValue(skill, false);
            }
            catch (Exception ex)
            {
                LpLog.LogWarning("[开锁技能槽位] 解除技能锁定失败（进度仍照常记录）：" + ex.Message);
            }
        }
    }
}
