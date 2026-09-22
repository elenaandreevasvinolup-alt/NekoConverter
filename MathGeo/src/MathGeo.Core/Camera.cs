namespace MathGeo.Core;

/// <summary>
/// 三维相机。默认是"转盘式"而不是自由翻滚：绕 Y 轴转方位角、限制仰角范围。
///
/// 为什么不做自由 trackball：自由翻滚几下就会翻出倒立或接近正上方的天书视角，
/// 老师在课堂上会瞬间失去方向感。课本上的立体图用的就是有限的几个角度，
/// 相机应该顺着这个习惯，而不是跟它作对。
/// </summary>
public sealed record Camera
{
    /// <summary>方位角（绕 Y 轴），度。0 = 从 +Z 方向看过去。</summary>
    public double AzimuthDeg { get; init; } = 35;

    /// <summary>仰角，度。会被限制在 ±89°，避免与世界上方向共线导致基向量退化。</summary>
    public double ElevationDeg { get; init; } = 20;

    /// <summary>
    /// 相机到目标的距离。它同时决定透视强度：
    /// 值越大透视越弱、越接近课本那种"看起来平行"的观感。
    /// </summary>
    public double Distance { get; init; } = 24;

    public Vec3 Target { get; init; } = Vec3.Zero;

    /// <summary>纯正交投影。斜二测、正等测这类课本画法用它。</summary>
    public bool Orthographic { get; init; }


    // —— 一键标准视角。讲课时要的就是这几个固定角度，不要现场调 ——

    public static Camera Textbook { get; } = new();

    /// <summary>正视（正面投影）。</summary>
    public static Camera Front { get; } = new() { AzimuthDeg = 0, ElevationDeg = 0 };

    /// <summary>俯视。</summary>
    public static Camera Top { get; } = new() { AzimuthDeg = 0, ElevationDeg = 89 };

    /// <summary>侧视。</summary>
    public static Camera Side { get; } = new() { AzimuthDeg = 90, ElevationDeg = 0 };

    /// <summary>正等测（课本"直观图"常用的那个角度）。</summary>
    public static Camera Isometric { get; } = new()
    {
        AzimuthDeg = 45,
        ElevationDeg = 35.264,
        Orthographic = true,
    };

    /// <summary>在当前位置上叠加旋转。触摸/鼠标拖拽直接调它。</summary>
    public Camera Orbit(double deltaAzimuthDeg, double deltaElevationDeg) => this with
    {
        AzimuthDeg = AzimuthDeg + deltaAzimuthDeg,
        ElevationDeg = MathUtil.Clamp(ElevationDeg + deltaElevationDeg, -89, 89),
    };

    public CameraView CreateView()
    {
        var azimuth = MathUtil.DegToRad(AzimuthDeg);

        // 允许正好 ±90°：三视图的俯视图需要严格从上往下看。
        // 这里不像 Orbit 那样收窄到 ±89°，因为退化情形下面有兜底。
        var elevation = MathUtil.DegToRad(MathUtil.Clamp(ElevationDeg, -90, 90));
        var cosElevation = Math.Cos(elevation);

        var direction = new Vec3(
            cosElevation * Math.Cos(azimuth),
            Math.Sin(elevation),
            cosElevation * Math.Sin(azimuth));

        var eye = Target + direction * Distance;
        var forward = (Target - eye).Normalized();

        var right = Vec3.Cross(forward, Vec3.UnitY);
        // 正上/正下看时 forward 与世界上方向共线，叉积退化 —— 给一个固定的右方向兜底。
        right = right.LengthSquared < 1e-6 ? Vec3.UnitX : right.Normalized();

        var up = Vec3.Cross(right, forward).Normalized();

        return new CameraView(eye, right, up, forward, Distance, Orthographic);
    }
}

/// <summary>
/// 相机的一次快照：预计算好基向量，之后投影就是几次点乘。
/// 做成结构体是因为每帧旋转都会重建它，而且会被逐顶点调用成千上万次。
/// </summary>
public readonly struct CameraView
{
    private readonly Vec3 _eye;
    private readonly Vec3 _right;
    private readonly Vec3 _up;
    private readonly Vec3 _forward;
    private readonly double _distance;
    private readonly bool _orthographic;

    internal CameraView(Vec3 eye, Vec3 right, Vec3 up, Vec3 forward, double distance, bool orthographic)
    {
        _eye = eye;
        _right = right;
        _up = up;
        _forward = forward;
        _distance = distance;
        _orthographic = orthographic;
    }

    /// <summary>相机到目标的距离。深度判定的容差要按它取相对值。</summary>
    public double Distance => _distance;

    /// <summary>相机在世界坐标里的位置。背面/正面判定要用它。</summary>
    public Vec3 Eye => _eye;

    /// <summary>视线方向（从相机指向目标）。判断"是不是正对着某个坐标面"要用它。</summary>
    public Vec3 Forward => _forward;

    public Vec2 Project(Vec3 world) => ProjectWithDepth(world, out _);

    /// <summary>
    /// 投影并给出深度（相机空间里的前向距离，越小越近）。
    /// 深度是隐藏线判定的唯一依据，所以必须和屏幕坐标一次算出来，不能事后补。
    /// </summary>
    public Vec2 ProjectWithDepth(Vec3 world, out double depth)
    {
        var d = world - _eye;
        var x = Vec3.Dot(d, _right);
        var y = Vec3.Dot(d, _up);
        var z = Vec3.Dot(d, _forward);
        depth = z;

        if (_orthographic || z < MathUtil.Epsilon)
            return new Vec2(x, y);

        // 在目标点附近 k ≈ 1，所以正交和透视的尺度是一致的，
        // 切换投影方式时图不会突然跳大小。
        var k = _distance / z;
        return new Vec2(x * k, y * k);
    }
}
