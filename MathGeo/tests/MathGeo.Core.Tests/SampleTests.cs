using System.Xml.Linq;

namespace MathGeo.Core.Tests;

/// <summary>
/// samples/ 下的题目是"格式的活文档"：既是给外部 agent 看的范例，
/// 也是回归测试。任何一条加载不了或者渲不出来，就说明文件格式或内核退化了。
/// </summary>
public class SampleTests
{
    private static string SampleDirectory => Path.Combine(AppContext.BaseDirectory, "samples");

    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(SampleDirectory)) return data;

        foreach (var path in Directory.EnumerateFiles(SampleDirectory, "*.problem.json").Order())
            data.Add(Path.GetFileName(path));

        return data;
    }

    [Fact]
    public void 样例目录非空()
    {
        Assert.True(Directory.Exists(SampleDirectory), $"样例目录不存在：{SampleDirectory}");
        Assert.NotEmpty(Directory.EnumerateFiles(SampleDirectory, "*.problem.json"));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void 每个样例都能加载并求解成功(string fileName)
    {
        var result = Load(fileName);

        Assert.True(result.Success, $"{fileName}: {string.Join("; ", result.Diagnostics)}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void 每个样例都能导出良构的SVG(string fileName)
    {
        var svg = Load(fileName).Scene.ToSvg();

        Assert.Contains("<svg", svg);
        XDocument.Parse(svg);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void 每个样例都有标题与题干(string fileName)
    {
        var file = ProblemFile.FromJson(File.ReadAllText(Path.Combine(SampleDirectory, fileName)))!;

        Assert.False(string.IsNullOrWhiteSpace(file.Title), $"{fileName} 缺少 title");
        Assert.False(string.IsNullOrWhiteSpace(file.Statement), $"{fileName} 缺少 statement");
        Assert.False(string.IsNullOrWhiteSpace(file.Id), $"{fileName} 缺少 id");
    }

    [Fact]
    public void 二级动点样例的轨迹半径恒为1()
    {
        // 这条断言把"二级动点"这个产品承诺钉死在样例上：
        // 样例 03 里 MD 应当恒等于 1，与 C 在圆上的位置无关。
        var result = Load("03-second-order-locus.problem.json");
        Assert.True(result.Success);

        var scene = result.Scene;
        Assert.NotNull(scene.Point("C")!.Driver);

        for (var i = 0; i <= 24; i++)
        {
            scene.Drive("C", i / 24.0);
            var m = scene.Point("M")!.Position;
            var d = scene.Point("D")!.Position;
            Approx.Scalar(1.0, Vec3.Distance(m, d), 1e-9);
        }
    }

    [Fact]
    public void 动点样例的P始终落在BC上()
    {
        var result = Load("02-moving-point.problem.json");
        Assert.True(result.Success);

        var scene = result.Scene;
        for (var i = 0; i <= 20; i++)
        {
            scene.Drive("P", i / 20.0);
            var p = scene.Point("P")!.Position;
            Approx.Scalar(4.0, p.X, 1e-9);          // BC 是 x = 4 这条竖线
            Assert.InRange(p.Y, -1e-9, 4 + 1e-9);
        }
    }

    private static ProblemLoadResult Load(string fileName)
    {
        var path = Path.Combine(SampleDirectory, fileName);
        var file = ProblemFile.FromJson(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{fileName} 解析为空");
        return ProblemMapper.Load(file);
    }
}
