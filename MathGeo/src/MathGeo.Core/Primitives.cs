using System.Globalization;

namespace MathGeo.Core;

/// <summary>
/// 几何判定用的统一容差与数值格式化。
/// 所有"约等于"判断都必须走这里，否则容差会散落在各处，旋转到某些角度时
/// 隐藏线或共线判定会开始抖动 —— 这是教学图最典型的翻车点。
/// </summary>
public static class MathUtil
{
    /// <summary>用于向量长度、归一化这类会放大的运算。</summary>
    public const double Epsilon = 1e-9;

    /// <summary>用于几何判定（平行、共线、相交）。</summary>
    public const double Tolerance = 1e-7;

    public static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    public static double RadToDeg(double radians) => radians * 180.0 / Math.PI;

    public static bool NearlyZero(double value, double tolerance = Tolerance)
        => Math.Abs(value) <= tolerance;

    public static bool NearlyEqual(double a, double b, double tolerance = Tolerance)
        => Math.Abs(a - b) <= tolerance;

    public static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    /// <summary>把角度规整到 (-π, π]，用于两直线夹角这类只关心方向的场景。</summary>
    public static double NormalizeAngle(double radians)
    {
        var twoPi = 2 * Math.PI;
        var a = radians % twoPi;
        if (a <= -Math.PI) a += twoPi;
        else if (a > Math.PI) a -= twoPi;
        return a;
    }

    /// <summary>SVG 输出用的稳定格式：固定不变的文化，避免小数点变成逗号。</summary>
    public static string Num(double value)
    {
        if (Math.Abs(value) < 1e-12) return "0";
        var rounded = Math.Round(value, 3);
        if (Math.Abs(rounded - Math.Round(rounded)) < 1e-12)
            return ((long)Math.Round(rounded)).ToString(CultureInfo.InvariantCulture);
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

/// <summary>屏幕/绘图平面上的二维点。渲染层的唯一坐标类型。</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var len = Length;
        return len < MathUtil.Epsilon ? Zero : new Vec2(X / len, Y / len);
    }

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
}

/// <summary>
/// 几何空间的点/向量。
///
/// 为什么二维也用三维类型：这样"点"在对象图里只有一种，求解器、依赖排序、
/// 显示列表、文件格式、SVG 导出全部只写一遍。二维场景就是 Z 恒为 0 的三维场景，
/// 立体几何接进来时不需要再造一套对象模型。
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vec3 operator /(Vec3 a, double k) => new(a.X / k, a.Y / k, a.Z / k);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>二维场景里 Z 是 0，投影到绘图平面就是取 XY。</summary>
    public Vec2 XY => new(X, Y);

    public Vec3 Normalized()
    {
        var len = Length;
        return len < MathUtil.Epsilon ? Zero : this / len;
    }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    /// <summary>绕 Z 轴旋转（二维场景的主要旋转方式），角度用弧度。</summary>
    public Vec3 RotateZ(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec3(X * c - Y * s, X * s + Y * c, Z);
    }

    /// <summary>绕通过 center 且平行于 Z 轴的直线旋转，二维场景用它。</summary>
    public static Vec3 RotateAboutZ(Vec3 point, Vec3 center, double radians)
        => center + (point - center).RotateZ(radians);

    /// <summary>到过 a、b 的直线的垂足。三维通式，二维自动退化为平面垂足。</summary>
    public static Vec3 FootOnLine(Vec3 point, Vec3 a, Vec3 b)
    {
        var dir = b - a;
        var lenSq = dir.LengthSquared;
        if (lenSq < MathUtil.Epsilon) return a;
        var t = Dot(point - a, dir) / lenSq;
        return a + dir * t;
    }

    /// <summary>关于点 center 的中心对称。</summary>
    public static Vec3 ReflectAboutPoint(Vec3 point, Vec3 center) => center * 2 - point;

    public override string ToString() => $"({MathUtil.Num(X)}, {MathUtil.Num(Y)}, {MathUtil.Num(Z)})";
}
