// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LockpickStandalone.Actions;
using LockpickStandalone.Interaction;

namespace LockpickStandalone.Ui;

/// <summary>
/// 《三角洲行动》风格的开锁破解面板构建器。只负责视觉，不参与任何判定逻辑。
/// 第二批「三角洲化」增量：顶部槽位条 [01]…[09]、面板四角刻线、圆环刻度盘 + 常亮进度弧、
/// 铂金锁定区 / 金色完美段、底部 40 格分段进度槽（逐格点亮）、扫描轨道暗红底。
/// 判定逻辑不变（单击锁定白色区间），本类只负责构建与刷新视觉。
/// 面板内所有尺寸一律按当前屏幕宽高的比例推算，不写死像素值，因此任意分辨率下版式保持一致。
/// </summary>
public static class DeltaLockUIBuilder
{
    // ---- 配色（对照三角洲：深蓝灰面板 + 青灰边框 + 荧光绿高亮） ----
    private static readonly Color PanelBgColor = new(0.07f, 0.11f, 0.14f, 0.93f);
    private static readonly Color PanelBorderColor = new(0.35f, 0.65f, 0.60f, 0.55f);
    private static readonly Color CornerColor = new(0.40f, 0.72f, 0.66f, 0.75f);     // 四角刻线
    private static readonly Color TrackBgColor = new(0.16f, 0.07f, 0.09f, 0.95f);    // 扫描轨道：暗红底
    private static readonly Color ZoneColor = new(0.87f, 0.89f, 0.92f, 0.9f);        // 锁定区：铂金白
    private static readonly Color PerfectZoneColor = new(1f, 0.78f, 0.25f, 0.95f);   // 完美区：金
    private static readonly Color ZoneHitColor = new(0.2f, 1f, 0.47f, 1f);           // 锁定区命中：荧光绿
    private static readonly Color CursorColor = new(0.2f, 1f, 0.47f, 1f);            // 游标：荧光绿
    private static readonly Color CdCursorColor = new(0.95f, 0.25f, 0.2f, 1f);       // CD 游标：红
    private static readonly Color TeethBarColor = new(0.23f, 0.23f, 0.36f, 1f);      // 锁齿：暗蓝紫
    private static readonly Color TeethZoneColor = new(0.8f, 0.8f, 0.85f, 1f);       // 锁齿在锁定区上：白
    private static readonly Color TeethHitColor = new(0.2f, 1f, 0.47f, 1f);          // 锁齿命中：荧光绿
    private static readonly Color ProgressBgColor = new(0.09f, 0.12f, 0.11f, 0.9f);
    private static readonly Color ProgressFillColor = new(0.62f, 0.72f, 0.68f, 0.45f);// 连续底衬：淡
    private static readonly Color SlotEmptyColor = new(0.55f, 0.60f, 0.58f, 0.60f);  // 格槽未点亮
    private static readonly Color SlotFullColor = new(0.90f, 0.94f, 0.92f, 1f);      // 格槽点亮：铂金
    private static readonly Color SlotAutoColor = new(0.95f, 0.72f, 0.25f, 1f);      // 格槽自动解锁：琥珀
    private static readonly Color SlotBarEmptyColor = new(0.16f, 0.21f, 0.24f, 0.95f);
    private static readonly Color SlotBarLitColor = new(0.32f, 0.72f, 0.66f, 0.95f);
    private static readonly Color RingColor = new(0f, 0.85f, 0.45f, 1f);
    private static readonly Color TextColor = new(0.75f, 0.78f, 0.8f, 1f);
    private static readonly Color LabelColor = new(0.45f, 0.52f, 0.52f, 0.85f);

    private const int TeethColumnCount = 10;   // 对照三角洲：10 槽位
    private const int TeethBarsPerColumn = 8;
    private const int ProgressSlotCount = 40;  // 格纹进度条段数
    private const int SlotBarCount = 10;       // 顶部槽位条段数

    public struct TeethColumn
    {
        public RectTransform Container;
        public List<Image> Bars;
    }

    public struct DeltaUI
    {
        public RectTransform Panel;
        public RectTransform ScanTrack;
        public RectTransform ScanCursor;
        public Image CursorGlow;
        public List<Image> ScanZones;
        public List<TeethColumn> Teeth;
        public Image ProgressFill;
        public Text ProgressText;
        public Text PercentText;
        public Text ErrorText;
        public Text HintText;
        public Text DoorInfoText;
        public Image CdFill;
        // 右侧圆环：纯状态显示（不可点击）。开锁中常亮 + 旋转弧 + 呼吸光 + 刻度盘 + 进度弧
        public RectTransform UnlockRingRect;
        public Image RingImg;
        public Image RingGlowImg;
        public RectTransform RingSpin;
        public Image RingTicks;
        public Image RingArc;
        public Text UnlockLabel;
        // 第二批：顶部槽位条 + 底部逐格进度槽
        public List<Image> SlotBarCells;
        public List<Image> ProgressCells;
        // 第三批：面板描边（失误红闪载体）+ 左下线框图（接线后随进度变化）+ 环内命中统计
        public Outline PanelOutline;
        public RectTransform DiagramRect;
        public Image DiagramCore;
        public Image DiagramDot1;
        public Image DiagramDot2;
        public Text HitStatText;
    }

