// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace LockpickStandalone.Data
{
    /// <summary>
    /// 开锁工具的定位（模板 id 由 F12 配置，默认值为本项目自用环境实测可用值）。
    /// </summary>
    /// <remarks>
    /// 【两套工具、两套耐久池】撬锁工具与骇入工具是两个独立的 id 列表，
    /// 查询、计数、消耗全部分开走，**绝不在任何一处合并**。
    /// 这不是代码风格问题而是玩法约束：玩家可以只带撬锁工具、也可以只带骇入工具，
    /// 两条路线各自的耐久消耗只扣自己那件工具。
    /// </remarks>
    internal static class ToolCatalog
    {
        private static List<string> _pickIds = new List<string>();
        private static string _pickRaw;

        private static List<string> _hackIds = new List<string>();
        private static string _hackRaw;

        internal static void Refresh()
        {
            string raw = LpConfig.LockpickIds.Value;
            if (raw != _pickRaw) { _pickRaw = raw; _pickIds = Split(raw); }

            string hackRaw = LpConfig.HackToolIds.Value;
            if (hackRaw != _hackRaw) { _hackRaw = hackRaw; _hackIds = Split(hackRaw); }
        }

        private static List<string> Split(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            string[] parts = s.Split(new string[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        private static IEnumerable<Item> Carried()
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world == null ? null : world.MainPlayer;
            if (player == null || player.Inventory == null) return Enumerable.Empty<Item>();
            return player.Inventory.GetPlayerItems((EPlayerItems)2);
        }

        private static bool Matches(Item item, List<string> ids)
        {
            if (item == null || ids.Count == 0) return false;
            string tid = item.TemplateId.ToString();
            for (int i = 0; i < ids.Count; i++)
                if (string.Equals(tid, ids[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static IEnumerable<Item> Picks()
        {
            Refresh();
            var ids = _pickIds;
            return Carried().Where(x => Matches(x, ids));
        }

        internal static bool HasPick() { return Picks().Any(); }

        /// <summary>背包里携带的骇入工具。与撬锁工具是两个独立列表，互不干涉。</summary>
        internal static IEnumerable<Item> HackTools()
        {
            Refresh();
            var ids = _hackIds;
            return Carried().Where(x => Matches(x, ids));
        }

        /// <summary>是否带了骇入工具。未配置骇入工具 id 时恒为 false（此时启用骇入也没有意义）。</summary>
        internal static bool HasHack() { return HackTools().Any(); }

        /// <summary>
        /// 取第一件骇入工具用于耐久消耗；没有则返回 null。
        /// 与撬锁侧的耐久消耗走两套逻辑，调用方不要混用。
        /// </summary>
        internal static Item FirstHackTool()
        {
            foreach (var it in HackTools()) return it;
            return null;
        }
    }
}