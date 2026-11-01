// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LockpickStandalone.Actions;
using LockpickStandalone.Data;
using LockpickStandalone.Ui;

namespace LockpickStandalone.Hack;

/// <summary>
/// 三角洲式黑入终端小游戏（Flipper Zero 破解刷卡门）。
/// N 列字符纵向循环滚动，每列一个绿色高亮格；按**鼠标左键**判定——高亮格在中央判定带内则锁定该列，
/// 带外记一次失误 + CD。全部列锁定即黑入成功；失误满即失败。
/// 底部自动解锁进度条每秒自动推进（三角洲式保底通道），走满也开门但磨 Flipper 耐久。
/// </summary>
public class TerminalHackGame : MonoBehaviour
{
    // ---- 状态 ----
    private List<string> _columns = new List<string>();   // 每列字符池（7 字符）
    private List<float> _positions = new List<float>();   // 每列高亮格位置 0-1
    private List<float> _speeds = new List<float>();      // 每列滚动速度（每秒 0-1）
    private List<bool> _locked = new List<bool>();        // 每列是否已锁定
    private List<char> _lockedChars = new List<char>();   // 每列锁定瞬间的字符（选定密码显示用）
    private int _columnCount;
    private int _errorsRemaining;
    private float _cdTimer;
    private float _cdDuration;
    private bool _inCooldown;

    // 自动解锁通道
    private float _autoProgress;
    private float _autoUnlockSeconds;   // 0 = 关闭

    private int _doorLevel;
    private bool _disabled = true;
    private bool _isUnlocked;
    private Action<bool> _onUnlocked;
    private bool _activationFrameConsumed;
    private bool _inputLockErrLogged;

    // 胜利定格：门当帧就开，但面板多留一会儿让玩家看清成功环
    private float _winHold;
    private bool _closing;
    private bool _callbackFired;
    private float _baseSpeed;

    // 失误计数：整局点空了几次。评级口径要用它。
    private int _missed;

    /// <summary>
    /// 评级用的「干净锁定数」：整局一次都没点错时才算满分，否则为 0。
    /// ★口径就是设计里的「密码一次全对 = S」。
    ///   曾经按「单次点击里所有未锁列都在带内」逐次累加，但列数 ≥4 时那种点击现实中
    ///   只可能出现在最后一击（其余列同时进带的概率 ≤1.4%），clean 会恒等于 1 ⇒ 评级永远
    ///   停在 B，而 A/S 才有的耐久退回就成了够不着的摆设。
    /// </summary>
    public int CleanLocks { get { return _missed == 0 ? _columnCount : 0; } }

    private DeltaHackUIBuilder.HackUI _hackUI;

    private static Player Player => Singleton<GameWorld>.Instance?.MainPlayer;

    // ==========================================
    //  Unity 生命周期
    // ==========================================

    public void OnEnable()
    {
        _disabled = false;
        LpLog.LogDebug($"TerminalHackGame.OnEnable id={GetInstanceID()}");
        if (Player != null && Player.IsYourPlayer)
        {
            Player.MovementContext.ToggleBlockInputPlayerRotation(true);
            Player.CurrentManagedState.ChangePose(-1f);
        }
    }

    public void OnDisable()
    {
        _disabled = true;
        LpLog.LogDebug($"TerminalHackGame.OnDisable id={GetInstanceID()}");
        try
        {
            if (Player != null && Player.IsYourPlayer)
            {
                Player.MovementContext.ToggleBlockInputPlayerRotation(false);
                Player.CurrentManagedState.ChangePose(1f);
                Cursor.lockState = (CursorLockMode)1;
                Cursor.visible = false;
                if (GamePlayerOwner.MyPlayer != null)
                {
                    GamePlayerOwner.IgnoreInputWithKeepResetLook = false;
                    GamePlayerOwner.IgnoreInputInNPCDialog = false;
                }
                Singleton<GUISounds>.Instance.PlayUISound((EUISoundType)11);
            }
        }
        catch (Exception ex)
        {
            LpLog.LogError("TerminalHackGame.OnDisable restore error: " + ex);
        }
    }

