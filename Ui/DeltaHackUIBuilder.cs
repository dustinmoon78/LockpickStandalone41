// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LockpickStandalone.Hack;
using LockpickStandalone.Interaction;

namespace LockpickStandalone.Ui;

/// <summary>
/// 三角洲式黑入终端面板 UI 构建器（视觉层，对照三角洲大保险柜字符列破解界面）。
/// 居中深蓝灰面板：顶部信息行 + N 列字符列阵（每列 7 个字符格，绿色高亮在列内上下滚动）
/// + 中央水平判定带 + 右侧「选定密码」区 + 底部格纹进度条 + 圆环取消按钮。
/// 判定逻辑在 TerminalHackGame，本类只负责构建与刷新视觉。
/// 所有尺寸按 Screen 比例计算，适配分辨率。
/// </summary>
public static class DeltaHackUIBuilder
{
    // ---- 配色（对照三角洲：深蓝灰面板 + 青灰边框 + 荧光绿高亮） ----
    private static readonly Color PanelBgColor = new(0.07f, 0.11f, 0.14f, 0.93f);
    private static readonly Color PanelBorderColor = new(0.35f, 0.65f, 0.60f, 0.55f);
    private static readonly Color CellColor = new(0.55f, 0.58f, 0.62f, 0.9f);        // 普通字符：灰
    private static readonly Color HighlightColor = new(0.2f, 1f, 0.47f, 1f);         // 绿色高亮（当前候选）
    private static readonly Color LockedColor = new(0.2f, 1f, 0.47f, 0.55f);         // 已锁定列：暗绿
    private static readonly Color BandColor = new(0.2f, 1f, 0.47f, 0.18f);           // 判定带：淡绿
    private static readonly Color BandCdColor = new(0.95f, 0.25f, 0.2f, 0.22f);      // CD 判定带：淡红
    private static readonly Color ProgressBgColor = new(0.13f, 0.18f, 0.16f, 0.9f);
    private static readonly Color ProgressFillColor = new(0.85f, 0.88f, 0.86f, 1f);
    private static readonly Color ProgressSlotColor = new(0.07f, 0.11f, 0.14f, 1f);
    private static readonly Color RingColor = new(0f, 0.85f, 0.45f, 1f);
    private static readonly Color TextColor = new(0.75f, 0.78f, 0.8f, 1f);
    private static readonly Color LabelColor = new(0.45f, 0.52f, 0.52f, 0.85f);

    public const int CellsPerColumn = 7;

    /// <summary>
    /// 列阵的物理上限：面板一次建满这么多列，实际显示几位密码由 SetColumnCount 决定。
    /// ★必须与 HackRules.PasswordLength 的上限一致 —— 建得比它少，超出部分永远看不见也锁不上，
    ///   那一局就再也赢不了了（见 SetColumnCount 的说明）。
    /// </summary>
    public const int MaxColumns = 8;

    private const int ProgressSlotCount = 40;

    /// <summary>与撬锁面板同档界面缩放（用户 2026-10-08 定为 1.1）。只放大尺寸与偏移，比例系数不动。</summary>
    private const float UiScale = 1.1f;

    public struct HackColumn
    {
        public RectTransform Container;
        public List<Text> Cells;
    }

    public struct HackUI
    {
        public RectTransform Panel;
        public Text DoorInfoText;
        public Text ActionHintText;
        public List<HackColumn> Columns;
        public Image Band;
        public Text PasswordText;
        public Image ProgressFill;
        public Text ProgressText;
        public Text PercentText;
        public Text ErrorText;
        public Text HintText;
        public Button UnlockButton;
        /// <summary>右侧成功环：按已锁列数径向填充，破解成功时点亮整环。</summary>
        public Image SuccessRing;
        public Text SuccessText;
        /// <summary>本面板最终是否拿到汉字字形；环下文案选中文还是英文由它决定。</summary>
        public bool Chinese;
        /// <summary>列阵可用总宽度，SetColumnCount 重排列间距时要用。</summary>
        public float TrackWidth;
    }

