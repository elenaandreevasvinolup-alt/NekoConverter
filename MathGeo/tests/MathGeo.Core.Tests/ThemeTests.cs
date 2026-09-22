namespace MathGeo.Core.Tests;

/// <summary>
/// 主题与配色。
///
/// 交互式预览靠 ThemePalette 做瞬时主题切换（否则一份 36 帧的动画要让文件翻倍），
/// 所以"每个浅色都能映射到深色"是硬要求：漏一个颜色，深色模式下那一处会保持浅色，
/// 压在深背景上直接看不见。
/// </summary>
public class ThemeTests
{
    private static string[] LightColors(Theme theme) =>
    [
        theme.Background, theme.PageBackground, theme.SurfaceBorder,
        theme.PointFill, theme.LineStroke, theme.CurveStroke,
        theme.HelperStroke, theme.AnswerStroke, theme.HighlightStroke, theme.AccentSoft,
        theme.PolygonFill, theme.PolygonStroke, theme.FaceStroke,
        theme.FaceFillHighlight, theme.SectionFill, theme.SectionStroke,
        theme.AngleStroke, theme.LabelFill,
    ];

    [Fact]
    public void 浅色主题的每个颜色都有深色对应()
    {
        foreach (var color in LightColors(Theme.Textbook))
            Assert.True(ThemePalette.DarkMap.ContainsKey(color),
                $"深色映射缺少 {color} —— 深色模式下这一处会保持浅色");
    }

    [Fact]
    public void 映射目标必须是深色主题里真实存在的颜色()
    {
        var darkColors = new HashSet<string>(LightColors(Theme.Dark), StringComparer.OrdinalIgnoreCase);

        foreach (var (from, to) in ThemePalette.DarkMap)
            Assert.True(darkColors.Contains(to),
                $"{from} → {to}，但 {to} 不在深色主题里（多半是抄错了）");
    }

    [Fact]
    public void 映射表里没有多余的颜色()
    {
        var lightColors = new HashSet<string>(LightColors(Theme.Textbook), StringComparer.OrdinalIgnoreCase);

        foreach (var from in ThemePalette.DarkMap.Keys)
            Assert.True(lightColors.Contains(from),
                $"{from} 在映射表里，但浅色主题已经不用它了 —— 表该清一清");
    }

    [Fact]
    public void 深浅主题的图底不同()
    {
        Assert.NotEqual(Theme.Textbook.Background, Theme.Dark.Background);
        Assert.NotEqual(Theme.Textbook.LabelFill, Theme.Dark.LabelFill);
        Assert.NotEqual(Theme.Textbook.PageBackground, Theme.Dark.PageBackground);
    }

    [Fact]
    public void 深色主题的图底不是纯黑()
    {
        // 纯黑在投影和希沃一体机上会把辅助线吃掉，用 #1E222A 这类深灰更耐看。
        Assert.NotEqual("#000000", Theme.Dark.Background.ToUpperInvariant());
    }

    [Fact]
    public void 线宽与半径在两个主题里保持一致()
    {
        // 主题只该换颜色，不该换几何观感 —— 否则切主题时图形会"跳"一下。
        Assert.Equal(Theme.Textbook.LineWidth, Theme.Dark.LineWidth);
        Assert.Equal(Theme.Textbook.HelperWidth, Theme.Dark.HelperWidth);
        Assert.Equal(Theme.Textbook.PointRadius, Theme.Dark.PointRadius);
        Assert.Equal(Theme.Textbook.LabelSize, Theme.Dark.LabelSize);
    }
}
