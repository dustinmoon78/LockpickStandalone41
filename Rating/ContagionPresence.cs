// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;

namespace LockpickStandalone.Rating;

/// <summary>
/// CONTAGION（疫潮）模组存在性探测。
///
/// 设计意图：本模组是独立插件，不与任何第三方模组建立程序集引用或硬依赖，
/// 仅通过观察磁盘布局来判断对方是否安装。这样玩家中途装/卸 CONTAGION
/// 不需要重启游戏，重启后行为也自动跟随对方的存在与否。
///
/// 探测方式：以本程序集所在位置反推 BepInEx\plugins 目录，
/// 再检查其中是否存在 CONTAGION 的插件目录或主 DLL。
///
/// 缓存策略：结果带 TTL 缓存。命中缓存时不触碰文件系统，
/// 因此高频调用（每次开锁结算都会问一次）不会产生额外的磁盘 I/O；
/// TTL 到期后自动重查，装/卸对方最多一个 TTL 周期内生效。
/// </summary>
public static class ContagionPresence
{
    /// <summary>缓存存活时长。到期前一律复用上次结果，不重新读盘。</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    /// <summary>上次探测结论；null 表示「尚未探测过」。</summary>
    private static bool? _lastResult;

    /// <summary>上次探测的时刻，用于判断 TTL 是否过期。</summary>
    private static DateTime _lastProbeUtc = DateTime.MinValue;

    /// <summary>
    /// 玩家当前是否处于安装了 CONTAGION 的环境。
    /// 探测失败一律按「未安装」处理，绝不因为一次 IO 异常影响开锁流程。
    /// </summary>
    public static bool IsOutbreakEnvironment
    {
        get
        {
            // TTL 未到且已有结论：直接复用，零 IO
            if (_lastResult.HasValue && DateTime.UtcNow - _lastProbeUtc < CacheLifetime)
                return _lastResult.Value;

            bool present = ProbeFilesystem();

            // 仅在结论发生变化时记账，避免每分钟刷屏同一条日志
            if (!_lastResult.HasValue || _lastResult.Value != present)
            {
                LpLog.LogInfo(present
                    ? "[疫潮联动] 已装载 CONTAGION —— 锁具锈死：开锁难度上升、收益上升"
                    : "[疫潮联动] 未发现 CONTAGION —— 按标准难度与标准收益结算");
            }

            _lastResult = present;
            _lastProbeUtc = DateTime.UtcNow;
            return present;
        }
    }

    /// <summary>
    /// 实际读盘判定。目录存在或主 DLL 存在任一条件成立即视为已安装。
    /// 全程 try/catch 包裹：权限不足、路径过长等一律降级为「未安装」。
    /// </summary>
    private static bool ProbeFilesystem()
    {
        try
        {
            // 本 DLL 位于 <BepInEx>/plugins/LockpickStandalone/ 下，上溯两级即 plugins 根
            string binDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string pluginsDir = binDir != null ? Path.GetDirectoryName(binDir) : null;
            if (string.IsNullOrEmpty(pluginsDir))
                return false;

            // 目录整体安装与「只有主 DLL 没有目录」两种形态都要认
            string contagionDir = Path.Combine(pluginsDir, "NekoContagion");
            if (Directory.Exists(contagionDir))
                return true;

            return File.Exists(Path.Combine(contagionDir, "NekoContagion.Client.dll"));
        }
        catch (Exception ex)
        {
            LpLog.LogWarning("[疫潮联动] 探测异常，按未安装处理: " + ex.Message);
            return false;
        }
    }
}
