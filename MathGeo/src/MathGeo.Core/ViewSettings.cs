namespace MathGeo.Core;

/// <summary>
/// 视图显示选项。
///
/// 和 Camera 一样，它描述的是"怎么看"，不是"是什么"，所以**不写进题目文件**——
/// 它属于每台机器上的个人偏好。老师调完网格、Y 轴方向、标记大小之后，
/// 题目文件还是原来那一份，发给别人不会带上自己的显示习惯。
/// </summary>
public sealed record ViewSettings
{
    /// <summary>
    /// 是否显示网格与坐标轴。
    ///
    /// 默认关闭：内核是库，不该默认往图里加装饰 —— 导出到 PPT 时多数时候只要图形本身。
    /// 外壳（应用、预览、命令行）各自决定要不要打开。
    /// </summary>
    public bool ShowGrid { get; init; }

    /// <summary>Y 轴反向：整个视图上下翻转。</summary>
    public bool InvertY { get; init; }

    /// <summary>格边长（世界单位）。</summary>
    public double GridSpacing { get; init; } = 1;

    /// <summary>从原点往每侧画多少格。</summary>
    public int GridExtent { get; init; } = 4;

    public static ViewSettings Default { get; } = new();

    /// <summary>网格的半边长（世界单位）。</summary>
    public double HalfExtent => Math.Max(GridExtent, 1) * Math.Max(GridSpacing, 1e-6);
}
