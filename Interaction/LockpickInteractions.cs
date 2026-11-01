// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Interactive;
using LockpickStandalone.Actions;
using LockpickStandalone.Data;
using LockpickStandalone.Hack;
using EFT.UI;
using EFT.InventoryLogic;

namespace LockpickStandalone.Interaction
{
    /// <summary>往门的可用交互列表里追加本模组的交互项（自写实现，仅在本地玩家视角生效）。</summary>
    internal static class LockpickInteractions
    {
        /// <summary>
        /// 骇入路线当前是否真的走得通（开关全开 + 有工具或不需要工具）。
        /// ★刻意**不判冷却**：冷却是临时状态，若因冷却就把原版刷卡路径还给玩家，
        ///   等于给冷开了一个后门 —— 冷却期间刷卡反而能开门。
        /// </summary>
        private static bool HackRouteUsable()
        {
            if (!LpConfig.Enabled.Value || !LpConfig.KeycardAsLock.Value || !LpConfig.HackEnabled.Value) return false;
            return !LpConfig.RequireTools.Value || ToolCatalog.HasHack();
        }

        internal static void AddLockpick(WorldInteractiveObject door, AvailableInteractionState state, GamePlayerOwner owner)
        {
            if (door == null || state == null || state.Actions == null) return;
            if (LockpickEnv.IsBotInteraction(owner)) return;
            bool usable = IsPickable(door);
            bool hasPick = !LpConfig.RequireTools.Value || LockpickEnv.GetLockPicksInInventory().Any();
            var action = new InteractionAction();
            if (usable)
            {
                action.Name = LpStrings.PickLock;
                action.Disabled = !door.Operatable && !hasPick;
                action.Action = (Action)delegate { LockpickActions.StartPickLock(door, owner); };
            }
            else
            {
                if (door.DoorState == EDoorState.Open || door.DoorState == EDoorState.Shut) return;   // 已开/已关不需要提示
                action.Name = LpStrings.CannotOpen;
                action.Disabled = door.Operatable;
                action.Action = (Action)delegate { LockpickActions.NotifyCannotOpen(owner); };
            }
            state.Actions.Add(action);
        }

        /// <summary>
        /// 追加「骇入」交互项。它是**门禁卡门（电子锁）专属**路线，机械锁一律不给 ——
        /// 电子锁才配得上「电子破解」这个名字，而且机械锁玩家看到一条永远点不动的死条目只会困惑。
        ///
        /// 冷却中把名字直接换成「骇入装置冷却中 X」，并置灰：玩家不必点一下才知道自己在冷却里。
        /// 菜单重建时机由游戏决定，所以冷却剩余秒数可能与实际差一拍；真正的门槛在 StartHack 里再查一次。
        /// </summary>
        internal static void AddHack(WorldInteractiveObject door, AvailableInteractionState state, GamePlayerOwner owner)
        {
            if (door == null || state == null || state.Actions == null) return;
            if (!LpConfig.Enabled.Value || !LpConfig.KeycardAsLock.Value || !LpConfig.HackEnabled.Value) return;
            if (LockpickEnv.IsBotInteraction(owner)) return;
            if (!IsPickable(door)) return;

            // 菜单每次重建都会走到这里，是判断「换局了没」最自然的挂点
            HackCooldown.TickSession();

            var action = new InteractionAction();
            string cooling = HackCooldown.Describe();
            if (cooling != null)
            {
                action.Name = cooling;
                action.Disabled = true;
                action.Action = (Action)delegate { LockpickActions.Notify(owner, cooling); };
            }
            else if (LpConfig.RequireTools.Value && !ToolCatalog.HasHack())
            {
                action.Name = LpStrings.Hack;
                action.Disabled = true;
                action.Action = (Action)delegate { LockpickActions.Notify(owner, LpText.NoHackTool); };
            }
            else
            {
                action.Name = LpStrings.Hack;
                action.Disabled = false;
                action.Action = (Action)delegate { LockpickActions.StartHack(door, owner); };
            }
            state.Actions.Add(action);
        }

        internal static void AddLockpick(WorldInteractiveObject door, AvailableInteractionState state, GamePlayerOwner owner, bool dedupForeign)
        {
            AddLockpick(door, state, owner);
            if (dedupForeign) RemoveDuplicateActions(state);
        }

