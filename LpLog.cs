// SPDX-License-Identifier: AGPL-3.0-or-later
using BepInEx.Logging;

namespace LockpickStandalone
{
    /// <summary>本模组统一日志出口（不依赖任何外部 mod）。</summary>
    internal static class LpLog
    {
        internal static ManualLogSource Log;
        internal static bool Verbose;

        internal static void Info(string m) { Log?.LogInfo(m); }
        internal static void Warn(string m) { Log?.LogWarning(m); }
        internal static void Error(string m) { Log?.LogError(m); }
        internal static void Debug(string m) { if (Verbose) Log?.LogInfo(m); }
        internal static void Always(string m) { Log?.LogInfo(m); }

        // 兼容迁移自旧工程的调用名（保持文本一致，便于与上游代码逐行对账）
        internal static void LogInfo(string m) { Log?.LogInfo(m); }
        internal static void LogWarning(string m) { Log?.LogWarning(m); }
        internal static void LogError(string m) { Log?.LogError(m); }
        internal static void LogDebug(string m) { if (Verbose) Log?.LogInfo(m); }
    }
}