// SPDX-License-Identifier: AGPL-3.0-or-later
using Comfort.Common;
using EFT;
using UnityEngine;
using LockpickStandalone.Data;

namespace LockpickStandalone.Hack;

/// <summary>
/// 骇入的全局冷却：整个战局同一时刻只有一个冷却值，任何一扇门骇入成功都会重置它。
/// ★它刻意只管骇入 —— 撬锁与踹门完全不受影响，否则冷却会把玩家逼回「只能硬撬」，
///   那就等于把刚加进来的第二条路线又删掉了。
/// 冷却时长不随等级变化：它是战术资源，不是能被刷掉的惩罚。
/// </summary>
internal static class HackCooldown
{
    private static float _until;
    private static GameWorld _world;

    /// <summary>
    /// 每个入口先调一次：靠 GameWorld 实例是否换过判断「进了一局新的」。
    ///
    /// ★不能指望时间戳自然过期：_until 存的是**未来**时刻，切场景不会让它变旧。
    ///   而 ResetSession 原本只挂在「地图字符串变化」上（DoorActionPatch.EnsureSession），
    ///   同一张图连开两局时 map 不变 ⇒ 上一局最多 150 秒的冷却会被直接带进下一局。
    /// </summary>
    internal static void TickSession()
    {
        var world = Singleton<GameWorld>.Instance;
        if (ReferenceEquals(world, _world)) return;
        _world = world;
        Reset();
    }

    /// <summary>剩余冷却秒数；0 表示可以骇入。</summary>
    internal static float Remaining { get { return HackRules.Remaining(_until, Time.unscaledTime); } }

    internal static bool IsCooling { get { return Remaining > 0f; } }

    /// <summary>当前是否在实验室地图。判定用解析后的地图 id，而不是可能为空的原始 locationId。</summary>
    internal static bool OnLabMap()
    {
        return HackRules.IsLab(LockpickEnv.ResolveMap(LockpickEnv.CurrentMap()), LpConfig.HackLabMaps.Value);
    }

    /// <summary>骇入成功后触发冷却；实验室走更长的一档（默认 150 秒）。配置为 0 即完全不冷却。</summary>
    internal static void Trigger()
    {
        bool lab = OnLabMap();
        float seconds = HackRules.CooldownFor(lab, LpConfig.HackCooldownNormal.Value, LpConfig.HackCooldownLab.Value);
        if (seconds <= 0f)
        {
            _until = 0f;
            return;
        }
        _until = Time.unscaledTime + seconds;
        LpLog.Info("[骇入] 进入冷却 " + seconds.ToString("0") + " 秒" + (lab ? "（实验室档）" : ""));
    }

    /// <summary>交互项上的冷却提示；不在冷却中返回 null，调用方据此决定是否禁用「骇入」。</summary>
    internal static string Describe()
    {
        float left = Remaining;
        return left > 0f ? "骇入装置冷却中 " + HackRules.FormatRemaining(left) : null;
    }

    /// <summary>进新战局时清零：上一局的冷却不该继承到这一局。</summary>
    internal static void Reset()
    {
        _until = 0f;
    }
}