    /// <summary>
    /// 在资源自带的 Canvas 之下搭建整块破解面板，并向调用方交回各控件的引用，供后续逐帧刷新。
    /// </summary>
    public static DeltaUI Build(Canvas canvas)
    {
        var ui = new DeltaUI
        {
            ScanZones = new List<Image>(),
            Teeth = new List<TeethColumn>(),
            SlotBarCells = new List<Image>(),
            ProgressCells = new List<Image>()
        };

        // ---- A1: 夺回缩放控制权 ----
        // 游戏界面 Canvas 的缩放器配置为 800×600 参考分辨率并按宽度匹配，实际运行时的缩放倍数约为 3.2。
        // 把自建 UI 挂到这个 Canvas 之前必须先把缩放器改回原样，否则所有尺寸都会被放大数倍而铺满整屏。
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
        // 缩放倍数归一之后，屏幕宽高即真实像素值；后续所有尺寸与下限阈值都按屏幕比例给出。
        // 2026-10-08：面板整体放大 1.1 倍（用户实机选定；1.2 偏大、原版偏小，1.1 取中）。
        // 在 sw/sh 源头乘系数，面板、轨道、槽条、圆环与所有按比例派生的区块偏移
        // 自动等比放大；下方另有一批绝对像素值（字号、线宽、留白）需要同步放大，
        // 否则面板变大但文字不变、线条相对变细。
        const float UI_SCALE = 1.1f;
        float sw = Screen.width * UI_SCALE;
        float sh = Screen.height * UI_SCALE;
        float panelW = Mathf.Max(sw * 0.46f, sw * 0.30f);  // 目标 46%，最低 30%
        float panelH = Mathf.Max(sh * 0.54f, sh * 0.35f);  // 目标 54%，最低 35%
        float trackW = panelW * 0.84f;
        float trackH = Mathf.Max(sh * 0.016f, sh * 0.014f); // 目标 1.6% 屏高，最低 1.4%

        // ★canvas 为空时下面每一行都会 NRE。被 GameHost 的 try 兜住只会记一行 Error，
        //   但那一局的撬锁整体不可用，且现场看不出是「Canvas 没就绪」还是别的。
        if (canvas == null)
        {
            LpLog.Error("[Lockpick] UI Canvas 未就绪，本次开锁面板构建跳过");
            return default;
        }

        Font font = ResolveFont(canvas.transform);
        // 语言优先看游戏界面语言（LpText 内部解析 LocalizationManager.Culture）；
        // 只有在读不到可信语言、且字体确实没有中文字形时才退回英文，避免出现方块。
        if (!LpText.LocaleResolved && FontLacksCjkGlyphs(font)) LpText.Override(false);

        // ---- 居中面板（无全屏遮罩，游戏世界保持可见） ----
        ui.Panel = CreateRect("DeltaPanel", canvas.transform);
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
        panelOutline.effectDistance = new Vector2(2.2f, -2.2f);
        ui.PanelOutline = panelOutline;   // 第三批：失误时整块面板红闪

        // ---- 四角刻线（三角洲面板标志性的切角括号） ----
        float cornerLen = Mathf.Max(11f, panelW * 0.022f);
        DrawCorner(ui.Panel, "CornerTL", -1f, 1f, cornerLen, 2.2f);
        DrawCorner(ui.Panel, "CornerTR", 1f, 1f, cornerLen, 2.2f);
        DrawCorner(ui.Panel, "CornerBL", -1f, -1f, cornerLen, 2.2f);
        DrawCorner(ui.Panel, "CornerBR", 1f, -1f, cornerLen, 2.2f);

        // ---- 顶部信息行（门等级 + 钥匙名，替代被隐藏的原版文本） ----
        // ★位置与高度都收过：20 号字居中在 30.8 高的框里，字形下缘会压到槽位条上沿约 3px，
        //   实机看是「门等级」下沿被槽条切掉一条。上移到 0.028P、框高压到 24 后留出 5px 空隙。
        ui.DoorInfoText = CreateText(ui.Panel, "DoorInfoText", "",
            new Vector2(0f, -panelH * 0.028f), new Vector2(trackW, 24f), 20,
            TextAnchor.MiddleCenter, new Color(0.55f, 0.68f, 0.66f, 0.95f), font);
        ui.DoorInfoText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        ui.DoorInfoText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        ui.DoorInfoText.rectTransform.pivot = new Vector2(0.5f, 1f);

        // ---- 顶部槽位条（对照三角洲 [05][06][08] 槽位刻度带） ----
        RectTransform slotBar = CreateRect("SlotBar", ui.Panel);
        slotBar.anchorMin = new Vector2(0.5f, 1f);
        slotBar.anchorMax = new Vector2(0.5f, 1f);
        slotBar.pivot = new Vector2(0.5f, 1f);
        slotBar.anchoredPosition = new Vector2(0f, -panelH * 0.092f);
        float slotBarH = Mathf.Max(sh * 0.016f, 13.2f);
        slotBar.sizeDelta = new Vector2(trackW, slotBarH);
        for (int s = 0; s < SlotBarCount; s++)
        {
            RectTransform cell = CreateRect($"SlotCell_{s:00}", slotBar);
            cell.anchorMin = new Vector2((float)s / SlotBarCount, 0f);
            cell.anchorMax = new Vector2((float)(s + 1) / SlotBarCount, 1f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.anchoredPosition = Vector2.zero;
            cell.sizeDelta = new Vector2(-3.3f, 0f);
            Image cellImg = cell.gameObject.AddComponent<Image>();
            cellImg.color = SlotBarEmptyColor;
            cellImg.raycastTarget = false;
            ui.SlotBarCells.Add(cellImg);

            // 槽位内部细刻线（三角洲槽条里的分隔感）
            RectTransform notch = CreateRect($"SlotNotch_{s:00}", cell);
            notch.anchorMin = new Vector2(0.5f, 0f);
            notch.anchorMax = new Vector2(0.5f, 1f);
            notch.pivot = new Vector2(0.5f, 0.5f);
            notch.anchoredPosition = Vector2.zero;
            notch.sizeDelta = new Vector2(1.2f, -4.4f);
            Image notchImg = notch.gameObject.AddComponent<Image>();
            notchImg.color = new Color(0.05f, 0.09f, 0.11f, 0.8f);
            notchImg.raycastTarget = false;

            // 稀疏编号：每 4 格一个，对照三角洲 [01] [05] [09]
            if (s % 4 == 0)
            {
                Text id = CreateText(cell, $"SlotId_{s:00}", $"[{s + 1:00}]",
                    Vector2.zero, new Vector2(37.4f, 18.7f), 16,
                    TextAnchor.UpperCenter, LabelColor, font);
                id.rectTransform.anchorMin = new Vector2(0.5f, 0f);
                id.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                id.rectTransform.pivot = new Vector2(0.5f, 1f);
            }
        }

        // ---- 顶部扫描轨道 ----
        // 轨道本体：贴在面板顶部下方固定偏移处，横向铺满内容宽度
        ui.ScanTrack = Place(CreateRect("ScanTrack", ui.Panel),
            TopCenter, TopCenter, new Vector2(0f, -panelH * 0.168f), new Vector2(trackW, trackH));
        var trackBg = ui.ScanTrack.gameObject.AddComponent<Image>();
        trackBg.color = TrackBgColor;
        trackBg.raycastTarget = false;

        // 显式声明不受布局系统管辖：轨道上的游标与刻度位置由本类独占计算，
        // 任何祖先布局组件一旦参与重排就会把它们推歪
        ui.ScanTrack.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // 刻度尺：共 11 根均分刻线，最外侧两根加长加粗作为端点标识
        const int TickCount = 10;
        for (int i = 0; i <= TickCount; i++)
        {
            bool isEnd = i == 0 || i == TickCount;
            Solid(CreateRect($"Tick_{i:00}", ui.ScanTrack),
                new Vector2(i / (float)TickCount, 0.5f), Center, Vector2.zero,
                new Vector2(isEnd ? 2.2f : 1.2f, trackH * (isEnd ? 1.7f : 0.9f)),
                isEnd ? TextColor : LabelColor);
        }

        // 游标：荧光绿竖条本体（高度数倍于轨道，保证在高倍缩放下仍醒目）
        ui.ScanCursor = Place(CreateRect("ScanCursor", ui.ScanTrack),
            Center, Center, Vector2.zero, new Vector2(9.9f, trackH * 2.6f));
        var cursorImg = ui.ScanCursor.gameObject.AddComponent<Image>();
        cursorImg.color = CursorColor;
        cursorImg.raycastTarget = false;

        // 游标辉光：包在本体外面一圈更大的半透明同色块，视觉上制造柔边
        ui.CursorGlow = Solid(CreateRect("CursorGlow", ui.ScanCursor),
            Center, Center, Vector2.zero, new Vector2(22f, trackH * 3.2f),
            new Color(CursorColor.r, CursorColor.g, CursorColor.b, 0.25f));

        // CD 底条：横跨轨道上沿的一条暗红细线
        Solid(CreateRect("CdBg", ui.ScanTrack),
            new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 3.3f), new Vector2(0f, 3.3f),
            new Color(0.3f, 0.1f, 0.1f, 0.6f));

        // CD 进度条：挂在轨道而非 CdBg 上（CdBg 的横向拉伸会干扰宽度计算）。
        // 从左边缘向右生长，调用方通过改 anchorMax.x 推进倒计时，见 DeltaLockGame。
        RectTransform cdFillRt = CreateRect("CdFill", ui.ScanTrack);
        cdFillRt.anchorMin = Vector2.zero;
        cdFillRt.anchorMax = Vector2.zero;          // 运行时由 Update 推到 (剩余比例, 1)
        cdFillRt.pivot = new Vector2(0f, 0.5f);
        cdFillRt.anchoredPosition = Vector2.zero;
        cdFillRt.sizeDelta = Vector2.zero;
        ui.CdFill = cdFillRt.gameObject.AddComponent<Image>();
        ui.CdFill.color = new Color(0.95f, 0.3f, 0.22f, 0.95f);
        ui.CdFill.raycastTarget = false;

        // ---- 锁齿列阵（轨道正下方，10 列，对照三角洲槽位） ----
        // 每列等分轨道宽度；列内横线的可用高度要扣掉底部留白与顶部编号行
        const float ColBottomPad = 13.2f;
        const float ColTopReserve = 24.2f;
        const float BarThickness = 3.3f;

        float colW = trackW / TeethColumnCount;
        float teethH = panelH * 0.26f;
        float barAreaH = teethH - ColBottomPad - ColTopReserve;
        // 横线之间的空隙由剩余高度均摊，保证整列上下顶到边界
        float barGap = (barAreaH - TeethBarsPerColumn * BarThickness) / (TeethBarsPerColumn - 1);

        // 列阵整体挂在轨道下方：轨道高度的一半 + 20px 视觉间隙
        RectTransform teethRoot = Place(CreateRect("TeethRoot", ui.Panel),
            TopCenter, TopCenter,
            new Vector2(0f, -panelH * 0.168f - trackH / 2f - 22f),
            new Vector2(trackW, teethH));

        for (int i = 0; i < TeethColumnCount; i++)
        {
            var col = new TeethColumn { Bars = new List<Image>() };

            // 列容器：横向只占列宽的一半左右，形成列与列之间的呼吸缝
            RectTransform container = Place(CreateRect($"TeethCol_{i:00}", teethRoot),
                Vector2.zero, new Vector2(0.5f, 0f),
                new Vector2(colW * (i + 0.5f), ColBottomPad),
                new Vector2(colW * 0.52f, teethH - ColBottomPad));
            col.Container = container;

            // 横线自容器顶缘向下依次排列：每根的偏移 = 自身半高 + 已累积的所有空隙
            for (int b = 0; b < TeethBarsPerColumn; b++)
            {
                // ★必须从 ColTopReserve 之下起算：barAreaH 本来就把这 24.2 扣掉了（8 根线 + 7 个空隙
                //   正好铺满剩余高度），旧式子却从列顶 4.4 就开始，横线整体上移 24.2，把顶部编号行挤没了。
                float drop = ColTopReserve + (b + 0.5f) * BarThickness + b * barGap;
                col.Bars.Add(Solid(CreateRect($"Bar_{b}", container),
                    TopCenter, TopCenter, new Vector2(0f, -drop),
                    new Vector2(container.sizeDelta.x, BarThickness), TeethBarColor));
            }

            // 列编号只标第 1/5/9 列，稀疏标注避免整排数字糊成一片
            if (i % 4 == 0)
            {
                // ★CreateText 里那个 -19.8 从来没生效过：紧随其后的 Place 用 Center + zero 把偏移归零，
                //   编号于是落在列正中央、压在锁齿线上。这里改成贴列顶，正好占住 ColTopReserve 那 24.2 的编号行。
                Text label = CreateText(container, $"Label_{i:00}", $"[{i + 1:00}]",
                    new Vector2(0f, -19.8f), new Vector2(colW, 24.2f), 17,
                    TextAnchor.UpperCenter, LabelColor, font);
                Place(label.rectTransform, TopCenter, TopCenter, Vector2.zero, label.rectTransform.sizeDelta);
            }

            ui.Teeth.Add(col);
        }

        // ---- 左侧线框装饰（简化还原三角洲保险柜结构示意） ----
        RectTransform diagram = CreateRect("Diagram", ui.Panel);
        diagram.anchorMin = new Vector2(0f, 0.5f);
        diagram.anchorMax = new Vector2(0f, 0.5f);
        diagram.pivot = new Vector2(0f, 0.5f);
        diagram.anchoredPosition = new Vector2(panelW * 0.09f, -panelH * 0.04f);
        diagram.sizeDelta = new Vector2(panelW * 0.30f, panelH * 0.24f);

        Color diagramColor = new(0.45f, 0.58f, 0.56f, 0.35f);
        // 四条边拼出矩形外框
        DrawDiagramLine(diagram, "D_Top", new Vector2(0f, 0f), new Vector2(diagram.sizeDelta.x, 1.65f), diagramColor);
        DrawDiagramLine(diagram, "D_Bottom", new Vector2(0f, -1.65f), new Vector2(diagram.sizeDelta.x, 1.65f), diagramColor);
        DrawDiagramLine(diagram, "D_Left", new Vector2(0f, 0f), new Vector2(1.65f, diagram.sizeDelta.y), diagramColor);
        DrawDiagramLine(diagram, "D_Right", new Vector2(-1.65f, 0f), new Vector2(1.65f, diagram.sizeDelta.y), diagramColor);
        // 内部方块与圆点，用于抽象表示锁芯结构
        ui.DiagramRect = diagram;
        ui.DiagramCore = DrawDiagramLine(diagram, "D_Core", new Vector2(diagram.sizeDelta.x * 0.32f, -diagram.sizeDelta.y * 0.18f),
            new Vector2(diagram.sizeDelta.x * 0.36f, diagram.sizeDelta.y * 0.36f), diagramColor);
        ui.DiagramDot1 = DrawDiagramLine(diagram, "D_Dot1", new Vector2(diagram.sizeDelta.x * 0.42f, 0f), new Vector2(6.6f, 6.6f), diagramColor);
        ui.DiagramDot2 = DrawDiagramLine(diagram, "D_Dot2", new Vector2(diagram.sizeDelta.x * 0.55f, -diagram.sizeDelta.y * 0.06f), new Vector2(5.5f, 5.5f), diagramColor);

        // ---- 右侧状态圆环（对照三角洲“破解中”指示：纯状态显示，不可点击） ----
        // 三角洲里破解开始后右侧就是转圈进度指示，不再是动作按钮；退出一律 Esc / 右键。
        RectTransform ring = CreateRect("UnlockRing", ui.Panel);
        ring.anchorMin = new Vector2(1f, 0.5f);
        ring.anchorMax = new Vector2(1f, 0.5f);
        ring.pivot = new Vector2(1f, 0.5f);
        // 2026-10-02：环原直径 0.30P 会压住锁齿横线区（0.19P~0.44P），而面板中段已被锁齿占满、
        // 下方空带只有 0.47P~0.86P（0.39P 高）⇒ 必须「缩小 + 下移」同时做，否则缩不动。
        // 2026-10-22：仍按 10-02 那次的「缩小 + 下移」思路，但 0.15P 让环底下缘压到「剩余容错」那行上沿
        //   约 13px（环底 0.76P vs 底部块顶 0.73P）。收到 0.11P：环顶距锁齿底仍有 0.03P，环底留出 0.01P 空隙。
        ring.anchoredPosition = new Vector2(-panelW * 0.07f, -panelH * 0.11f);
        float ringSize = panelH * 0.22f;
        ring.sizeDelta = new Vector2(ringSize, ringSize);
        ui.UnlockRingRect = ring;
        Image ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = CreateRingSprite(128, 7, RingColor);
        ringImg.raycastTarget = false;
        ui.RingImg = ringImg;

        // 刻度盘：环外一圈 60 根短刻线（三角洲转盘外圈）
        RectTransform ringTicks = CreateRect("RingTicks", ring);
        ringTicks.anchorMin = new Vector2(0.5f, 0.5f);
        ringTicks.anchorMax = new Vector2(0.5f, 0.5f);
        ringTicks.pivot = new Vector2(0.5f, 0.5f);
        ringTicks.anchoredPosition = Vector2.zero;
        ringTicks.sizeDelta = new Vector2(ringSize * 1.10f, ringSize * 1.10f);
        Image ringTicksImg = ringTicks.gameObject.AddComponent<Image>();
        // 半径比例 0.93 ⇒ 刻线落在基准环（外缘 ≈0.492 环径）之外、光晕（≈0.581）之内
        ringTicksImg.sprite = CreateTicksSprite(256, 60, 0.93f, 6f, new Color(RingColor.r, RingColor.g, RingColor.b, 0.45f));
        ringTicksImg.raycastTarget = false;
        ui.RingTicks = ringTicksImg;

        // 圆环外发光（更大更淡的一圈，呼吸动画载体）
        RectTransform ringGlow = CreateRect("RingGlow", ring);
        ringGlow.anchorMin = new Vector2(0.5f, 0.5f);
        ringGlow.anchorMax = new Vector2(0.5f, 0.5f);
        ringGlow.pivot = new Vector2(0.5f, 0.5f);
        ringGlow.anchoredPosition = Vector2.zero;
        ringGlow.sizeDelta = new Vector2(ringSize * 1.18f, ringSize * 1.18f);
        Image ringGlowImg = ringGlow.gameObject.AddComponent<Image>();
        ringGlowImg.sprite = CreateRingSprite(128, 3, new Color(RingColor.r, RingColor.g, RingColor.b, 0.22f));
        ringGlowImg.raycastTarget = false;
        ui.RingGlowImg = ringGlowImg;

        // 常亮进度弧：环内一圈细环，按解锁进度径向填充（Radial360，起点正上方，顺时针）
        RectTransform ringArc = CreateRect("RingArc", ring);
        ringArc.anchorMin = new Vector2(0.5f, 0.5f);
        ringArc.anchorMax = new Vector2(0.5f, 0.5f);
        ringArc.pivot = new Vector2(0.5f, 0.5f);
        ringArc.anchoredPosition = Vector2.zero;
        ringArc.sizeDelta = new Vector2(ringSize * 0.80f, ringSize * 0.80f);
        Image ringArcImg = ringArc.gameObject.AddComponent<Image>();
        ringArcImg.sprite = CreateRingSprite(128, 6, new Color(0.85f, 1f, 0.92f, 0.95f));
        ringArcImg.type = Image.Type.Filled;
        ringArcImg.fillMethod = Image.FillMethod.Radial360;
        ringArcImg.fillOrigin = (int)Image.Origin360.Top;
        ringArcImg.fillClockwise = true;
        ringArcImg.fillAmount = 0f;
        ringArcImg.raycastTarget = false;
        ui.RingArc = ringArcImg;

        // 旋转弧：缺口圆环，由 DeltaLockGame 每帧旋转 + 呼吸（三角洲味来源）
        RectTransform ringSpin = CreateRect("RingSpin", ring);
        ringSpin.anchorMin = new Vector2(0.5f, 0.5f);
        ringSpin.anchorMax = new Vector2(0.5f, 0.5f);
        ringSpin.pivot = new Vector2(0.5f, 0.5f);
        ringSpin.anchoredPosition = Vector2.zero;
        ringSpin.sizeDelta = new Vector2(ringSize * 0.92f, ringSize * 0.92f);
        Image ringSpinImg = ringSpin.gameObject.AddComponent<Image>();
        ringSpinImg.sprite = CreateArcSprite(128, 9, RingColor, 300f);
        ringSpinImg.raycastTarget = false;
        ui.RingSpin = ringSpin;

        // 环内主标签：命中统计搬走后环心只剩它一个，回正居中
        ui.UnlockLabel = CreateText(ring, "UnlockLabel", LpText.Working,
            Vector2.zero, new Vector2(ringSize, 33f), 25,
            TextAnchor.MiddleCenter, RingColor, font);

        // 命中统计改挂底行（见下方 BottomBar）：
        // 17 号「命中 N/M · 完美 K」实测约 127px 宽，而环在 -0.16 半径处的内弦只有约 93px，
        // 原来放环心时文字两端会横穿环壁。底行左侧是进度行、右侧是容错，中间正好空出约 200px。

        // ---- 底部进度区 ----
        RectTransform bottom = CreateRect("BottomBar", ui.Panel);
        bottom.anchorMin = new Vector2(0.5f, 0f);
        bottom.anchorMax = new Vector2(0.5f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.anchoredPosition = new Vector2(0f, panelH * 0.05f);
        bottom.sizeDelta = new Vector2(trackW, panelH * 0.22f);
        float bottomW = bottom.sizeDelta.x;

        ui.ProgressText = CreateText(bottom, "ProgressText", LpText.InitialProgress,
            new Vector2(0f, 0f), new Vector2(bottomW * 0.6f, 30.8f), 20,
            TextAnchor.LowerLeft, TextColor, font);
        ui.ProgressText.rectTransform.anchorMin = new Vector2(0f, 1f);
        ui.ProgressText.rectTransform.anchorMax = new Vector2(0f, 1f);
        ui.ProgressText.rectTransform.pivot = new Vector2(0f, 1f);

        ui.ErrorText = CreateText(bottom, "ErrorText", LpText.ErrorsLeft(0),
            new Vector2(-bottomW * 0.26f, 0f), new Vector2(bottomW * 0.25f, 28.6f), 18,
            TextAnchor.LowerRight, new Color(1f, 0.55f, 0.5f, 0.95f), font);
        ui.ErrorText.rectTransform.anchorMin = new Vector2(1f, 1f);
        ui.ErrorText.rectTransform.anchorMax = new Vector2(1f, 1f);
        ui.ErrorText.rectTransform.pivot = new Vector2(1f, 1f);

        // 命中统计：底行正中（左侧进度行约占 180px、右侧容错约占 136px，中间还有约 200px 空位）。
        // 与 ProgressText 同一基线（同为块顶锚点 + 30.8 高 + 贴底对齐），三栏看起来是一行。
        ui.HitStatText = CreateText(bottom, "HitStatText", "",
            Vector2.zero, new Vector2(bottomW * 0.35f, 30.8f), 17,
            TextAnchor.LowerCenter, new Color(0.62f, 0.72f, 0.68f, 0.9f), font);
        ui.HitStatText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        ui.HitStatText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        ui.HitStatText.rectTransform.pivot = new Vector2(0.5f, 1f);

        ui.PercentText = CreateText(bottom, "PercentText", "[ 0%]",
            new Vector2(0f, -33f), new Vector2(143f, 28.6f), 22,
            TextAnchor.UpperRight, TextColor, font);
        ui.PercentText.rectTransform.anchorMin = new Vector2(1f, 0f);
        ui.PercentText.rectTransform.anchorMax = new Vector2(1f, 0f);
        ui.PercentText.rectTransform.pivot = new Vector2(1f, 0f);

        // 格纹进度条：暗底 + 淡色连续衬底 + 40 个分段格槽（逐格点亮，三角洲白格纹条）
        RectTransform progressBg = CreateRect("ProgressBg", bottom);
        progressBg.anchorMin = new Vector2(0f, 0f);
        progressBg.anchorMax = new Vector2(0f, 0f);
        progressBg.pivot = new Vector2(0f, 0f);
        progressBg.anchoredPosition = new Vector2(0f, 26.4f);
        progressBg.sizeDelta = new Vector2(bottomW, 13.2f);
        Image progressBgImg = progressBg.gameObject.AddComponent<Image>();
        progressBgImg.color = ProgressBgColor;
        progressBgImg.raycastTarget = false;

        RectTransform fill = CreateRect("ProgressFill", progressBg);
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

        // 40 个分段格槽：未点亮=半透明浅灰，点亮=铂金（自动解锁时琥珀）
        for (int s = 0; s < ProgressSlotCount; s++)
        {
            RectTransform cell = CreateRect($"ProgressCell_{s:00}", progressBg);
            cell.anchorMin = new Vector2((float)s / ProgressSlotCount, 0f);
            cell.anchorMax = new Vector2((float)(s + 1) / ProgressSlotCount, 1f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.anchoredPosition = Vector2.zero;
            cell.sizeDelta = new Vector2(-2.2f, -2.2f);
            Image cellImg = cell.gameObject.AddComponent<Image>();
            cellImg.color = SlotEmptyColor;
            cellImg.raycastTarget = false;
            ui.ProgressCells.Add(cellImg);
        }

        // ---- 操作提示（面板最下沿） ----
        ui.HintText = CreateText(ui.Panel, "HintText", LpText.HoldLmb + "  |  " + LpText.ExitHint,
            new Vector2(0f, panelH * 0.025f), new Vector2(trackW, 26.4f), 18,
            TextAnchor.UpperCenter, new Color(0.55f, 0.58f, 0.6f, 0.85f), font);
        ui.HintText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        ui.HintText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        ui.HintText.rectTransform.pivot = new Vector2(0.5f, 0f);

        return ui;
    }

    /// <summary>
    /// 根据区间数据在扫描轨道上创建锁定区高亮。
    /// zones: [(start01, end01), ...] 归一化区间
    /// </summary>
    public static void CreateZoneOverlays(DeltaUI ui, List<Vector2> zones)
    {
        if (ui.ScanTrack == null) return;
        foreach (Image img in ui.ScanZones)
            if (img != null) Object.Destroy(img.gameObject);
        ui.ScanZones.Clear();

        float trackW = ui.ScanTrack.sizeDelta.x;
        float trackH = ui.ScanTrack.sizeDelta.y;

        // 完美窄带的宽度占比：读取一次供本轮全部锁定区复用
        float perfectRatio = LpConfig.PerfectZoneRatio.Value;

        for (int i = 0; i < zones.Count; i++)
        {
            float spanStart = zones[i].x;
            float spanWidth = (zones[i].y - zones[i].x) * trackW;

            // 锁定区：以轨道左端为基准水平铺开，高度与轨道等齐
            RectTransform zoneRect = Place(CreateRect($"Zone_{i}", ui.ScanTrack),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(spanStart * trackW, 0f), new Vector2(spanWidth, trackH));

            var zoneImg = zoneRect.gameObject.AddComponent<Image>();
            zoneImg.color = ZoneColor;
            zoneImg.raycastTarget = false;

            // 必须压到游标下层：锁定区是后创建的，天生盖在游标之上；
            // 在 2 倍以上缩放下，白色区间会把细游标整个吞掉，玩家看不到自己在哪
            zoneRect.SetAsFirstSibling();
            ui.ScanZones.Add(zoneImg);

            // 完美窄带：判定开启时在区间正中叠一条金色竖带。
            // 判定用的比例与这里画出来的宽度取同一个值，保证「看到的」就是「判到的」
            if (perfectRatio > 0.001f)
            {
                RectTransform perfectRect = CreateRect($"Perfect_{i}", zoneRect);
                perfectRect.anchorMin = new Vector2(0.5f, 0f);
                perfectRect.anchorMax = new Vector2(0.5f, 1f);
                perfectRect.pivot = new Vector2(0.5f, 0.5f);
                perfectRect.anchoredPosition = Vector2.zero;
                perfectRect.sizeDelta = new Vector2(spanWidth * perfectRatio, 0f);
                var perfectImg = perfectRect.gameObject.AddComponent<Image>();
                perfectImg.color = PerfectZoneColor;
                perfectImg.raycastTarget = false;
            }
        }

        UpdateTeeth(ui, zones);
    }

    /// <summary>
    /// 锁齿列阵刷新：与锁定区相交的列高亮白色，其余保持暗色（已命中的列除外）。
    /// hitColumns: 已命中列索引集合（可为 null = 无）
    /// </summary>
    public static void UpdateTeeth(DeltaUI ui, List<Vector2> zones, HashSet<int> hitColumns = null)
    {
        for (int i = 0; i < ui.Teeth.Count; i++)
        {
            // 命中过的列优先级最高：已经锁定的槽位不该再退回「可锁定」的提示色，
            // 否则玩家会误以为那一列还能再点一次
            bool alreadyHit = hitColumns != null && hitColumns.Contains(i);

            Color barColor;
            if (alreadyHit)
            {
                barColor = TeethHitColor;
            }
            else if (ColumnTouchesAnyZone(i, zones))
            {
                // 只要与任意一个锁定区有重叠就算「可锁定列」，整列统一提示
                barColor = TeethZoneColor;
            }
            else
            {
                barColor = TeethBarColor;
            }

            foreach (Image bar in ui.Teeth[i].Bars)
                if (bar != null) bar.color = barColor;
        }
    }

    /// <summary>
    /// 第 columnIndex 列是否与任意一个锁定区存在重叠。
    /// 列在归一化轨道上的左右边界由列号均分得出，与锁定区做区间相交判定。
    /// </summary>
    private static bool ColumnTouchesAnyZone(int columnIndex, List<Vector2> zones)
    {
        if (zones == null) return false;

        float columnLeft = columnIndex / (float)TeethColumnCount;
        float columnRight = (columnIndex + 1) / (float)TeethColumnCount;

        for (int z = 0; z < zones.Count; z++)
        {
            // 相交判定写成「列左 < 区右 且 列右 > 区左」，等价于两个区间有公共长度
            if (columnLeft < zones[z].y && columnRight > zones[z].x)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 标记某个锁定区为已命中（白色 → 绿色），对应锁齿列也变绿。
    /// </summary>
    public static void MarkZoneHit(DeltaUI ui, int index, List<Vector2> zones, HashSet<int> hitColumns)
    {
        // 轨道上的高亮块翻成命中色
        if (index >= 0 && index < ui.ScanZones.Count && ui.ScanZones[index] != null)
            ui.ScanZones[index].color = ZoneHitColor;

        if (zones == null)
            return;

        // 把与该区间相交的所有列登记进命中集合，随后整体重刷一遍列色。
        // 只做单区间相交判定，这里不经过 ColumnTouchesAnyZone —— 那会为每一列
        // 重新遍历一遍全部区间，这里只跟 zones[index] 比一次，避免开锁期间的重复分配
        float zoneLeft = zones[index].x;
        float zoneRight = zones[index].y;
        for (int i = 0; i < TeethColumnCount; i++)
        {
            float columnLeft = i / (float)TeethColumnCount;
            float columnRight = (i + 1) / (float)TeethColumnCount;
            if (columnLeft < zoneRight && columnRight > zoneLeft)
                hitColumns.Add(i);
        }

        UpdateTeeth(ui, zones, hitColumns);
    }

    /// <summary>
    /// 切换游标配色：冷却期间转为红色，外发光层跟随同步。
    /// </summary>
    public static void SetCursorColor(DeltaUI ui, bool inCd)
    {
        Image img = ui.ScanCursor?.GetComponent<Image>();
        if (img != null)
            img.color = inCd ? CdCursorColor : CursorColor;
        if (ui.CursorGlow != null)
        {
            Color glow = inCd ? CdCursorColor : CursorColor;
            ui.CursorGlow.color = new Color(glow.r, glow.g, glow.b, 0.25f);
        }
    }

    /// <summary>
    /// 按归一化坐标把游标摆到轨道上的对应位置。
    /// </summary>
    public static void SetCursorPosition(DeltaUI ui, float pos01)
    {
        if (ui.ScanCursor == null) return;
        float trackW = ui.ScanTrack.sizeDelta.x;
        float x = Mathf.Lerp(-trackW * 0.5f, trackW * 0.5f, pos01);
        // 两个属性都写：仅靠 anchoredPosition 在被布局系统干扰时会失准，用 localPosition 做兜底
        ui.ScanCursor.anchoredPosition = new Vector2(x, 0f);
        ui.ScanCursor.localPosition = new Vector3(x, 0f, 0f);
    }

    /// <summary>
    /// 顶部槽位条刷新：前 ceil(progress01 * SlotBarCount) 格点亮（三角洲式进度刻度带）。
    /// </summary>
    public static void UpdateSlotBar(DeltaUI ui, float progress01)
    {
        if (ui.SlotBarCells == null) return;
        float p = Mathf.Clamp01(progress01);
        int lit = Mathf.CeilToInt(p * ui.SlotBarCells.Count - 0.001f);
        for (int i = 0; i < ui.SlotBarCells.Count; i++)
        {
            Image cell = ui.SlotBarCells[i];
            if (cell == null) continue;
            cell.color = i < lit ? SlotBarLitColor : SlotBarEmptyColor;
        }
    }

    /// <summary>
    /// 底部 40 格进度槽刷新：逐格点亮（铂金）；auto=true 时用琥珀色表示自动解锁通道。
    /// </summary>
    public static void UpdateProgressBar(DeltaUI ui, float progress01, bool auto)
    {
        if (ui.ProgressCells == null) return;
        float p = Mathf.Clamp01(progress01);
        int lit = (int)(p * ui.ProgressCells.Count + 0.0001f);
        Color litColor = auto ? SlotAutoColor : SlotFullColor;
        for (int i = 0; i < ui.ProgressCells.Count; i++)
        {
            Image cell = ui.ProgressCells[i];
            if (cell == null) continue;
            cell.color = i < lit ? litColor : SlotEmptyColor;
        }
    }

    /// <summary>
    /// 环内常亮进度弧刷新（0-1 径向填充）。
    /// </summary>
    public static void UpdateRingProgress(DeltaUI ui, float progress01)
    {
        if (ui.RingArc != null)
            ui.RingArc.fillAmount = Mathf.Clamp01(progress01);
    }

    /// <summary>
    /// 第三批：环内命中统计（命中 N/M · 完美 K）。此前只显示百分比，玩家看不到「还差几下」。
    /// </summary>
    public static void UpdateHitStats(DeltaUI ui, int hits, int required, int perfects)
    {
        if (ui.HitStatText == null) return;
        ui.HitStatText.text = LpText.HitStat(hits, required, perfects);
    }

    /// <summary>
    /// 第三批：左下线框示意图接线。此前 D_Core/D_Dot1/D_Dot2 建好后从未被刷新，是死装饰。
    /// 锁芯方块随进度由暗转亮并轻微缩放，两个指示点分别对应「已命中」与「有完美」。
    /// </summary>
    public static void UpdateDiagram(DeltaUI ui, float progress01, bool anyHit, bool anyPerfect)
    {
        float p = Mathf.Clamp01(progress01);
        if (ui.DiagramCore != null)
        {
            Color idle = new Color(0.45f, 0.58f, 0.56f, 0.35f);
            // 底色向青绿推进，越接近完成越亮
            ui.DiagramCore.color = Color.Lerp(idle, new Color(0.32f, 0.85f, 0.72f, 0.75f), p);
            // 缩放 0.85→1.0，给一点「充能」体感（幅度刻意小，避免抖动）
            float s = Mathf.Lerp(0.85f, 1f, p);
            ui.DiagramCore.rectTransform.localScale = new Vector3(s, s, 1f);
        }
        if (ui.DiagramDot1 != null)
            ui.DiagramDot1.color = anyHit
                ? new Color(0.2f, 1f, 0.47f, 0.95f)
                : new Color(0.45f, 0.58f, 0.56f, 0.35f);
        if (ui.DiagramDot2 != null)
            ui.DiagramDot2.color = anyPerfect
                ? new Color(1f, 0.82f, 0.3f, 0.95f)
                : new Color(0.45f, 0.58f, 0.56f, 0.35f);
    }

    /// <summary>
    /// 第三批：失误红闪 / 完美白闪。
    /// err01 0→1 衰减驱动整块面板描边转红；perfect01 驱动环内弧与标签闪铂金白。
    /// 两个值都由 DeltaLockGame 每帧衰减后传入，0 表示已恢复常态。
    /// </summary>
    public static void FlashFeedback(DeltaUI ui, float err01, float perfect01)
    {
        float e = Mathf.Clamp01(err01);
        if (ui.PanelOutline != null)
        {
            // 红闪叠在常态描边色上，衰减到 0 时精确回到 PanelBorderColor
            ui.PanelOutline.effectColor = Color.Lerp(PanelBorderColor, CdCursorColor, e);
        }

        float pf = Mathf.Clamp01(perfect01);
        if (ui.UnlockLabel != null)
        {
            // 完美瞬间标签转铂金白，常态回环绿色
            ui.UnlockLabel.color = Color.Lerp(RingColor, new Color(1f, 1f, 1f, 1f), pf);
        }
        if (ui.RingGlowImg != null)
        {
            // 完美时额外加一层白辉，抬高正反馈的亮度峰值
            float breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f);
            float baseA = 0.10f + 0.16f * breathe;
            Color g = Color.Lerp(new Color(RingColor.r, RingColor.g, RingColor.b, baseA),
                                 new Color(1f, 1f, 1f, 0.55f), pf);
            ui.RingGlowImg.color = g;
        }
    }

    // ---- 工具方法 ----

    private static RectTransform CreateRect(string name, Transform parent)
        => UiKit.CreateRect(name, parent);

    /// <summary>
    /// 摆位辅助：把一个 RectTransform 一次性摆到指定锚点/轴心/偏移/尺寸。
    /// 面板里所有控件都是「锚点 + 轴心 + 偏移 + 尺寸」四元组，
    /// 逐字段手写既冗长又容易写串，这里收口成一处，出错面收敛到单个方法。
    /// </summary>
    /// <param name="anchor">锚点归一化坐标（0..1，相对父矩形）。</param>
    /// <param name="pivot">轴心归一化坐标。</param>
    /// <param name="pos">相对锚点的像素偏移。</param>
    /// <param name="size">控件像素尺寸。</param>
    private static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        => UiKit.Place(rt, anchor, pivot, pos, size);

    /// <summary>
    /// 摆位并着色：Place 的常见组合，再挂一个不可交互的纯色 Image。
    /// 面板里绝大多数元素都是纯色块，用这一个入口即可，
    /// 同时统一保证 raycastTarget 关闭（否则会挡住鼠标输入）。
    /// </summary>
    private static Image Solid(RectTransform rt, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 size, Color color, bool keepAspect = false)
        => UiKit.Solid(rt, anchor, pivot, pos, size, color, keepAspect);

    /// <summary>以中心为锚点与轴心的快捷摆法。</summary>
    private static RectTransform AtCenter(RectTransform rt, Vector2 pos, Vector2 size)
        => UiKit.AtCenter(rt, pos, size);

    /// <summary>中心归一化坐标，避免各调用点重复书写字面量。</summary>
    private static Vector2 Center => UiKit.Center;

    /// <summary>居中靠上的锚点（用于从顶部往下排布的元素）。</summary>
    private static Vector2 TopCenter => UiKit.TopCenter;


    /// <summary>
    /// 面板四角刻线：L 形两笔。cx/cy 为 -1/+1，指定角所在方向。
    /// </summary>
    private static void DrawCorner(RectTransform panel, string name, float cx, float cy, float len, float thick)
    {
        float hx = panel.sizeDelta.x * 0.5f * cx;
        float hy = panel.sizeDelta.y * 0.5f * cy;
        float offX = len * 0.5f * cx;
        float offY = len * 0.5f * cy;

        RectTransform h = CreateRect(name + "_H", panel);
        h.anchorMin = new Vector2(0.5f, 0.5f);
        h.anchorMax = new Vector2(0.5f, 0.5f);
        h.pivot = new Vector2(0.5f, 0.5f);
        h.anchoredPosition = new Vector2(hx - offX, hy - thick * 0.5f * cy);
        h.sizeDelta = new Vector2(len, thick);
        Image hi = h.gameObject.AddComponent<Image>();
        hi.color = CornerColor;
        hi.raycastTarget = false;

        RectTransform v = CreateRect(name + "_V", panel);
        v.anchorMin = new Vector2(0.5f, 0.5f);
        v.anchorMax = new Vector2(0.5f, 0.5f);
        v.pivot = new Vector2(0.5f, 0.5f);
        v.anchoredPosition = new Vector2(hx - thick * 0.5f * cx, hy - offY);
        v.sizeDelta = new Vector2(thick, len);
        Image vi = v.gameObject.AddComponent<Image>();
        vi.color = CornerColor;
        vi.raycastTarget = false;
    }

    /// <summary>
    /// 绘制线框示意用的实心小矩形；调用点以元素中心为基准定位，多个小矩形拼合出线条与方块。
    /// pos 是相对父级中心的像素偏移。
    /// </summary>
    private static Image DrawDiagramLine(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        RectTransform rt = CreateRect(name, parent);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;   // 第三批：返回引用，供 UpdateDiagram 随进度改色/缩放
    }

    private static Text CreateText(Transform parent, string name, string content,
        Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color, Font font)
        => UiKit.CreateText(parent, name, content, position, size, fontSize, alignment, color, font);

    /// <summary>
    /// 判断该字体是否确定「渲染不出汉字」。
    ///
    /// 不能拿字体名字推断：内置 Arial 的 name 就是 Arial，但玩家换上的第三方字体
    /// 同样可能顶着别的名字却根本没有汉字字形，反过来也成立。唯一可靠的依据是字形表本身。
    ///
    /// 两种查询重载都试一遍：部分引擎版本只实现了带 FontStyle 的那个重载。
    /// 任何异常都判定为「有中文」——宁可显示成方块，也不要把中文界面整片退回英文。
    /// </summary>
    private static bool FontLacksCjkGlyphs(Font font)
        => UiKit.FontLacksCjkGlyphs(font);

    /// <summary>
    /// 取得用于本面板的字体。
    ///
    /// 优先借用界面里已经在用的字体（游戏的界面字体自带完整汉字字形，风格也统一）；
    /// 实在找不到才退回引擎内置字体——那种字体没有汉字，此时由调用方把文案切成英文。
    /// </summary>
    private static Font ResolveFont(Transform parent)
        => UiKit.ResolveFont(parent);

    /// <summary>
    /// 每帧驱动右侧“开锁中”状态环：旋转弧匀速转 + 外发光呼吸 + 文案省略号跳动。
    /// progress01 用来点亮环内常亮进度弧与顶部槽位条（三角洲破解中指示：转盘转 + 进度弧推进）。
    /// </summary>
    public static void AnimateUnlockRing(DeltaUI ui, float time, float dt, bool inCd, float progress01 = 0f)
    {
        // 面板在跑说明已进游戏、后端语言表通常已下发：给语言判定一次重解析机会（最多 3 次）
        LpText.Invalidate();

        if (ui.RingSpin != null)
        {
            ui.RingSpin.Rotate(0f, 0f, (inCd ? -110f : -220f) * dt);
            var spinImg = ui.RingSpin.GetComponent<Image>();
            if (spinImg != null)
            {
                Color c = inCd ? CdCursorColor : RingColor;
                c.a = inCd ? 0.55f : 1f;
                spinImg.color = c;
            }
        }
        if (ui.RingTicks != null)
        {
            var tickImg = ui.RingTicks;
            tickImg.color = inCd
                ? new Color(CdCursorColor.r, CdCursorColor.g, CdCursorColor.b, 0.40f)
                : new Color(RingColor.r, RingColor.g, RingColor.b, 0.45f);
        }
        if (ui.RingGlowImg != null)
        {
            float breathe = 0.5f + 0.5f * Mathf.Sin(time * 4.2f);
            Color g = inCd ? CdCursorColor : RingColor;
            ui.RingGlowImg.color = new Color(g.r, g.g, g.b, 0.10f + 0.16f * breathe);
        }
        if (ui.RingArc != null)
        {
            ui.RingArc.fillAmount = Mathf.Clamp01(progress01);
            ui.RingArc.color = inCd
                ? new Color(CdCursorColor.r, CdCursorColor.g, CdCursorColor.b, 0.9f)
                : new Color(0.85f, 1f, 0.92f, 0.95f);
        }
        UpdateSlotBar(ui, progress01);
        if (ui.UnlockLabel != null)
        {
            int dots = 1 + (int)(time * 2f % 3f); // 1~3 点循环
            string base_ = LpText.Working;
            string dotStr = LpText.DotSuffix;
            string s = base_;
            for (int i = 0; i < dots; i++) s += dotStr;
            ui.UnlockLabel.text = s;
        }
    }

    /// <summary>
    /// 代码生成缺口圆弧 Sprite（旋转动画载体，无素材依赖）。sweepDeg 为弧长角度。
    /// </summary>
    private static Sprite CreateArcSprite(int size, float thickness, Color color, float sweepDeg)
        => UiKit.CreateArcSprite(size, thickness, color, sweepDeg);

    /// <summary>
    /// 在运行时用代码画出空心圆环贴图，不依赖任何外部素材。
    /// </summary>
    private static Sprite CreateRingSprite(int size, float thickness, Color color)
        => UiKit.CreateRingSprite(size, thickness, color);

    /// <summary>
    /// 代码生成刻度盘 Sprite：环外一圈等分短刻线（三角洲转盘外圈）。
    /// radiusFrac 为刻线所在半径占半尺寸的比例，tickLen 为刻线长度（像素）。
    /// </summary>
    private static Sprite CreateTicksSprite(int size, int count, float radiusFrac, float tickLen, Color color)
        => UiKit.CreateTicksSprite(size, count, radiusFrac, tickLen, color);
}
