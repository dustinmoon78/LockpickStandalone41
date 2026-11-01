// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using BepInEx;
using LockpickStandalone.Interaction;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LockpickStandalone.Data
{
    /// <summary>
    /// 本模组自有的门等级方案与显示名表（全部数值均为本模组自行设计，不含任何第三方数据）。
    /// 门等级优先级：① keys.json 钥匙价表（按该门钥匙的价格分级，数据由 SPT 自身数据库换算）
    /// ② doorOverrides 单门精确指定 ③ mapBase 地图基准（查不到价的钥匙落这里）。
    /// unknownMapLevel 给未登记地图（新装的自定义地图等）一个兜底等级，0 表示该图不提供开锁。
    /// </summary>
    internal static class LpData
    {
        internal static readonly Dictionary<string, int> MapBase = new Dictionary<string, int>
        {
            // 键 = SPT locations/<dir>/base.json 的 Id，也就是运行期 Player.Location / GameWorld.LocationId 的值。
            // ★大小写严格敏感：4.1 的真实 Id 大多是首字母大写（Woods/Shoreline/Interchange/RezervBase/
            // Lighthouse/TarkovStreets/Labyrinth/Terminal/Suburbs），写小写会对不上、整图落到 unknownMapLevel。
            { "factory4_day", 1 }, { "factory4_night", 1 }, { "Woods", 2 }, { "bigmap", 3 },
            { "Shoreline", 3 }, { "Interchange", 4 }, { "RezervBase", 5 }, { "Lighthouse", 5 },
            { "TarkovStreets", 5 }, { "laboratory", 4 },
            // Ground Zero 有两个变体（Id 分别是 Sandbox 与 Sandbox_high，同名不同图）。
            { "Sandbox", 2 }, { "Sandbox_high", 2 },
            // 自定义/扩展图：迷宫与 Terminal 是它们自己的 Id，破冰船复用 Suburbs。
            { "Labyrinth", 3 }, { "Terminal", 3 }, { "Suburbs", 3 },
        };

        internal static readonly Dictionary<string, Dictionary<string, int>> DoorOverrides =
            new Dictionary<string, Dictionary<string, int>>();
        internal static readonly Dictionary<string, string> KeyNames = new Dictionary<string, string>();

        /// <summary>未登记地图的兜底等级出厂值；0 表示未登记地图不提供开锁。</summary>
        internal const int DefaultUnknownMapLevel = 4;

        /// <summary>未登记地图的兜底等级（读 doors.json 的 unknownMapLevel；缺该键时用 DefaultUnknownMapLevel）。</summary>
        internal static int UnknownMapLevel = DefaultUnknownMapLevel;
        internal static int MaxLevel = 8;

        private static string _path;
        private static bool _loggedUnknownMap;

        internal static void Load(string configDir)
        {
            _path = Path.Combine(configDir, "com.dsh.lockpickstandalone", "doors.json");
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path, BuildDefault(), new System.Text.UTF8Encoding(false));
                    LpLog.Info("[Lockpick] 已生成默认 doors.json：" + _path);
                    return;
                }
                var root = JObject.Parse(File.ReadAllText(_path));
                ApplyMapBase(root["mapBase"] as JObject);
                ApplyDoorOverrides(root["doorOverrides"] as JObject);
                ApplyKeyNames(root["keyNames"] as JObject);
                UnknownMapLevel = Int(root["unknownMapLevel"], DefaultUnknownMapLevel);
                MaxLevel = System.Math.Max(1, Int(root["maxLevel"], 8));
                LpLog.Info("[Lockpick] doors.json 载入完成：地图 " + MapBase.Count + " 张 / 单门覆盖 " + CountOverrides() + " 条 / 钥匙名 " + KeyNames.Count + " 条");
            }
            catch (System.Exception ex)
            {
                LpLog.Error("[Lockpick] doors.json 解析失败，改用内置默认值：" + ex.Message);
            }
        }

        private static int CountOverrides()
        {
            int n = 0;
            foreach (var kv in DoorOverrides) n += kv.Value.Count;
            return n;
        }

        private static void ApplyMapBase(JObject o)
        {
            if (o == null) return;
            MapBase.Clear();
            foreach (var p in o.Properties()) MapBase[p.Name] = Int(p.Value, 0);
        }

        private static void ApplyDoorOverrides(JObject o)
        {
            DoorOverrides.Clear();
            if (o == null) return;
            foreach (var map in o.Properties())
            {
                var inner = map.Value as JObject;
                if (inner == null) continue;
                var d = new Dictionary<string, int>();
                foreach (var p in inner.Properties()) d[p.Name] = Int(p.Value, 0);
                DoorOverrides[map.Name] = d;
            }
        }

        private static void ApplyKeyNames(JObject o)
        {
            KeyNames.Clear();
            if (o == null) return;
            foreach (var p in o.Properties()) KeyNames[p.Name] = p.Value.ToString();
        }

        private static int Int(JToken t, int dflt)
        {
            if (t == null || t.Type == JTokenType.Null) return dflt;
            int v;
            return int.TryParse(t.ToString(), out v) ? v : dflt;
        }

        private static string BuildDefault()
        {
            var root = new JObject();
            root["_说明"] = "门等级优先级：① keys.json 的钥匙价表（按该门钥匙的价格分级）② doorOverrides 单门指定（地图 -> 门id -> 等级）③ mapBase 地图基准（查不到价的钥匙走这里）；keyNames=钥匙显示名；unknownMapLevel=未知/自定义地图的等级，0 表示该地图不提供开锁；maxLevel=等级上限";
            root["unknownMapLevel"] = DefaultUnknownMapLevel;
            root["maxLevel"] = 8;
            var mb = new JObject();
            foreach (var kv in MapBase) mb[kv.Key] = kv.Value;
            root["mapBase"] = mb;
            root["doorOverrides"] = new JObject();
            root["keyNames"] = new JObject();
            return root.ToString(Formatting.Indented);
        }

        internal static bool IsKnownMap(string map)
        {
            if (string.IsNullOrEmpty(map)) return false;
            return MapBase.ContainsKey(map) || DoorOverrides.ContainsKey(map);
        }

        /// <summary>
        /// 返回门等级；-1 表示该门不参与开锁。
        /// 优先级：① 钥匙价表（keys.json，按该门钥匙的价格分级，实验室钥匙另有独立档位）
        ///         ② doors.json 的 doorOverrides 单门指定
        ///         ③ doors.json 的 mapBase 地图基准（查不到价的钥匙落这里）
        /// </summary>
        internal static int GetLevel(string map, string doorId, string keyId)
        {
            if (string.IsNullOrEmpty(map)) return -1;

            int byKey = LpKeys.GetLevel(keyId);
            if (byKey > 0) return Clamp(byKey);

            Dictionary<string, int> perDoor;
            if (DoorOverrides.TryGetValue(map, out perDoor))
            {
                int ov;
                if (!string.IsNullOrEmpty(doorId) && perDoor.TryGetValue(doorId, out ov) && ov > 0) return Clamp(ov);
            }
            int baseLevel;
            if (!MapBase.TryGetValue(map, out baseLevel))
            {
                if (!_loggedUnknownMap)
                {
                    _loggedUnknownMap = true;
                    LpLog.Warn("[Lockpick] 地图「" + map + "」不在 doors.json 方案内，按未登记地图等级处理：Lv." + UnknownMapLevel + "（可在 mapBase 中登记）");
                }
                return UnknownMapLevel > 0 ? Clamp(UnknownMapLevel) : -1;
            }
            if (baseLevel <= 0) return -1;
            return Clamp(baseLevel);
        }

        private static int Clamp(int lv)
        {
            if (lv < 1) return 1;
            return lv > MaxLevel ? MaxLevel : lv;
        }

        internal static string KeyName(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return LpText.NoKey;
            string name;
            if (KeyNames.TryGetValue(keyId, out name) && !string.IsNullOrEmpty(name)) return name;
            // 门外看到的显示名：未登记的钥匙按 id 前 6 位兜底（这条会进英文界面，走 LpText）
            return keyId.Length >= 6 ? LpText.UnknownKeyPrefix(keyId.Substring(0, 6)) : LpText.UnknownKey;
        }
    }
}