        /// <summary>
        /// 同名交互项只保留最先出现的那一条。第三方模组在门禁卡门上会把同一条追加两次：
        /// WorldInteractiveObject 那层与 KeycardDoor 那层共用同一个 AvailableInteractionState，
        /// 它的两个后置补丁各 Add 一次（中间还夹着游戏自己加的一批动作，所以不是相邻重复）。
        /// 游戏原版每个动作名在同一个列表里本来只出现一次，故本方法对原版是恒等变换。
        /// 空名字的条目一律不动（原版有若干动作名来自本地化串，空名不参与比对）。
        /// </summary>
        internal static void RemoveDuplicateActions(AvailableInteractionState state)
        {
            if (state == null) return;
            var list = state.Actions;
            if (list == null || list.Count < 2) return;
            try
            {
                int removed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    string name = list[i] == null ? null : list[i].Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    for (int j = 0; j < i; j++)
                    {
                        if (list[j] == null) continue;
                        if (!string.Equals(list[j].Name, name, StringComparison.Ordinal)) continue;
                        list.RemoveAt(i);
                        removed++;
                        break;
                    }
                }
                if (removed > 0 && state.SelectedAction != null && !list.Contains(state.SelectedAction))
                {
                    state.SelectedAction = null;
                    state.InitSelected();
                }
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 交互项去重跳过：" + ex.Message);
            }
        }

