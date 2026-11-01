// SPDX-License-Identifier: AGPL-3.0-or-later
using UnityEngine;
using UnityEngine.UI;

namespace LockpickStandalone.Ui;

/// <summary>
/// 两套面板（撬锁 / 骇入）共用的 UGUI 构建基元。
/// 这里只做「造对象、摆位置、着色、画程序化精灵」四件事，不含任何业务配色与判定逻辑。
/// 所有方法均为无状态的静态纯函数，调用方传什么就摆什么。
/// </summary>
public static class UiKit
{
    // ---- 目前只用到这两个归一化锚点；按需再加，不预置一堆没人用的常量 ----
    public static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    public static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    /// <summary>探测字形用的字符与字号：任取一个汉字即可。</summary>
    public const char CjkProbeChar = '中';
    public const int GlyphProbeSize = 32;

    // ---------------- 对象创建 ----------------

    /// <summary>建一个只带 RectTransform 的空节点并挂到父级（保持局部坐标，不受父级缩放影响）。</summary>
    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// 摆位：把一个 RectTransform 一次性摆到「锚点 + 轴心 + 偏移 + 尺寸」。
    /// 面板里所有控件都是这四元组，逐字段手写既冗长又容易写串，收口到这一处。
    /// </summary>
    /// <param name="anchor">锚点归一化坐标（0..1，相对父矩形）。</param>
    /// <param name="pivot">轴心归一化坐标。</param>
    /// <param name="pos">相对锚点的像素偏移。</param>
    /// <param name="size">控件像素尺寸。</param>
    public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>以中心为锚点与轴心的快捷摆法。</summary>
    public static RectTransform AtCenter(RectTransform rt, Vector2 pos, Vector2 size)
    {
        return Place(rt, Center, Center, pos, size);
    }

