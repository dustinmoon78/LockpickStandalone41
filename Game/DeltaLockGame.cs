// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using LockpickStandalone.Actions;
using LpSounds = LockpickStandalone.Ui.SoundBank;
using LockpickStandalone.Data;
using LockpickStandalone.Interaction;
using LockpickStandalone.Rating;
using LockpickStandalone.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LockpickStandalone.Game
{
    /// <summary>
    /// 开锁小游戏（本模组自写）。玩法：游标在轨道上往返，点击把命中区间逐个锁定，
    /// 打空扣容错并短时冷却；命中区间中心窄条算完美锁定，计入经验与评级。
    /// 另有一条自动解锁保底通道（按时推进，代价是额外消耗工具耐久）。
    /// </summary>
    internal sealed class DeltaLockGame : MonoBehaviour
    {
        private sealed class Zone
        {
            internal float Start, End;      // 轨道归一化位置
            internal float PerfectStart, PerfectEnd;
            internal bool Hit;
        }

        private readonly List<Zone> _zones = new List<Zone>();
        private readonly HashSet<int> _hitColumns = new HashSet<int>();
        private DeltaLockUIBuilder.DeltaUI _ui;
        private Action<bool> _onClosed;

        private float _sliderPos, _sliderSpeed, _cdTimer, _cdDuration;
        private int _slideDir = 1;
        private int _doorLevel, _errors, _required, _hits, _perfects;
        private bool _cooling, _closed, _firstFrameSkipped, _inputErrLogged;
        private float _flash, _autoProgress, _autoSeconds;
        private int _autoCost;
        private int _tier = -1;   // 上一次播报过的台阶档位（升档时弹一次提示，避免每把都弹）
        private float _ringTime;
        // 第三批：失误红闪 / 完美白闪的衰减计时（秒），每帧递减，归零即恢复常态
        private float _errFlash;
        private float _perfectFlash;

        internal int PerfectHits { get { return _perfects; } }

        private static Player MainPlayer
        {
            get
            {
                var w = Comfort.Common.Singleton<GameWorld>.Instance;
                return w == null ? null : w.MainPlayer;
            }
        }

        private void Awake()
        {
            _closed = true;
        }

        private void OnEnable()
        {
            var p = MainPlayer;
            if (p != null && p.IsYourPlayer && p.MovementContext != null)
                p.MovementContext.ToggleBlockInputPlayerRotation(true);
        }

        private void OnDisable()
        {
            RestorePlayerState();
        }

        internal void Bind(DeltaLockUIBuilder.DeltaUI ui)
        {
            _ui = ui;
            // 右侧圆环为纯状态显示：不可点击，退出一律 Esc / 右键（见 WantsExit）。
            // 此处故意不挂任何 onClick，避免误触导致开锁失败。
            _ringTime = 0f;   // 文案语言由 LpText 统一解析，UI 构建期已按字体兜底过
        }

        /// <summary>开始一次开锁。sweetSpotRange 由调用方按门等级算好（-1 表示该门不可开）。</summary>
        internal void Begin(GamePlayerOwner owner, WorldInteractiveObject door, Action<bool> onClosed, float sweetSpotRange)
        {
            _closed = false;
            _onClosed = onClosed;
            _firstFrameSkipped = false;
            enabled = true;

            string keyId = door == null ? null : door.KeyId;
            _doorLevel = LockpickEnv.GetLevelForDoor(owner == null || owner.Player == null ? null : owner.Player.Location, door == null ? null : door.Id, keyId);
            if (_doorLevel < 1) _doorLevel = 1;

            if (_ui.DoorInfoText != null)
                _ui.DoorInfoText.text = LpStrings.LevelPrefix + _doorLevel + "   |   " + LockpickEnv.KeyName(door == null ? null : door.KeyId);

            BuildSpeedAndLimits(sweetSpotRange);
            BuildZones();
            DeltaLockUIBuilder.CreateZoneOverlays(_ui, ToVectorList());
            ResetRun();
            RefreshProgress();
            RefreshErrors();

            // 台阶升档播报：只在「本局档位比上次播报过的高」时弹一次，不重复刷屏
            int tier = LockpickEnv.Tier();
            if (_tier != tier)
            {
                bool rising = _tier >= 0 && tier > _tier;
                _tier = tier;
                if (rising && owner != null)
                    owner.DisplayPreloaderUiNotification(LpText.TierUpBanner(LockpickEnv.TierBanner()));
            }

            string hint = LockPickingPerformance.TakeOutbreakHint(owner);
            if (!string.IsNullOrEmpty(hint) && owner != null) owner.DisplayPreloaderUiNotification(hint);
        }

        private void BuildSpeedAndLimits(float sweetSpotRange)
        {
            float skillBuff = 1f + LockpickEnv.Forgiveness() * 2f;
            float mul = LpConfig.SliderSpeedMultiplier.Value * LockPickingPerformance.OutbreakSpeedMultiplier;
            _sliderSpeed = Mathf.Clamp(0.35f * (1f + _doorLevel * 0.25f) / skillBuff * mul, 0.1f, 3f);
            // 容错 = 门难度基线 + 用户额外容错 + 技能台阶（每 10 级 +1，见 LockpickEnv.TierErrors）
            _errors = Mathf.Clamp(6 - _doorLevel, 1, 5) + LpConfig.BonusErrors.Value
                + Mathf.RoundToInt(LockpickEnv.TierErrors());
            _cdDuration = Mathf.Clamp(1.5f - _doorLevel * 0.1f, 0.5f, 1.5f);
            _cdTimer = 0f;
            _cooling = false;
            _sweetSpotRange = sweetSpotRange;
        }

        private float _sweetSpotRange;

        private float ZoneWidth
        {
            get
            {
                float forgiveness = 1f + LockpickEnv.Forgiveness();
                return Mathf.Clamp(0.20f / (1f + _doorLevel * 0.4f) * forgiveness * LpConfig.ZoneWidthMultiplier.Value, 0.03f, 0.9f);
            }
        }

        private void BuildZones()
        {
            _zones.Clear();
            int count = Mathf.Clamp(_doorLevel, 1, 8);
            float width = ZoneWidth;
            float pad = width * 0.3f;
            float perfectRatio = LpConfig.PerfectZoneRatio.Value;
            const float lo = 0.05f, hi = 0.95f;
            float span = hi - lo;

            if (count * width + (count - 1) * pad > 0.9f)
                width = Mathf.Max(0.03f, (0.9f - (count - 1) * pad) / count);

            for (int i = 0; i < count; i++)
            {
                float start = 0f, end = 0f;
                bool placed = false;
                for (int attempt = 0; attempt < 50 && !placed; attempt++)
                {
                    start = lo + UnityEngine.Random.Range(0f, Mathf.Max(0.01f, span - width));
                    end = start + width;
                    placed = true;
                    for (int j = 0; j < _zones.Count; j++)
                    {
                        if (start < _zones[j].End + pad && end > _zones[j].Start - pad) { placed = false; break; }
                    }
                }
                if (!placed)
                {
                    start = lo + i * (width + pad);
                    end = Mathf.Min(start + width, hi);
                }
                var z = new Zone();
                z.Start = start;
                z.End = end;
                if (perfectRatio > 0f)
                {
                    float center = (start + end) * 0.5f;
                    float half = (end - start) * 0.5f * Mathf.Clamp01(perfectRatio);
                    z.PerfectStart = center - half;
                    z.PerfectEnd = center + half;
                }
                else
                {
                    z.PerfectStart = 0f;
                    z.PerfectEnd = 0f;
                }
                _zones.Add(z);
            }
            _required = Mathf.Max(1, Mathf.CeilToInt(_zones.Count * 0.6f));
        }

        private List<Vector2> ToVectorList()
        {
            var list = new List<Vector2>(_zones.Count);
            for (int i = 0; i < _zones.Count; i++) list.Add(new Vector2(_zones[i].Start, _zones[i].End));
            return list;
        }

        private void ResetRun()
        {
            _sliderPos = 0f;
            _slideDir = 1;
            _hits = 0;
            _perfects = 0;
            _flash = 0f;
            _errFlash = 0f;
            _perfectFlash = 0f;
            _autoProgress = 0f;
            _hitColumns.Clear();
            for (int i = 0; i < _zones.Count; i++) _zones[i].Hit = false;

            int seconds = LpConfig.AutoUnlockSeconds.Value;
            // 自动解锁代价随技能台阶递减，最低不低于 1（保证自动通道仍有代价）
            _autoCost = Mathf.Max(1, LpConfig.AutoUnlockDurabilityCost.Value
                - Mathf.RoundToInt(LockpickEnv.TierDurabilityRelief()));
            _autoSeconds = (seconds > 0 && ToolDurability.CanAfford(_autoCost)) ? seconds : 0f;

            if (_ui.HintText != null)
            {
                _ui.HintText.text = (seconds > 0 && _autoSeconds <= 0f
                    ? LpText.AutoUnlockUnavailable + "   |   "
                    : "") + LpText.ClickToLock + "   |   " + LpText.ExitHint
                    + "   |   " + LockpickEnv.TierBanner();
            }
            if (_zones.Count > 0) DeltaLockUIBuilder.UpdateTeeth(_ui, ToVectorList(), _hitColumns);
            // 第三批：开局先把统计行与线框图刷成初始态（否则首帧是空文本）
            DeltaLockUIBuilder.UpdateHitStats(_ui, _hits, _required, _perfects);
            DeltaLockUIBuilder.UpdateDiagram(_ui, _autoProgress, false, false);
            if (_ui.CdFill != null) _ui.CdFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            DeltaLockUIBuilder.SetCursorPosition(_ui, _sliderPos);
            DeltaLockUIBuilder.SetCursorColor(_ui, false);
        }

        private void Update()
        {
            if (_closed) return;

            if (!_firstFrameSkipped) _firstFrameSkipped = true;
            else if (WantsExit()) { Finish(false); return; }

            LockInput();

            if (_cooling)
            {
                _cdTimer -= Time.unscaledDeltaTime;
                if (_ui.CdFill != null) _ui.CdFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(_cdTimer / Mathf.Max(0.01f, _cdDuration)), 1f);
                if (_cdTimer <= 0f)
                {
                    _cooling = false;
                    DeltaLockUIBuilder.SetCursorColor(_ui, false);
                    if (_ui.CdFill != null) _ui.CdFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                }
            }

            if (_flash > 0f)
            {
                _flash -= Time.unscaledDeltaTime;
                if (_flash <= 0f) { _flash = 0f; RefreshProgress(); }
            }

            if (_autoSeconds > 0f)
            {
                _autoProgress += Time.unscaledDeltaTime / _autoSeconds;
                if (_autoProgress >= 1f)
                {
                    _autoProgress = 1f;
                    RefreshProgress();
                    if (_hits < _required && _autoCost > 0) ToolDurability.ConsumePicks(_autoCost);
                    Finish(true);
                    return;
                }
                RefreshProgress();
            }

            _sliderPos += _slideDir * _sliderSpeed * Time.unscaledDeltaTime;
            if (_sliderPos >= 1f) { _sliderPos = 1f; _slideDir = -1; }
            else if (_sliderPos <= 0f) { _sliderPos = 0f; _slideDir = 1; }
            if (float.IsNaN(_sliderPos) || float.IsInfinity(_sliderPos)) { _sliderPos = 0f; _sliderSpeed = 0.35f; }
            DeltaLockUIBuilder.SetCursorPosition(_ui, _sliderPos);

            // 右侧“开锁中”状态环动画（恒转 + 呼吸 + 省略号），CD 时降速变红
            _ringTime += Time.unscaledDeltaTime;
            DeltaLockUIBuilder.AnimateUnlockRing(_ui, _ringTime, Time.unscaledDeltaTime, _cooling, _autoProgress);

            // 第三批：失误红闪 / 完美白闪的逐帧衰减（时长写死在触发点，此处只做递减）
            if (_errFlash > 0f) _errFlash = Mathf.Max(0f, _errFlash - Time.unscaledDeltaTime);
            if (_perfectFlash > 0f) _perfectFlash = Mathf.Max(0f, _perfectFlash - Time.unscaledDeltaTime);
            DeltaLockUIBuilder.FlashFeedback(_ui, _errFlash / 0.4f, _perfectFlash / 0.45f);

            if (Input.GetMouseButtonDown(0) && !_cooling) OnClick();
        }

        private void OnClick()
        {
            int idx = -1;
            bool perfect = false;
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                if (z.Hit) continue;
                if (_sliderPos >= z.Start && _sliderPos <= z.End)
                {
                    z.Hit = true;
                    idx = i;
                    perfect = LpConfig.PerfectZoneRatio.Value > 0f && _sliderPos >= z.PerfectStart && _sliderPos <= z.PerfectEnd;
                    if (perfect) _perfects++;
                    break;
                }
            }

            if (idx >= 0)
            {
                _hits++;
                _autoProgress = Mathf.Clamp01(_autoProgress + (_required > 0 ? 1f / _required : 1f));
                if (perfect) { _flash = 1.2f; _perfectFlash = 0.45f; }
                DeltaLockUIBuilder.MarkZoneHit(_ui, idx, ToVectorList(), _hitColumns);
                Play(perfect ? LpSounds.Perfect : LpSounds.Click);
                RefreshProgress();
                if (_autoProgress >= 1f) Finish(true);
                return;
            }

            if (InsideHitZone(_sliderPos)) return;   // 穿过已锁定区间不罚

            _errors--;
            _errFlash = 0.4f;
            _cooling = true;
            _cdTimer = _cdDuration;
            DeltaLockUIBuilder.SetCursorColor(_ui, true);
            Play(LpSounds.Fail);
            RefreshErrors();
            if (_errors <= 0) Finish(false);
        }

        private bool InsideHitZone(float pos)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                if (!_zones[i].Hit) continue;
                if (pos >= _zones[i].Start && pos <= _zones[i].End) return true;
            }
            return false;
        }

        private static bool WantsExit()
        {
            return Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape);
        }

        private void RefreshProgress()
        {
            float pct = Mathf.Clamp01(_autoProgress);
            if (_ui.ProgressText != null)
                _ui.ProgressText.text = _flash > 0f ? LpText.PerfectLock : (_autoSeconds > 0f ? LpText.AutoInProgress : LpText.UnlockInProgress);
            if (_ui.PercentText != null) _ui.PercentText.text = "[" + Mathf.RoundToInt(pct * 100f) + "%]";
            if (_ui.ProgressFill != null) _ui.ProgressFill.fillAmount = pct;
            // 三角洲式逐格点亮：自动解锁通道琥珀色，手动锁定铂金；环内常亮进度弧同步推进
            DeltaLockUIBuilder.UpdateProgressBar(_ui, pct, _autoSeconds > 0f);
            DeltaLockUIBuilder.UpdateRingProgress(_ui, pct);
            // 第三批：环内命中统计 + 左下线框图接线（此前线框图建好从不刷新）
            DeltaLockUIBuilder.UpdateHitStats(_ui, _hits, _required, _perfects);
            DeltaLockUIBuilder.UpdateDiagram(_ui, pct, _hits > 0, _perfects > 0);
        }

        private void RefreshErrors()
        {
            if (_ui.ErrorText != null) _ui.ErrorText.text = LpText.ErrorsLeft(Mathf.Max(0, _errors));
        }

        private void Play(AudioClip clip)
        {
            var src = UiRoot.Audio;
            if (src != null && clip != null) src.PlayOneShot(clip);
        }

        private void LockInput()
        {
            try
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (GamePlayerOwner.MyPlayer != null)
                {
                    GamePlayerOwner.IgnoreInputWithKeepResetLook = true;
                    GamePlayerOwner.IgnoreInputInNPCDialog = true;
                }
            }
            catch (Exception ex)
            {
                if (!_inputErrLogged) { _inputErrLogged = true; LpLog.Error("[Lockpick] 输入锁定异常（本局只报一次）：" + ex.Message); }
            }
        }

        private void RestorePlayerState()
        {
            try
            {
                var p = MainPlayer;
                if (p != null && p.IsYourPlayer)
                {
                    if (p.MovementContext != null) p.MovementContext.ToggleBlockInputPlayerRotation(false);
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    if (GamePlayerOwner.MyPlayer != null)
                    {
                        GamePlayerOwner.IgnoreInputWithKeepResetLook = false;
                        GamePlayerOwner.IgnoreInputInNPCDialog = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LpLog.Error("[Lockpick] 恢复玩家状态异常：" + ex.Message);
            }
        }

        internal void Finish(bool won)
        {
            if (_closed) return;
            _closed = true;
            Play(won ? LpSounds.Win : LpSounds.Fail);
            try
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                var cb = _onClosed;
                _onClosed = null;
                if (cb != null) cb(won);
            }
            catch (Exception ex)
            {
                LpLog.Error("[Lockpick] 开锁回调异常：" + ex.Message);
            }
            finally
            {
                if (_ui.Panel != null) _ui.Panel.gameObject.SetActive(false);
                RestorePlayerState();
            }
        }
    }
}