    /// <summary>
    /// 在给定 Canvas 下构建居中黑入面板，返回引用集合。
    /// </summary>
    public static HackUI Build(Canvas canvas)
    {
        var ui = new HackUI { Columns = new List<HackColumn>() };

        // ★canvas 为空时下面每一行都会 NRE（:114 直接解引用 canvas.transform）。
        //   被 GameHost 的 try 兜住只会留一行带调用栈的 Error，
        //   现场看不出是「Canvas 没就绪」还是别的毛病。
        if (canvas == null)
        {
            LpLog.Error("[Lockpick] UI Canvas 未就绪，本次骇入面板构建跳过");
            return default;
        }

        // ---- A1: 夺回缩放控制权（同 DeltaLockUIBuilder） ----
        if (canvas != null)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.referenceResolution = Vector2.zero;
                scaler.matchWidthOrHeight = 0f;
                scaler.scaleFactor = 1f;
            }
            Canvas.ForceUpdateCanvases();
        }

        // ---- A2: 面板尺寸（全比例，零硬编码像素） ----
        float sw = Screen.width * UiScale;
        float sh = Screen.height * UiScale;
        float panelW = Mathf.Max(sw * 0.46f, sw * 0.30f);
        float panelH = Mathf.Max(sh * 0.54f, sh * 0.35f);
        float trackW = panelW * 0.84f;

        Font font = ResolveFont(canvas != null ? canvas.transform : null);
        // 与撬锁面板用同一套判据：先看游戏界面语言（LpText 内部解析 LocalizationManager.Culture），
        // 只有在读不到可信语言、且字体确实没有汉字字形时才退回英文，避免出现方块。
        // ★不能只按字体名/字形判断：英文客户端若 UI 字体自带汉字字形，
        //   两套面板会一中一英，并排出现时非常刺眼。
        if (!LpText.LocaleResolved && UiKit.FontLacksCjkGlyphs(font)) LpText.Override(false);
        bool hasChinese = LpText.Chinese;
        ui.Chinese = hasChinese;

        // ---- 居中面板 ----
        ui.Panel = CreateRect("HackPanel", canvas.transform);
        ui.Panel.anchorMin = new Vector2(0.5f, 0.5f);
        ui.Panel.anchorMax = new Vector2(0.5f, 0.5f);
        ui.Panel.pivot = new Vector2(0.5f, 0.5f);
        ui.Panel.anchoredPosition = new Vector2(0f, sh * 0.02f);
        ui.Panel.sizeDelta = new Vector2(panelW, panelH);
        Image panelImg = ui.Panel.gameObject.AddComponent<Image>();
        panelImg.color = PanelBgColor;
        panelImg.raycastTarget = false;
        Outline panelOutline = ui.Panel.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = PanelBorderColor;
        panelOutline.effectDistance = new Vector2(2f, -2f);

        // ---- 顶部信息行 ----
        ui.DoorInfoText = CreateText(ui.Panel, "HackDoorInfo", "",
            new Vector2(0f, -panelH * 0.045f), new Vector2(trackW, 22f), 13,
            TextAnchor.MiddleCenter, new Color(0.55f, 0.68f, 0.66f, 0.95f), font);
        ui.DoorInfoText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        ui.DoorInfoText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        ui.DoorInfoText.rectTransform.pivot = new Vector2(0.5f, 1f);

        // ---- 操作提示行（判定带正上方，常驻） ----
        ui.ActionHintText = CreateText(ui.Panel, "HackActionHint", "",
            new Vector2(0f, -panelH * 0.105f), new Vector2(trackW, 20f), 12,
            TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 0.55f, 0.95f), font);
        ui.ActionHintText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        ui.ActionHintText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        ui.ActionHintText.rectTransform.pivot = new Vector2(0.5f, 1f);

        // ---- 字符列阵区 ----
        float colsH = panelH * 0.30f;
        RectTransform colsRoot = CreateRect("HackColsRoot", ui.Panel);
        colsRoot.anchorMin = new Vector2(0.5f, 1f);
        colsRoot.anchorMax = new Vector2(0.5f, 1f);
        colsRoot.pivot = new Vector2(0.5f, 1f);
        colsRoot.anchoredPosition = new Vector2(0f, -panelH * 0.13f);
        colsRoot.sizeDelta = new Vector2(trackW, colsH);
        LayoutElement colsLayoutIgnore = colsRoot.gameObject.AddComponent<LayoutElement>();
        colsLayoutIgnore.ignoreLayout = true;

        // 中央判定带（横贯列阵，位于中心行）
        RectTransform band = CreateRect("HackBand", colsRoot);
        band.anchorMin = new Vector2(0f, 0.5f);
        band.anchorMax = new Vector2(1f, 0.5f);
        band.pivot = new Vector2(0.5f, 0.5f);
        band.anchoredPosition = Vector2.zero;
        band.sizeDelta = new Vector2(0f, colsH * 0.16f);
        ui.Band = band.gameObject.AddComponent<Image>();
        ui.Band.color = BandColor;
        ui.Band.raycastTarget = false;

        // 列容器：一次建满 MaxColumns 列，实际显示几列由 SetColumnCount 决定并重排。
        ui.TrackWidth = trackW;
        float colW = trackW / MaxColumns;
        for (int c = 0; c < MaxColumns; c++)
        {
            var col = new HackColumn { Cells = new List<Text>() };
            RectTransform container = CreateRect($"HackCol_{c:00}", colsRoot);
            container.anchorMin = new Vector2(0f, 0f);
            container.anchorMax = new Vector2(0f, 0f);
            container.pivot = new Vector2(0.5f, 0.5f);
            container.anchoredPosition = new Vector2(colW * (c + 0.5f), 0f);
            container.sizeDelta = new Vector2(colW * 0.6f, colsH);
            col.Container = container;

            float cellH = colsH / CellsPerColumn;
            for (int r = 0; r < CellsPerColumn; r++)
            {
                Text cell = CreateText(container, $"Cell_{r:00}", "0",
                    new Vector2(0f, (r - (CellsPerColumn - 1) * 0.5f) * cellH),
                    new Vector2(colW * 0.6f, cellH), Mathf.RoundToInt(cellH * 0.55f),
                    TextAnchor.MiddleCenter, CellColor, font);
                col.Cells.Add(cell);
            }
            ui.Columns.Add(col);
        }

        // ---- 右侧「选定密码」区 ----
        RectTransform pwd = CreateRect("HackPwd", ui.Panel);
        pwd.anchorMin = new Vector2(1f, 0.5f);
        pwd.anchorMax = new Vector2(1f, 0.5f);
        pwd.pivot = new Vector2(1f, 0.5f);
        pwd.anchoredPosition = new Vector2(-panelW * 0.07f, -panelH * 0.04f);
        float pwdSize = panelH * 0.30f;
        pwd.sizeDelta = new Vector2(pwdSize, pwdSize);
        Image pwdImg = pwd.gameObject.AddComponent<Image>();
        pwdImg.color = new Color(0.10f, 0.10f, 0.14f, 0.95f);
        pwdImg.raycastTarget = false;
        Outline pwdOutline = pwd.gameObject.AddComponent<Outline>();
        pwdOutline.effectColor = PanelBorderColor;
        pwdOutline.effectDistance = new Vector2(1.5f, -1.5f);

        CreateText(pwd, "PwdLabel", hasChinese ? "选定密码" : "PASSWORD",
            new Vector2(0f, pwdSize * 0.30f), new Vector2(pwdSize, 20f), 12,
            TextAnchor.MiddleCenter, LabelColor, font);
        // ★密码字必须留在框的上半部：右下的退出环与这个框是同一个矩形（见下方 HackRing），
        //   环心的「退出」二字就落在框心。曾经密码字排在框心（-0.05），与「退出」整行重叠，
        //   玩家看到的是「A 退出 _ _」。上移到 +0.06 后，框内自上而下是 标签 / 密码 / 退出，三层不再互压。
        ui.PasswordText = CreateText(pwd, "PwdChars", "",
            new Vector2(0f, pwdSize * 0.06f), new Vector2(pwdSize * 0.9f, pwdSize * 0.5f), 20,
            TextAnchor.MiddleCenter, HighlightColor, font);

        // ---- 底部进度区 ----
        RectTransform bottom = CreateRect("HackBottom", ui.Panel);
        bottom.anchorMin = new Vector2(0.5f, 0f);
        bottom.anchorMax = new Vector2(0.5f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.anchoredPosition = new Vector2(0f, panelH * 0.05f);
        bottom.sizeDelta = new Vector2(trackW, panelH * 0.22f);
        float bottomW = bottom.sizeDelta.x;

        ui.ProgressText = CreateText(bottom, "HackProgressText", ">>自动解锁同步进行中",
            new Vector2(0f, 0f), new Vector2(bottomW * 0.6f, 22f), 13,
            TextAnchor.LowerLeft, TextColor, font);
        ui.ProgressText.rectTransform.anchorMin = new Vector2(0f, 1f);
        ui.ProgressText.rectTransform.anchorMax = new Vector2(0f, 1f);
        ui.ProgressText.rectTransform.pivot = new Vector2(0f, 1f);

        ui.ErrorText = CreateText(bottom, "HackErrorText", "剩余错误: 0",
            new Vector2(-bottomW * 0.26f, 0f), new Vector2(bottomW * 0.25f, 22f), 12,
            TextAnchor.LowerRight, new Color(1f, 0.55f, 0.5f, 0.95f), font);
        ui.ErrorText.rectTransform.anchorMin = new Vector2(1f, 1f);
        ui.ErrorText.rectTransform.anchorMax = new Vector2(1f, 1f);
        ui.ErrorText.rectTransform.pivot = new Vector2(1f, 1f);

        ui.PercentText = CreateText(bottom, "HackPercentText", "[ 0%]",
            new Vector2(0f, -26f), new Vector2(120f, 22f), 14,
            TextAnchor.UpperRight, TextColor, font);
        ui.PercentText.rectTransform.anchorMin = new Vector2(1f, 0f);
        ui.PercentText.rectTransform.anchorMax = new Vector2(1f, 0f);
        ui.PercentText.rectTransform.pivot = new Vector2(1f, 0f);

        // 格纹进度条
        RectTransform progressBg = CreateRect("HackProgressBg", bottom);
        progressBg.anchorMin = new Vector2(0f, 0f);
        progressBg.anchorMax = new Vector2(0f, 0f);
        progressBg.pivot = new Vector2(0f, 0f);
        progressBg.anchoredPosition = new Vector2(0f, 24f);
        progressBg.sizeDelta = new Vector2(bottomW, 10f);
        Image progressBgImg = progressBg.gameObject.AddComponent<Image>();
        progressBgImg.color = ProgressBgColor;
        progressBgImg.raycastTarget = false;

        RectTransform fill = CreateRect("HackProgressFill", progressBg);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.zero;
        fill.pivot = new Vector2(0f, 0f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = progressBg.sizeDelta;
        ui.ProgressFill = fill.gameObject.AddComponent<Image>();
        ui.ProgressFill.color = ProgressFillColor;
        ui.ProgressFill.type = Image.Type.Filled;
        ui.ProgressFill.fillMethod = Image.FillMethod.Horizontal;
        ui.ProgressFill.fillAmount = 0f;
        ui.ProgressFill.raycastTarget = false;

        float slotW = progressBg.sizeDelta.x / ProgressSlotCount;
        for (int s = 1; s < ProgressSlotCount; s++)
        {
            RectTransform slot = CreateRect($"HackSlot_{s:00}", progressBg);
            slot.anchorMin = new Vector2((float)s / ProgressSlotCount, 0f);
            slot.anchorMax = new Vector2((float)s / ProgressSlotCount, 1f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = Vector2.zero;
            slot.sizeDelta = new Vector2(2f, 0f);
            Image slotImg = slot.gameObject.AddComponent<Image>();
            slotImg.color = ProgressSlotColor;
            slotImg.raycastTarget = false;
        }

        // ---- 右侧圆环取消按钮 ----
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        RectTransform ring = CreateRect("HackRing", ui.Panel);
        UiKit.Place(ring, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-panelW * 0.07f, -panelH * 0.04f), new Vector2(panelH * 0.30f, panelH * 0.30f));
        float ringSize = panelH * 0.30f;
        float succSize = ringSize * 1.42f;

        // ---- 成功进度环：套在退出环外面 ----
        // 先铺一圈暗底轨再叠填充环：否则进度为 0 时整圈不可见，玩家看不出这里之后会亮起来。
        Image succTrack = UiKit.Solid(ring, "HackSuccessTrack", UiKit.Center, UiKit.Center,
            Vector2.zero, new Vector2(succSize, succSize),
            new Color(RingColor.r, RingColor.g, RingColor.b, 0.16f));
        succTrack.sprite = CreateRingSprite(128, 8, new Color(RingColor.r, RingColor.g, RingColor.b, 0.16f));

        RectTransform succ = CreateRect("HackSuccessRing", ring);
        ui.SuccessRing = UiKit.Solid(succ, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(succSize, succSize), RingColor);
        ui.SuccessRing.sprite = CreateRingSprite(128, 8, RingColor);
        // 径向填充而不是逐帧重建贴图：纹路连续无接缝，也不必每帧 new 一张 Texture2D。
        UiKit.MakeRadialFill(ui.SuccessRing, Image.Origin360.Top, true);

        Image ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = CreateRingSprite(128, 7, RingColor);
        ringImg.raycastTarget = true;
        ui.UnlockButton = ring.gameObject.AddComponent<Button>();
        ui.UnlockButton.targetGraphic = ringImg;

        RectTransform ringGlow = CreateRect("HackRingGlow", ring);
        ringGlow.anchorMin = new Vector2(0.5f, 0.5f);
        ringGlow.anchorMax = new Vector2(0.5f, 0.5f);
        ringGlow.pivot = new Vector2(0.5f, 0.5f);
        ringGlow.anchoredPosition = Vector2.zero;
        ringGlow.sizeDelta = new Vector2(ringSize * 1.18f, ringSize * 1.18f);
        Image ringGlowImg = ringGlow.gameObject.AddComponent<Image>();
        ringGlowImg.sprite = CreateRingSprite(128, 3, new Color(RingColor.r, RingColor.g, RingColor.b, 0.22f));
        ringGlowImg.raycastTarget = false;

        // ★必须显式给锚点：运行时新建的 RectTransform 默认锚在左下角，
        //   不给的话「退出」二字会贴在环的左下边缘而不是环心。
        // 标签排在环内下缘（-0.34 半径处），不再占环心 —— 环心与密码框同心，占住就会压到密码字。
        // 字号同时从 22 收到 16：这个位置的可视弦宽只有约 0.58×直径，22 号两字会把环撑满。
        var ringLabelPos = new Vector2(0f, -ringSize * 0.34f);
        Text ringLabel = CreateText(ring, "HackRingLabel", hasChinese ? "退出" : "EXIT",
            ringLabelPos, new Vector2(ringSize, 40f), 16,
            TextAnchor.MiddleCenter, RingColor, font);
        UiKit.Place(ringLabel.rectTransform, UiKit.Center, UiKit.Center, ringLabelPos, new Vector2(ringSize, 40f));

        ui.SuccessText = CreateText(ring, "HackSuccessText", "",
            Vector2.zero, new Vector2(succSize * 1.8f, 20f), 14,
            TextAnchor.UpperCenter, RingColor, font);
        UiKit.Place(ui.SuccessText.rectTransform, UiKit.Center, new Vector2(0.5f, 1f),
            new Vector2(0f, -(succSize * 0.5f + 22f)), new Vector2(succSize * 1.8f, 20f));

        // ---- 操作提示 ----
        ui.HintText = CreateText(ui.Panel, "HackHint", hasChinese ? "鼠标左键锁定  |  Esc / 右键退出" : "LMB to lock  |  Esc / RMB to exit",
            new Vector2(0f, panelH * 0.025f), new Vector2(trackW, 20f), 12,
            TextAnchor.UpperCenter, new Color(0.55f, 0.58f, 0.6f, 0.85f), font);
        ui.HintText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        ui.HintText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        ui.HintText.rectTransform.pivot = new Vector2(0.5f, 0f);

        return ui;
    }

    /// <summary>
    /// 按密码位数显示列数（3~MaxColumns），隐藏多余的列并重新均分间距。
    /// </summary>
    public static void SetColumnCount(HackUI ui, int count)
    {
        if (ui.Columns == null || ui.Columns.Count == 0) return;
        int n = Mathf.Clamp(count, 1, ui.Columns.Count);
        float track = ui.TrackWidth > 0f ? ui.TrackWidth : 1f;
        float colW = track / n;
        for (int c = 0; c < ui.Columns.Count; c++)
        {
            var col = ui.Columns[c];
            if (col.Container == null) continue;
            bool visible = c < n;
            col.Container.gameObject.SetActive(visible);
            if (!visible) continue;
            // ★必须重新均分间距，不能只隐藏多余的列：列容器是按 MaxColumns 列固定摆好的，
            //   只隐藏会让 3 位密码的三列全挤在左边、右侧空掉半个面板。
            float x = colW * (c + 0.5f);
            float w = colW * 0.6f;
            col.Container.anchoredPosition = new Vector2(x, col.Container.anchoredPosition.y);
            col.Container.sizeDelta = new Vector2(w, col.Container.sizeDelta.y);
            for (int r = 0; r < col.Cells.Count; r++)
            {
                if (col.Cells[r] == null) continue;
                col.Cells[r].rectTransform.sizeDelta = new Vector2(w, col.Cells[r].rectTransform.sizeDelta.y);
            }
        }
    }

    /// <summary>
    /// 刷新列字符与高亮。
    /// ★只有 activeColumn 这一列挂绿色高亮（＝当前可操作的那一列），其余未锁列只是灰字在滚。
    ///   旧口径是每列各挂一个绿高亮，五团绿并排，玩家反而不知道该盯哪一处。
    /// </summary>
    /// <param name="activeColumn">当前可操作列索引；-1 表示没有（已全部锁定）。</param>
    public static void UpdateColumns(HackUI ui, List<string> chars, List<float> positions, List<bool> locked, int activeColumn)
    {
        for (int c = 0; c < ui.Columns.Count; c++)
        {
            var col = ui.Columns[c];
            if (col.Container == null || !col.Container.gameObject.activeSelf) continue;
            // ★positions 也要一起防越界：列池是按 MaxColumns 建满的，而位置表只有实际列数那么长。
            //   下面那句 activeSelf 判断能挡住大多数情形，但「面板刚 Show 出来、SetColumnCount 还没跑」
            //   的那一帧窗口里，多出来的列仍然是激活的 —— 少这一次判断就是一次下标越界。
            if (positions == null || c >= positions.Count) continue;
            bool isLocked = locked != null && c < locked.Count && locked[c];
            // 非当前列把 highlight 置 -1：下面 r == highlight 恒不成立，自然没有绿格。
            int highlight = !isLocked && c == activeColumn
                ? Mathf.Clamp(Mathf.RoundToInt(positions[c] * (CellsPerColumn - 1)), 0, CellsPerColumn - 1)
                : -1;
            for (int r = 0; r < col.Cells.Count; r++)
            {
                Text cell = col.Cells[r];
                if (cell == null) continue;
                if (chars != null && c < chars.Count && r < chars[c].Length)
                    cell.text = chars[c][r].ToString();
                cell.color = isLocked ? LockedColor : (r == highlight ? HighlightColor : CellColor);
            }
        }
    }

    /// <summary>
    /// 判定带颜色（CD 时变红）。
    /// </summary>
    public static void SetBandColor(HackUI ui, bool inCd)
    {
        if (ui.Band != null)
            ui.Band.color = inCd ? BandCdColor : BandColor;
    }

    /// <summary>
    /// 刷新右侧成功环。locked/total 决定填充比例；done=true 时点亮整环并显示「破解成功」。
    /// </summary>
    public static void SetSuccess(HackUI ui, int locked, int total, bool done)
    {
        float frac = total <= 0 ? 0f : Mathf.Clamp01(locked / (float)total);
        if (ui.SuccessRing != null)
            ui.SuccessRing.fillAmount = done ? 1f : frac;
        if (ui.SuccessText != null)
            ui.SuccessText.text = done
                ? (ui.Chinese ? "破解成功" : "ACCESS GRANTED")
                : (ui.Chinese ? "破解中 " + locked + "/" + total : "HACKING " + locked + "/" + total);
    }

    // ---- 工具方法 ----

    private static RectTransform CreateRect(string name, Transform parent)
        => UiKit.CreateRect(name, parent);

    /// <summary>
    /// 建文本。
    /// ★字号**不乘** UiScale。撬锁面板的绝对像素里只有尺寸值放大了（30.8 / 37.4 / 28.6 这类），
    /// 字号仍是原始整数（20 / 16 / 17 / 22）—— 那个面板的字号本来就没跟着放大。
    /// 这里若单边乘 1.1，两套面板并排时骇入的字会明显大一圈。
    /// 要放大就两边一起放大并实机确认，绝不能只动一边。
    /// </summary>
    private static Text CreateText(Transform parent, string name, string content,
        Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color, Font font)
        => UiKit.CreateText(parent, name, content, position, size, fontSize, alignment, color, font);

    private static Font ResolveFont(Transform parent)
        => UiKit.ResolveFont(parent);

    private static Sprite CreateRingSprite(int size, float thickness, Color color)
        => UiKit.CreateRingSprite(size, thickness, color);
}
