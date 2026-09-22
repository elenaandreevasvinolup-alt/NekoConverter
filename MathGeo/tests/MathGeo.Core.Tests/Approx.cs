namespace MathGeo.Core.Tests;

internal static class Approx
{
    public static void Vec(Vec3 expected, Vec3 actual, double tolerance = 1e-9)
        => Assert.True(
            Vec3.Distance(expected, actual) <= tolerance,
            $"期望 {expected}，实际 {actual}");

    public static void Vec(Vec2 expected, Vec2 actual, double tolerance = 1e-9)
        => Assert.True(
            Vec2.Distance(expected, actual) <= tolerance,
            $"期望 {expected}，实际 {actual}");

    public static void Scalar(double expected, double actual, double tolerance = 1e-9)
        => Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"期望 {expected}，实际 {actual}");
}