    public void SetHackUI(DeltaHackUIBuilder.HackUI ui)
    {
        _hackUI = ui;
        if (ui.UnlockButton != null)
            // ★定格期间（_closing）必须挡住这个按钮：那 0.9 秒里门已经开了，
            //   再走一次失败分支会把日志与表现都写成「失败」，只是回调有 FireOnce 兜着不会真的重复开。
            ui.UnlockButton.onClick.AddListener(delegate { if (!_closing) HandleWin(won: false); });
    }

    public void Update()
    {
        // 胜利定格：门已经开了，面板再留 0.9 秒展示「破解成功」，然后才真正收起。
        if (_closing)
        {
            _winHold -= Time.unscaledDeltaTime;
            if (_winHold <= 0f) CloseNow(true);
            return;
        }

        // 退出检查永远优先（同滑条版：首帧跳过防残留按键误退出）
        if (!_activationFrameConsumed)
        {
            _activationFrameConsumed = true;
        }
        else if (!_isUnlocked && ShouldClose())
        {
            HandleWin(won: false);
            return;
        }

        if (_isUnlocked || _disabled) return;

        // 输入锁定（异常只报一次，绝不打断滚动与判定）
        try
        {
            Cursor.lockState = (CursorLockMode)0;
            Cursor.visible = true;
            if (GamePlayerOwner.MyPlayer != null)
            {
                GamePlayerOwner.IgnoreInputWithKeepResetLook = true;
                GamePlayerOwner.IgnoreInputInNPCDialog = true;
            }
        }
        catch (Exception ex)
        {
            if (!_inputLockErrLogged)
            {
                _inputLockErrLogged = true;
                LpLog.LogError("TerminalHackGame input-lock error (logged once): " + ex);
            }
        }

        // CD 计时
        if (_inCooldown)
        {
            _cdTimer -= Time.unscaledDeltaTime;
            if (_cdTimer <= 0f)
            {
                _inCooldown = false;
                DeltaHackUIBuilder.SetBandColor(_hackUI, false);
            }
        }

        // 自动解锁：进度每秒自动推进（unscaled）
        if (_autoUnlockSeconds > 0f)
        {
            _autoProgress += Time.unscaledDeltaTime / _autoUnlockSeconds;
            if (_autoProgress >= 1f)
            {
                _autoProgress = 1f;
                UpdateProgressUI();
                HandleWin(won: true);
                return;
            }
            UpdateProgressUI();
        }

        // 列滚动（unscaled：timeScale=0 时也要动）
        for (int c = 0; c < _columnCount; c++)
        {
            if (_locked[c]) continue;
            _positions[c] += _speeds[c] * Time.unscaledDeltaTime;
            if (_positions[c] >= 1f) _positions[c] -= 1f;
        }
        int active = FirstUnlockedColumn();
        DeltaHackUIBuilder.UpdateColumns(_hackUI, _columns, _positions, _locked, active);

        // ★只认鼠标左键（定稿）：空格在 EFT 里被别的系统占用，而且与撬锁面板的手感也不统一。
        //   GetMouseButtonDown 只在按下那一帧为真，按住不会连发。
        // ★一次只处理「当前可操作列」＝最左边还没锁的那一列（旧口径是点一下判定全部未锁列，
        //   任一一列不在带内就记一次错，于是每列都得挂绿高亮，五团绿并排反而没重点）。
        //   现在只有这一列亮绿，判定也只认它，锁掉后绿色自动推进到下一列。
        bool lockPressed = Input.GetMouseButtonDown(0);
        if (lockPressed && !_inCooldown && active >= 0)
        {
            // 判定带：中央 ±0.12（约 1/4 列高）
            if (Mathf.Abs(_positions[active] - 0.5f) <= 0.12f)
            {
                _locked[active] = true;
                // 捕获锁定瞬间的字符（高亮格所在字符），选定密码显示用
                int highlight = Mathf.Clamp(Mathf.RoundToInt(_positions[active] * (DeltaHackUIBuilder.CellsPerColumn - 1)), 0, DeltaHackUIBuilder.CellsPerColumn - 1);
                _lockedChars[active] = _columns[active][highlight];
                _autoProgress = Mathf.Clamp01(_autoProgress + 1f / _columnCount);

                DeltaHackUIBuilder.UpdateColumns(_hackUI, _columns, _positions, _locked, FirstUnlockedColumn());
                DeltaHackUIBuilder.SetSuccess(_hackUI, _locked.Count(b => b), _columnCount, false);
                UpdatePasswordUI();
                UpdateProgressUI();

                if (_locked.Count(b => b) >= _columnCount)
                {
                    HandleWin(won: true);
                    return;
                }
            }
            else
            {
                _missed++;
                _errorsRemaining--;
                _inCooldown = true;
                _cdTimer = _cdDuration;
                DeltaHackUIBuilder.SetBandColor(_hackUI, true);
                UpdateErrorUI();
                if (_errorsRemaining <= 0)
                {
                    HandleWin(won: false);
                    return;
                }
            }
        }
    }

