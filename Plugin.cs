// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using BepInEx;
using BepInEx.Configuration;
using LockpickStandalone.Compat;
using LockpickStandalone.Data;
using LockpickStandalone.Patches;
using UnityEngine;

namespace LockpickStandalone
{
    /// <summary>
    /// 万能钥匙（Master Key，GUID 与目录名沿用 com.dsh.lockpickstandalone）：三角洲风格的点击锁定开锁 + 表现评级 + 自注入的开锁技能槽位（等级与进度随存档保存，上限由游戏本身决定）。
    /// 纯客户端、纯代码实现：不含任何 AssetBundle，也不依赖任何第三方模组。
    /// </summary>
    // BE 版命名（工作区规矩）：版本串恒为线上正式号，未正式发布不占号；内测序号挂在显示名上。
    // 正式发布时：版本串 1.0.0 → 1.0.1，显示名去掉「BE 版 N」还原「万能钥匙」，内测序号清零重算。
    [BepInPlugin(Guid, "万能钥匙", "1.0.0")]
    public sealed class LpPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.dsh.lockpickstandalone";

        private void Awake()
        {
            LpLog.Log = Logger;
            LpConfig.Bind(Config);
            LpConfig.PruneDeadKeys(Config);   // 收口：清掉已删除功能留下的死键   
            LpLog.Verbose = LpConfig.DebugLog.Value;
            LpConfig.DebugLog.SettingChanged += delegate (object s, EventArgs e) { LpLog.Verbose = LpConfig.DebugLog.Value; };
            string cfgDir = System.IO.Path.GetDirectoryName(Config.ConfigFilePath);
            LpData.Load(cfgDir);
            LpKeys.Load(cfgDir, LpConfig.PriceTiers.Value, LpConfig.KeyTableFile.Value);
            SeCompat.EnsureTakeover();
            InvokeRepeating("RetrySeTakeover", 3f, 5f);

            TryEnable(new DoorActionPatch(), "门交互");
            TryEnable(new LockPickingFatiguePatch(), "技能疲劳豁免");
            // 技能面板三件套。装配顺序无依赖（挂在不同的目标方法上），但按「数据 → 显示 → 取文」列出更好读。
            // 关键时序：SkillBonusBuffPatch 挂 Skill 构造器，SkillManager 的 44 个 new Skill(...)
            // 全部发生在读档/建档时（BepInEx 插件链早已 Awake 完）⇒ 补丁必然早于技能构造链建立。
            // 它不走 SPT 的 PatchManager：要与 SkillsExtended 的同名构造器前缀定序，必须显式给优先级。
            TryEnable(SkillBonusBuffPatch.Enable, "技能面板加成行");
            TryEnable(new BuffIconTextPatch(), "技能面板加成文案（图标悬停）");
            TryEnable(new BuffPanelTextPatch(), "技能面板加成文案（技能说明列表）");
            TryEnable(new SkillInjectPatch(), "技能槽位注入");
            if (LpConfig.DedupeActions.Value) TryEnable(new KeycardDoorActionPatch(), "门禁卡门交互项去重");
            // 电子锁接管（默认关；去重仍排在接管之前，两者作用于同一锚点）
            if (LpConfig.KeycardAsLock.Value) TryEnable(new KeycardDoorLockPatch(), "门禁卡门转开锁");
            if (LpConfig.BreachEnabled.Value) TryEnable(new BreachKickPatch(), "踹门（概率+代价）");

            LpLog.Info("[Lockpick] 万能钥匙 已加载（自研实现 / 纯代码 UI / 无 bundle / 无外部依赖 / 开锁等级上限 Lv"
                + LockpickLevel.MaxLevel + " / 门禁卡门=" + (LpConfig.KeycardAsLock.Value ? "按锁处理" : "原生刷卡") + "）");
        }

        /// <summary>周期重试接管：SkillsExtended 可能比本模组晚加载。</summary>
        private void RetrySeTakeover()
        {
            if (!SeCompat.Finished) SeCompat.EnsureTakeover();
        }

        private static void TryEnable(SPT.Reflection.Patching.ModulePatch patch, string what)
        {
            try
            {
                patch.Enable();
            }
            catch (System.Exception ex)
            {
                LpLog.Error("[Lockpick] " + what + " 补丁装配失败：" + ex.Message);
            }
        }

        /// <summary>不走 SPT PatchManager 的补丁（需要显式 Harmony 优先级）走这个重载。</summary>
        private static void TryEnable(System.Action enable, string what)
        {
            try
            {
                enable();
            }
            catch (System.Exception ex)
            {
                LpLog.Error("[Lockpick] " + what + " 补丁装配失败：" + ex.Message);
            }
        }
    }
}