        /// <summary>
        /// 接管门禁卡门的交互菜单：把原生「背包里每张卡各列一条」的刷卡条目整个换掉，
        /// 改成本模组自己的「撬锁 / 骇入」两条路线。
        ///
        /// 【为什么可以整个清空】
        /// 游戏里 KeycardDoor 重载有四支（InteractionContextHelper.cs:1954-2006）：
        ///   锁着 + proxy    → 只建刷卡条目（无卡则 Error=No keycards）
        ///   锁着 + 非 proxy → 只写 Error=Locked，且**不转发**
        ///   未锁 + proxy    → 只写 Error=Unlocked
        ///   未锁 + 非 proxy → 转发 Door 重载（OpenDoor 等）
        /// 而 Player.cs:29374 的保留条件保证了：非 Locked 时 proxy 必为 false。
        /// 两条合起来 ⇒ **只有「锁着 + proxy」这一支会以 isProxy=true 进入本方法**，
        /// 而它除了刷卡条目什么都不加。所以这里拿到的列表内容是穷举得尽的，
        /// 整体清空不会误删合法的开门交互（这一点经反编译逐支核对过）。
        ///
        /// 【为什么不按条目名前缀筛选了】
        /// 曾经按 `"Use item"` 前缀找刷卡条目 —— 那是错的。游戏真实的名字是
        /// `string.Format("Try_keycard {0}".Localized(), 卡名)`，中文落到「尝试 卡名」，
        /// 整个项目里根本没有 `"Use item"` 这个字面量。前缀匹配恒失败 ⇒ 这条接管路径从未生效过。
        ///
        /// 【为什么在这里补「撬锁」是必需的】
        /// 门锁着的时候，KeycardDoor 重载不会转发给 Door / WorldInteractiveObject 重载，
        /// 所以挂在更内层的 DoorActionPatch 那一条根本不会跑 —— 不补就没有「撬锁」。
        /// </summary>
        internal static void TakeOverKeycardDoor(WorldInteractiveObject door, AvailableInteractionState state, GamePlayerOwner owner)
        {
            if (state == null || state.Actions == null) return;
            var list = state.Actions;

            try
            {
                // ★不做「两条路线都走不通就退回原生刷卡列表」的兜底（2026-10-17 用户拍板）：
                //   兜底会让「带着卡、没带工具」的玩家又看到一长串卡，与「和机械锁一样」的诉求相悖。
                //   缺工具 / 移动中 / 门未登记难度 / 锁已报废，一律改由点击后的 StartPickLock 逐条说明
                //   （LockpickActions.cs:40-65），条目本身恒可点。
                // ★「本模组在这扇门上一无所能」的情形必须原样放过（2026-10-17 审查 P0-1）：
                //   IsPickable 为假 ＝ 门没有钥匙位或不可操作，此时 AddHack（本文件 :61 同一判据）也进不去，
                //   而原生刷卡列表对这类门仍可能是**唯一**开门手段 —— KeycardDoor.cs:198 判卡用的是
                //   `KeyId 命中 || _additionalKeys 命中`，PlayerOwner.cs:517 取卡又不过滤 KeyId，
                //   所以「KeyId 为空」的卡门照样能刷卡开。清空它 = 带卡也开不了的硬死局。
                //   注意这**不是**复活被拍板删掉的那层兜底：旧兜底是在「门可撬、只是没带工具」时退让，
                //   与「和机械锁一样」的诉求相悖；这条只在门压根撬不了时才让路。
                if (!IsPickable(door)) return;

                int nativeCount = list.Count;

                // ① 原版「身上每张卡各一条」的刷卡条目整个收掉。
                //    只收刷卡条目本身：第三方挂在同一锚点上的条目（SPTOpenSesame 的 DoNothing / OpenSesame 等）
                //    一条都不动，两边谁先跑谁后跑都能共存（2026-10-17 用户清单）。
                bool hadKeycardEntries = RemoveKeycardEntries(list);

                // 记下「本模组条目」的起始位置：全部加完后要把这一段整体挪到最前，见 ④ 之后的说明
                int before = list.Count;

                // ② 用卡的选项收成一条「开锁」：名字用游戏原版 key（中文即「开锁」），与钥匙门同名同形态。
                //    只在身上确实有能开这扇门的卡时才给 —— 判据与 KeycardDoor.cs:198 一致。
                KeycardComponent usableCard = FindUsableKeycard(door, owner);
                if (usableCard != null)
                {
                    KeycardComponent card = usableCard;
                    list.Add(new InteractionAction
                    {
                        Name = UnlockActionName,
                        Disabled = false,
                        Action = (Action)delegate { UnlockWithKeycard(door, card, owner); }
                    });
                }

                // ③ 撬锁（万能钥匙）
                list.Add(new InteractionAction
                {
                    Name = LpStrings.PickLock,
                    // 恒可点：置灰的条目玩家点不动，也就无从知道自己差什么（缺工具 / 站位 / 未登记难度）
                    Disabled = false,
                    Action = (Action)delegate { LockpickActions.StartPickLock(door, owner); }
                });
                // ④ 骇入是门禁卡门（电子锁）专属路线，与撬锁并列而非替代它；开关关着时它自己不加
                AddHack(door, state, owner);

                // ⑤ 踹门：原版只在 Door 重载里给这条（InteractionContextHelper.cs:2036-2062），
                //    而卡门锁着时**不转发**到那一层 ⇒ 卡门上原本没有「踹门」。这里照抄原版的条目与置灰口径：
                //    站位不正对门（IsBreachAngle）或门不可踹（CanInteractWithBreach）或人趴着 ⇒ 置灰；
                //    真正踹一脚的判定仍走 Door.BreachSuccessRoll（本模组开了「启用踹门」时由 BreachKickPatch 接管成功率）。
                Door breachDoor = door as Door;
                if (breachDoor != null && owner != null && owner.Player != null)
                {
                    Door bd = breachDoor;
                    bool breachOk = bd.IsBreachAngle(owner.Player.Position) && bd.CanInteractWithBreach;
                    if (breachOk)
                    {
                        EPlayerState st = owner.Player.MovementContext.CurrentState.Name;
                        breachOk = !(st == EPlayerState.ProneIdle || st == EPlayerState.ProneMove || st == EPlayerState.Prone2Stand);
                    }
                    list.Add(new InteractionAction
                    {
                        Name = BreachActionName,
                        Disabled = !breachOk,
                        Action = (Action)delegate
                        {
                            owner.Player.MovementContext.ResetCanUsePropState();
                            var breachResult = Door.Interact(owner.Player, EInteractionType.Breach);
                            if (breachResult.Succeeded) owner.Player.StartInteraction(bd, breachResult.Value, null);
                        }
                    });
                }

                // ★把本模组这几条整体挪到列表最前面（2026-10-17 审查 P1-1，本机必现）：
                //   SPTOpenSesame 比本模组先加载（LogOutput.log:942 vs :980）⇒ 它的 postfix 先跑，
                //   DoNothing / OpenSesame 落在列表头部；而 GamePlayerOwner.cs:851 在 GetAvailableActions
                //   返回后**无条件**调 InitSelected()，取的是「第一条非 Disabled」——
                //   不挪的话默认选中「无动作」而不是「开锁」，菜单顺序也随两个补丁的先后漂移。
                //   挪过之后无论谁先跑，顺序恒为 开锁 / 撬锁 / 骇入 /（第三方的）无动作 / 芝麻开门。
                int added = list.Count - before;
                if (added > 0 && before > 0)
                {
                    List<InteractionAction> mine = list.GetRange(before, added);
                    list.RemoveRange(before, added);
                    list.InsertRange(0, mine);
                }

                // 增删条目之后，原先选中的那条可能已经不在列表里 —— 重挑一次，免得 UI 指向幽灵条目
                if (state.SelectedAction != null && !list.Contains(state.SelectedAction))
                {
                    state.SelectedAction = null;
                    state.InitSelected();
                }
                // 原生的 Error 多是「没有可用门卡」；现在本模组自带路线，这句话不再成立
                state.Error = null;

                // 口径注意：HackRouteUsable 只看开关与工具，不含「这扇门能不能骇」的判据（门判据在 AddHack :61），
                // 所以这里标成「骇入开关」而不是「骇入可用」，免得排查时被误导。
                LpLog.Debug("[Lockpick] 门禁卡门接管：原生 " + nativeCount + " 条 -> " + list.Count
                    + " 条（收掉刷卡条目=" + hadKeycardEntries + " 可用卡=" + (usableCard != null)
                    + " 骇入开关=" + HackRouteUsable() + "）");
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 门禁卡门接管跳过：" + ex.Message);
            }
        }