    // ==========================================
    //  激活入口
    // ==========================================

    /// <summary>开工。door 只用到 Id / KeyId 这两个通用成员，故收 WorldInteractiveObject 而不是 KeycardDoor，
    /// 将来若要放开到普通电子门也无需再改签名。</summary>
    public void Activate(GamePlayerOwner owner, WorldInteractiveObject door, Action<bool> action)
    {
        _isUnlocked = false;
        _closing = false;
        _callbackFired = false;
        _missed = 0;
        _onUnlocked = action;
        _disabled = false;
        enabled = true;
        _activationFrameConsumed = false;
        LpLog.LogInfo($"TerminalHackGame.Activate id={GetInstanceID()}");

        string mapId = owner == null || owner.Player == null ? null : owner.Player.Location;
        int levelForDoor = LockpickEnv.GetLevelForDoor(mapId, door == null ? null : door.Id, door == null ? null : door.KeyId);
        _doorLevel = levelForDoor;
        if (_hackUI.DoorInfoText != null)
            _hackUI.DoorInfoText.text = $"DOOR LEVEL: {levelForDoor}  |  {LockpickEnv.KeyName(door.KeyId)}";

        InitParams();
        GenerateColumns();
        DeltaHackUIBuilder.SetColumnCount(_hackUI, _columnCount);
        DeltaHackUIBuilder.UpdateColumns(_hackUI, _columns, _positions, _locked, FirstUnlockedColumn());
        DeltaHackUIBuilder.SetSuccess(_hackUI, 0, _columnCount, false);
        UpdatePasswordUI();
        UpdateProgressUI();
        UpdateErrorUI();
    }

    // ==========================================
    //  参数与生成
    // ==========================================