    /// <summary>
    /// 摆位并着色：Place 的常见组合，再挂一个不可交互的纯色 Image。
    /// 统一保证 raycastTarget 关闭，否则纯装饰块会挡住鼠标输入。
    /// </summary>
    public static Image Solid(RectTransform rt, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 size, Color color, bool keepAspect = false)
    {
        Place(rt, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = keepAspect;
        return img;
    }

    /// <summary>建节点 + 摆位 + 着色一步到位。</summary>
    public static Image Solid(Transform parent, string name, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 size, Color color, bool keepAspect = false)
    {
        return Solid(CreateRect(name, parent), anchor, pivot, pos, size, color, keepAspect);
    }

    /// <summary>
    /// 建一个居中对齐的 Text。horizontalOverflow/verticalOverflow 都放开，
    /// 免得文案变长时被裁剪成半截。
    /// </summary>
    public static Text CreateText(Transform parent, string name, string content,
        Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        var text = go.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.font = font;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    // ---------------- 字体 ----------------

    /// <summary>
    /// 判断该字体是否确定「渲染不出汉字」。
    ///
    /// 不能拿字体名字推断：内置 Arial 的 name 就是 Arial，但玩家换上的第三方字体
    /// 同样可能顶着别的名字却根本没有汉字字形，反过来也成立。唯一可靠的依据是字形表本身。
    ///
    /// 两种查询重载都试一遍：部分引擎版本只实现了带 FontStyle 的那个重载。
    /// 任何异常都判定为「有中文」——宁可显示成方块，也不要把中文界面整片退回英文。
    /// </summary>
    public static bool FontLacksCjkGlyphs(Font font)
    {
        if (font == null)
            return false;

        try
        {
            CharacterInfo info;
            if (font.GetCharacterInfo(CjkProbeChar, out info, GlyphProbeSize, FontStyle.Normal))
                return false;
            if (font.GetCharacterInfo(CjkProbeChar, out info, GlyphProbeSize))
                return false;

            // 两次都明确报告「取不到字形」才认定缺字
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 取得用于面板的字体。
    ///
    /// 优先借用界面里已经在用的字体（游戏界面字体自带完整汉字字形，风格也统一）；
    /// 实在找不到才退回引擎内置字体——那种字体没有汉字，此时由调用方把文案切成英文。
    /// </summary>
    public static Font ResolveFont(Transform parent)
    {
        Text alreadyOnScreen = parent == null ? null : parent.GetComponentInChildren<Text>(includeInactive: true);
        if (alreadyOnScreen != null && alreadyOnScreen.font != null)
            return alreadyOnScreen.font;

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    // ---------------- 程序化精灵（无素材依赖） ----------------

    /// <summary>运行时画一张空心圆环贴图。</summary>
    public static Sprite CreateRingSprite(int size, float thickness, Color color)
    {
        var tex = NewTex(size, "DeltaRing");
        float radius = size * 0.5f - 1f;
        float inner = radius - thickness;
        var pixels = new Color[size * size];
        float center = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = dist <= radius && dist >= inner ? color : Color.clear;
            }
        }
        return Finish(tex, pixels, size);
    }

    /// <summary>运行时画一张缺口圆弧贴图（旋转动画载体）。sweepDeg 为弧长角度。</summary>
    public static Sprite CreateArcSprite(int size, float thickness, Color color, float sweepDeg)
    {
        var tex = NewTex(size, "DeltaArc");
        float radius = size * 0.5f - 1f;
        float inner = radius - thickness;
        var pixels = new Color[size * size];
        float center = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (ang < 0f) ang += 360f;
                bool inArc = ang <= sweepDeg;
                pixels[y * size + x] = (dist <= radius && dist >= inner && inArc) ? color : Color.clear;
            }
        }
        return Finish(tex, pixels, size);
    }

    /// <summary>
    /// 运行时画一张刻度盘贴图：环外一圈等分短刻线。
    /// radiusFrac 为刻线所在半径占半尺寸的比例，tickLen 为刻线长度（像素）。
    /// </summary>
    public static Sprite CreateTicksSprite(int size, int count, float radiusFrac, float tickLen, Color color)
    {
        var tex = NewTex(size, "DeltaTicks");
        var pixels = new Color[size * size];
        float center = size * 0.5f;
        float radius = (size * 0.5f - 2f) * Mathf.Clamp01(radiusFrac);
        for (int t = 0; t < count; t++)
        {
            float ang = (t / (float)count) * Mathf.PI * 2f;
            float dirX = Mathf.Cos(ang);
            float dirY = Mathf.Sin(ang);
            // 每根刻线自内向外逐像素铺，保证细刻线在任何分辨率下都连续
            for (float d = 0f; d < tickLen; d += 0.5f)
            {
                int px = Mathf.RoundToInt(center + dirX * (radius + d) - 0.5f);
                int py = Mathf.RoundToInt(center + dirY * (radius + d) - 0.5f);
                if (px < 0 || py < 0 || px >= size || py >= size) continue;
                pixels[py * size + px] = color;
            }
        }
        return Finish(tex, pixels, size);
    }

    private static Texture2D NewTex(int size, string name)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.name = name;
        return tex;
    }

    private static Sprite Finish(Texture2D tex, Color[] pixels, int size)
    {
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 1f);
    }

    /// <summary>把一个 Image 切到径向填充模式（做进度环用）。</summary>
    public static void MakeRadialFill(Image img, Image.Origin360 origin, bool clockwise)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)origin;
        img.fillClockwise = clockwise;
        img.fillAmount = 0f;
    }

    /// <summary>
    /// 销毁面板节点。
    /// ★注意：Object.Destroy(GameObject) **不会**连带释放程序化生成的 Sprite / Texture2D
    /// （Unity 只销毁 GameObject 与挂在它上面的组件，贴图是独立资源）。
    /// 两套面板都是「一局只建一次、只切显隐」，因此目前没有泄漏；
    /// 若将来要做面板重建，这里必须连 Sprite 与其 texture 一起 Destroy。
    /// </summary>
    public static void Release(RectTransform rt)
    {
        if (rt == null) return;
        Object.Destroy(rt.gameObject);
    }
}
