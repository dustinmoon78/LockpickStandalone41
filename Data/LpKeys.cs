// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace LockpickStandalone.Data
{
    /// <summary>
    /// 钥匙价表：钥匙模板 id -> 门等级。
    /// 数据由本模组自带的转换工具从 SPT 自身的数据库（手册价）换算而来，不含任何第三方模组的数据；
    /// 玩家也可以直接编辑 keys.json 自行调档。命中即用，未命中回落到 doors.json 的地图基准。
    /// </summary>
    internal static class LpKeys
    {
        internal static readonly Dictionary<string, int> Levels = new Dictionary<string, int>();

        private static bool _enabled;

        internal static bool Enabled { get { return _enabled; } }
        internal static int Count { get { return Levels.Count; } }

        internal static void Load(string configDir, bool enabled, string fileName)
        {
            _enabled = enabled;
            Levels.Clear();
            if (string.IsNullOrEmpty(fileName)) fileName = "keys.json";
            string path = Path.Combine(configDir, "com.dsh.lockpickstandalone", fileName);
            if (!enabled)
            {
                LpLog.Info("[Lockpick] 按钥匙价格分级已关闭：门等级改用 doors.json 的 mapBase / doorOverrides");
                return;
            }
            try
            {
                if (!File.Exists(path))
                {
                    LpLog.Warn("[Lockpick] 钥匙价表不存在：" + path + "（门等级改用 doors.json 的 mapBase / doorOverrides）");
                    return;
                }
                var keys = JObject.Parse(File.ReadAllText(path))["keys"] as JObject;
                if (keys == null)
                {
                    LpLog.Warn("[Lockpick] 钥匙价表缺少 keys 段：" + path);
                    return;
                }
                foreach (var p in keys.Properties())
                {
                    if (p.Value == null || p.Value.Type == JTokenType.Null) continue;
                    int v;
                    if (!int.TryParse(p.Value.ToString(), out v) || v < 1) continue;
                    Levels[p.Name] = v;
                }
                LpLog.Info("[Lockpick] 钥匙价表载入完成：" + Levels.Count + " 把钥匙（按钥匙价格分级；查不到价的门回落 doors.json 基准）");
            }
            catch (System.Exception ex)
            {
                Levels.Clear();
                LpLog.Error("[Lockpick] 钥匙价表解析失败，门等级改用 doors.json：" + ex.Message);
            }
        }

        /// <summary>命中返回门等级；未命中返回 0（调用方回落地图基准）。</summary>
        internal static int GetLevel(string keyId)
        {
            if (!_enabled || string.IsNullOrEmpty(keyId)) return 0;
            int lv;
            return Levels.TryGetValue(keyId, out lv) ? lv : 0;
        }
    }
}