    private void InitParams()
    {
        // 密码位数由 F12 配置决定（3~8 位）。不再随门难度变宽 —— 面板宽度必须固定，否则每扇门版式都在跳。
        // 再夹一次面板物理上限：配置上限与面板列数上限一旦不同步，多出来的那几列
        // 既看不见也锁不上，那一局就永远赢不了。宁可少一位，也不能出现赢不了的局。
        _columnCount = Mathf.Min(HackRules.PasswordLength(LpConfig.HackPasswordLength.Value),
            DeltaHackUIBuilder.MaxColumns);
        // 滚动速度：门难度抬高基速、撬锁等级压低它，最后乘 F12 系数。
        // ★骇入的等级门槛整体下移：练到「追平等级」（默认 25）才追平撬锁 1 级的手感，
        //   越低越难（见 HackRules.HackEquivalentLevel / HackPenalty）。
        //   ReelSpeed 的单位就是「圈/秒」，而 _positions 是 0~1 归一化位置，两者同量纲，不必换算。
        float speedMul = LpConfig.HackSpeedMultiplier.Value;
        int lockpickLevel = LockpickEnv.CurrentLevel();
        float effForgiveness = LockpickEnv.ForgivenessFor(HackRules.HackEquivalentLevel(lockpickLevel));
        _baseSpeed = HackRules.ReelSpeed(_doorLevel, effForgiveness, speedMul)
            * HackRules.HackPenalty(lockpickLevel);
        // 容错次数。
        // ★旧口径：一次点击判定全部未锁列，任一列不在带内就记一次错，锁满 N 列至少要 N-1 次失误，
        //   所以额度下限必须等于列数，否则是数学上必输的局（Lv5 的门曾只给 1 次，5 位密码赢不了）。
        // ★现在改成逐列推进：每次点击只判定当前列，理论上能 0 失误连中 N 列，旧额度（列数 + 余量，
        //   5 位密码就是 6 次）在新口径下等于没有惩罚，故按用户 2026-10-22 定稿收紧为「1 + 门难度余量」：
        //   门 Lv1 → 3 次，Lv2~Lv5 → 2 次。
        // ★注意 max(1, 3-门Lv) 在 Lv2 以上会钳平，所以 Lv4/Lv5 并不比 Lv2 更严；
        //   要让高难门更狠，把余量换成 max(1, 5-门Lv) 即可（Lv5 → 1 次），这是留给下一轮的旋钮。
        int tolerance = 1 + Mathf.Max(1, 3 - _doorLevel);
        _errorsRemaining = tolerance + Mathf.RoundToInt(LpConfig.HackErrorBonus.Value);
        // 试错 CD
        _cdDuration = Mathf.Clamp(1.5f - _doorLevel * 0.1f, 0.5f, 1.5f);
        _cdTimer = 0f;
        _inCooldown = false;

        // 自动解锁
        // ★不再检查工具耐久：骇入装置是无限使用的（-1），冷却是唯一的频率限制。
        //   自动通道的代价就只有「耗时」——这段时间里玩家动不了、敌人可能走过来。
        int seconds = LpConfig.HackAutoSeconds.Value;
        _autoUnlockSeconds = seconds > 0 ? seconds : 0f;
        _autoProgress = 0f;

        bool hasChinese = _hackUI.Chinese;
        if (_hackUI.HintText != null)
            _hackUI.HintText.text = hasChinese
                ? "鼠标左键锁定  |  Esc / 右键退出"
                : "LMB to lock  |  Esc / RMB to exit";
        // 常驻操作提示行（判定带正上方）：绿色高亮格进入中央亮带时按鼠标左键锁定该列
        if (_hackUI.ActionHintText != null)
        {
            _hackUI.ActionHintText.text = hasChinese
                ? "绿色密码进入中央亮带时按鼠标左键锁定"
                : "Click when the green code is inside the center band";
        }
    }

    /// <summary>
    /// 当前可操作列＝最左边还没锁的那一列；全部锁完返回 -1。
    /// 判定、绿高亮、失败扣次数全部以它为准，三者必须是同一个来源（否则会出现「提示这一列、判另一列」）。
    /// </summary>
    private int FirstUnlockedColumn()
    {
        for (int c = 0; c < _columnCount; c++)
            if (!_locked[c]) return c;
        return -1;
    }