        /// <summary>游戏原版的解锁动作名（裸 key，由 UI 本地化；中文即「开锁」，与钥匙门同名）。</summary>
        private const string UnlockActionName = "Unlock";

        /// <summary>游戏原版的踹门动作名（裸 key，中文即「踹门」）。</summary>
        private const string BreachActionName = "Breach";

        /// <summary>
        /// 删掉原版「身上每张卡各一条」的刷卡条目，返回是否删到过。
        /// 条目名来自模板 `string.Format("Try_keycard {0}".Localized(), 卡名)`（中文「尝试 卡名」），
        /// 所以按「本地化模板去掉 {0} 后的首尾片段」匹配 —— 不写死中文，换界面语言也不失配。
        /// ★只删这一类条目：第三方（SPTOpenSesame 的 DoNothing / OpenSesame 等）挂在同一锚点上的条目必须原样留下。
        /// </summary>
        private static bool RemoveKeycardEntries(List<InteractionAction> list)
        {
            if (list == null) return false;
            string tpl = "Try_keycard {0}".Localized();
            int at = tpl.IndexOf("{0}", StringComparison.Ordinal);
            if (at < 0) return false;
            string head = tpl.Substring(0, at);
            string tail = tpl.Substring(at + 3);
            // 模板异常（首尾都空）时宁可不删，也不能把整张菜单清空
            if (head.Length == 0 && tail.Length == 0) return false;

            bool removed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                string name = list[i] == null ? null : list[i].Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Length < head.Length + tail.Length) continue;
                if (!name.StartsWith(head, StringComparison.Ordinal)) continue;
                if (tail.Length > 0 && !name.EndsWith(tail, StringComparison.Ordinal)) continue;
                list.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        /// <summary>
        /// 身上第一张能开这扇门的卡；一张都没有则返回 null。
        /// 判据照抄 KeycardDoor.cs:198：主 KeyId 命中，或落在该门的 _additionalKeys 白名单里。
        /// </summary>
        private static KeycardComponent FindUsableKeycard(WorldInteractiveObject door, GamePlayerOwner owner)
        {
            KeycardDoor keycardDoor = door as KeycardDoor;
            if (keycardDoor == null || owner == null) return null;
            try
            {
                foreach (KeycardComponent card in owner.GetAllKeyCards())
                {
                    if (card == null || card.Key == null || card.Key.Template == null) continue;
                    string keyId = card.Key.Template.KeyId;
                    if (string.IsNullOrEmpty(keyId)) continue;
                    if (keyId == keycardDoor.KeyId) return card;
                    string[] extra = keycardDoor._additionalKeys;
                    if (extra != null && Array.IndexOf(extra, keyId) >= 0) return card;
                }
            }
            catch (Exception ex)
            {
                LpLog.Debug("[Lockpick] 清点门卡失败：" + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 用指定门卡开门。动作体照抄原版刷卡条目（InteractionContextHelper.cs:1972-1984）：
        /// 先复位道具使用状态，再走 UnlockOperation，成功后按 Begin / Succeed 两拍发事件并起交互。
        /// </summary>
        private static void UnlockWithKeycard(WorldInteractiveObject door, KeycardComponent card, GamePlayerOwner owner)
        {
            if (door == null || card == null || card.Key == null || owner == null || owner.Player == null) return;
            try
            {
                owner.Player.MovementContext.ResetCanUsePropState();
                var unlockResult = door.UnlockOperation(card.Key, owner.Player, door);
                if (unlockResult.Failed)
                {
                    LockpickActions.Notify(owner, LpText.NoUsableKeycard);
                    return;
                }
                unlockResult.Value.RaiseEvents(owner.Player.InventoryController, CommandStatus.Begin);
                owner.Player.StartInteraction(door, unlockResult.Value, delegate
                {
                    unlockResult.Value.RaiseEvents(owner.Player.InventoryController, CommandStatus.Succeed);
                });
            }
            catch (Exception ex)
            {
                LpLog.Warn("[Lockpick] 刷卡开门异常：" + ex.Message);
            }
        }

        /// <summary>可撬：上锁 + 有钥匙位 + 当前不是开启/关闭态。fail-closed，未知一律不开。</summary>
        internal static bool IsPickable(WorldInteractiveObject door)
        {
            if (door == null) return false;
            if (door.DoorState != EDoorState.Locked) return false;
            if (!door.Operatable) return false;
            if (string.IsNullOrEmpty(door.KeyId)) return false;
            return true;
        }
    }
}