    private void GenerateColumns()
    {
        _columns.Clear();
        _positions.Clear();
        _speeds.Clear();
        _locked.Clear();
        _lockedChars.Clear();
        var rng = new System.Random();
        for (int c = 0; c < _columnCount; c++)
        {
            char[] chars = new char[7];
            for (int r = 0; r < 7; r++)
                // 用 HackRules.Charset 而不是自带的那串：它刻意避开了 0/O 与 1/l/I 这些
            // 在滚轮里一眼分不清的字符。旧的自带串没避，列滚动时容易看错。
            chars[r] = HackRules.Charset[rng.Next(HackRules.Charset.Length)];
            _columns.Add(new string(chars));
            _positions.Add((float)rng.NextDouble());
            // 每列在基准速度上下浮动 ±30%：完全同步的滚动反而更好点，那样就没有手感可言了
            _speeds.Add(_baseSpeed * (0.7f + (float)rng.NextDouble() * 0.6f));
            _locked.Add(false);
            _lockedChars.Add('_');
        }
    }

    // ==========================================
    //  UI 更新
    // ==========================================

    private void UpdatePasswordUI()
    {
        if (_hackUI.PasswordText == null) return;
        var sb = new System.Text.StringBuilder();
        for (int c = 0; c < _columnCount; c++)
        {
            if (_locked[c])
                sb.Append(_lockedChars[c]);
            else
                sb.Append('_');
            if (c < _columnCount - 1) sb.Append(' ');
        }
        _hackUI.PasswordText.text = sb.ToString();
    }

    private void UpdateProgressUI()
    {
        float pct = Mathf.Clamp01(_autoProgress);
        if (_hackUI.ProgressText != null)
            _hackUI.ProgressText.text = _autoUnlockSeconds > 0f ? ">>自动解锁同步进行中" : ">>破解同步进行中";
        if (_hackUI.PercentText != null)
            _hackUI.PercentText.text = $"[{Mathf.RoundToInt(pct * 100f)}%]";
        if (_hackUI.ProgressFill != null)
            _hackUI.ProgressFill.fillAmount = pct;
    }

    private void UpdateErrorUI()
    {
        if (_hackUI.ErrorText != null)
            _hackUI.ErrorText.text = $"剩余错误: {_errorsRemaining}";
    }

    // ==========================================
    //  关闭 / 胜负处理
    // ==========================================

    private bool ShouldClose()
    {
        return Input.GetMouseButtonDown(1) || Input.GetKeyDown((KeyCode)27);
    }

    /// <summary>回调只许发一次：成功路径先发（门当帧就开），收尾路径不能再发一遍。</summary>
    private void FireOnce(bool won)
    {
        if (_callbackFired) return;
        _callbackFired = true;
        try { _onUnlocked?.Invoke(won); }
        catch (Exception ex) { LpLog.LogError("TerminalHackGame unlock callback error: " + ex); }
    }

    private void HandleWin(bool won = true)
    {
        if (won && !_closing)
        {
            // 成功先定格再收：门立刻开（不拖延玩家），面板留 0.9 秒让玩家看到成功环与「破解成功」。
            _closing = true;
            _isUnlocked = true;
            _winHold = 0.9f;
            DeltaHackUIBuilder.SetSuccess(_hackUI, _columnCount, _columnCount, true);
            HackCooldown.Trigger();
            FireOnce(true);
            return;
        }
        CloseNow(won);
    }

    private void CloseNow(bool won)
    {
        _isUnlocked = true;
        LpLog.LogInfo("TerminalHackGame closing, won=" + won);
        try
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            FireOnce(won);
        }
        catch (Exception ex)
        {
            LpLog.LogError("TerminalHackGame.HandleWin callback error: " + ex);
        }
        finally
        {
            gameObject.SetActive(false);
            try
            {
                if (Player != null)
                {
                    GamePlayerOwner.SetIgnoreInputWithKeepResetLook(false);
                    if (Player.MovementContext != null)
                        Player.MovementContext.ToggleBlockInputPlayerRotation(false);
                    Cursor.visible = false;
                    Cursor.lockState = (CursorLockMode)1;
                }
            }
            catch (Exception ex)
            {
                LpLog.LogError("TerminalHackGame.HandleWin restore error (UI already closed): " + ex);
            }
        }
    }
}
