namespace MathGeo.Core.Tests;

/// <summary>
/// .problem.json 的读写、校验与宽容度。
///
/// 这个文件格式是整个产品的契约：内核读它画图，外部 agent 写它出题。
/// 所以测试的重点不是"能读"，而是"写错的时候会不会给出能看懂的错误"。
/// </summary>
public class ProblemFileTests
{
    private const string MidpointProblem = """
    {
      "schema": 1,
      "id": "g8-tri-012",
      "kind": "2d-geometry",
      "lang": "zh-Hans",
      "title": "中点与中线",
      "statement": "△ABC 中，AB=5，D 是 BC 中点，求 AD。",
      "objects": [
        { "id": "A", "type": "point", "pointKind": "free", "at": [0, 0], "label": "A" },
        { "id": "B", "type": "point", "pointKind": "free", "at": [5, 0], "label": "B" },
        { "id": "C", "type": "point", "pointKind": "free", "at": [1, 4], "label": "C" },
        { "id": "BC", "type": "segment", "a": "B", "b": "C" },
        { "id": "D", "type": "point", "pointKind": "derived", "derived": "midpoint",
          "args": ["B", "C"], "label": "D" },
        { "id": "AD", "type": "segment", "a": "A", "b": "D", "style": "answer" }
      ],
      "ask": { "target": "AD", "measure": "length" },
      "answer": { "value": "√13", "steps": ["求 BC 中点 D", "两点距离公式"] },
      "source": { "type": "agent", "file": "ch3.pdf", "page": "12", "model": "qwen-max" }
    }
    """;

    [Fact]
    public void 能读入一道完整的题目并解出正确答案()
    {
        var file = ProblemFile.FromJson(MidpointProblem);
        Assert.NotNull(file);

        var result = ProblemMapper.Load(file);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Approx.Vec(new Vec3(3, 2, 0), result.Scene.Point("D")!.Position);

        var a = result.Scene.Point("A")!.Position;
        var d = result.Scene.Point("D")!.Position;
        Approx.Scalar(Math.Sqrt(13), Vec3.Distance(a, d));
    }

    [Fact]
    public void 元信息被完整保留()
    {
        var file = ProblemFile.FromJson(MidpointProblem)!;

        Assert.Equal(1, file.Schema);
        Assert.Equal("g8-tri-012", file.Id);
        Assert.Equal("2d-geometry", file.Kind);
        Assert.Equal("中点与中线", file.Title);
        Assert.Equal("√13", file.Answer!.Value);
        Assert.Equal("qwen-max", file.Source!.Model);
        Assert.Equal("ch3.pdf", file.Source.File);
    }

    [Fact]
    public void 序列化是驼峰命名的缩进JSON()
    {
        var json = ProblemFile.FromJson(MidpointProblem)!.ToJson();

        Assert.Contains("\"pointKind\"", json);
        Assert.DoesNotContain("\"PointKind\"", json);
        Assert.Contains("\n", json);                 // 缩进输出，老师能看懂、能手改
        Assert.Contains("△ABC", json);               // 中文不被转义
    }

    [Fact]
    public void 保存后再读回来位置一致()
    {
        var original = ProblemFile.FromJson(MidpointProblem)!;
        var first = ProblemMapper.Load(original);
        Assert.True(first.Success);

        var saved = ProblemMapper.Save(first.Scene, original);
        var reloaded = ProblemMapper.Load(ProblemFile.FromJson(saved.ToJson())!);

        Assert.True(reloaded.Success, string.Join("; ", reloaded.Diagnostics));

        foreach (var id in new[] { "A", "B", "C", "D" })
            Approx.Vec(first.Scene.Point(id)!.Position, reloaded.Scene.Point(id)!.Position);

        // 样式角色也要活下来，否则"所求对象高亮"会在存盘后消失
        Assert.Equal("answer", reloaded.Scene.Find("AD")!.Style);
    }

    [Fact]
    public void 点类型可以省略_由字段自动推断()
    {
        const string json = """
        {
          "schema": 1,
          "objects": [
            { "id": "A", "type": "point", "at": [0, 0] },
            { "id": "B", "type": "point", "at": [4, 0] },
            { "id": "AB", "type": "segment", "a": "A", "b": "B" },
            { "id": "P", "type": "point", "path": "AB", "param": 0.25 },
            { "id": "M", "type": "point", "derived": "mid", "args": ["A", "B"] }
          ]
        }
        """;

        var result = ProblemMapper.Load(ProblemFile.FromJson(json)!);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(PointKind.Free, result.Scene.Point("A")!.Kind);
        Assert.Equal(PointKind.OnPath, result.Scene.Point("P")!.Kind);
        Assert.Equal(PointKind.Derived, result.Scene.Point("M")!.Kind);
        Approx.Vec(new Vec3(1, 0, 0), result.Scene.Point("P")!.Position);
        Approx.Vec(new Vec3(2, 0, 0), result.Scene.Point("M")!.Position);
    }

    [Fact]
    public void 动点与驱动器能存能读()
    {
        const string json = """
        {
          "schema": 1,
          "objects": [
            { "id": "O", "type": "point", "at": [0, 0] },
            { "id": "c", "type": "circle", "center": "O", "radius": 2 },
            { "id": "C", "type": "point", "path": "c", "param": 0.25,
              "driver": { "min": 0, "max": 1, "value": 0.25, "speed": 0.5, "pingPong": true } }
          ]
        }
        """;

        var loaded = ProblemMapper.Load(ProblemFile.FromJson(json)!);
        Assert.True(loaded.Success);

        var c = loaded.Scene.Point("C")!;
        Assert.NotNull(c.Driver);
        Approx.Vec(new Vec3(0, 2, 0), c.Position);

        var saved = ProblemMapper.Save(loaded.Scene);
        Assert.Contains("\"driver\"", saved.ToJson());

        var reloaded = ProblemMapper.Load(ProblemFile.FromJson(saved.ToJson())!);
        Assert.NotNull(reloaded.Scene.Point("C")!.Driver);
    }

    // ————————————————————— 校验：写错了要看得懂 —————————————————————

    [Fact]
    public void 引用不存在的对象会被指出()
    {
        var file = ProblemFile.FromJson("""
        {
          "schema": 1,
          "objects": [
            { "id": "A", "type": "point", "at": [0, 0] },
            { "id": "l", "type": "segment", "a": "A", "b": "Z" }
          ]
        }
        """)!;

        var diagnostics = ProblemMapper.Validate(file);

        var error = Assert.Single(diagnostics, d => d.Code == "ref.missing");
        Assert.Equal("l", error.ObjectId);
        Assert.Contains("Z", error.Message);
    }

    [Fact]
    public void 重复的id会被指出()
    {
        var file = ProblemFile.FromJson("""
        {
          "schema": 1,
          "objects": [
            { "id": "A", "type": "point", "at": [0, 0] },
            { "id": "A", "type": "point", "at": [1, 0] }
          ]
        }
        """)!;

        Assert.Contains(ProblemMapper.Validate(file), d => d.Code == "schema.duplicate");
    }

    [Fact]
    public void 未知的对象类型会被指出()
    {
        var file = ProblemFile.FromJson("""
        {
          "schema": 1,
          "objects": [ { "id": "x", "type": "hyperbola" } ]
        }
        """)!;

        var result = ProblemMapper.Load(file);

        Assert.Contains(result.Diagnostics, d => d.Code == "schema.type.unknown");
        Assert.False(result.Success);
    }

    [Fact]
    public void 多边形顶点不足会被指出()
    {
        var file = ProblemFile.FromJson("""
        {
          "schema": 1,
          "objects": [
            { "id": "A", "type": "point", "at": [0, 0] },
            { "id": "B", "type": "point", "at": [1, 0] },
            { "id": "p", "type": "polygon", "vertices": ["A", "B"] }
          ]
        }
        """)!;

        Assert.Contains(ProblemMapper.Validate(file), d => d.Code == "schema.polygon.vertices");
        Assert.Contains(ProblemMapper.Load(file).Diagnostics, d => d.Code == "schema.polygon.vertices");
    }

    [Fact]
    public void 高版本的schema会被拒绝()
    {
        var file = ProblemFile.FromJson("""{ "schema": 99, "objects": [] }""")!;

        var diagnostics = ProblemMapper.Validate(file);

        Assert.Contains(diagnostics, d => d.Code == "schema.version.tooNew");
        Assert.Contains(diagnostics, d => d.Code == "schema.empty");
    }

    [Fact]
    public void 一个坏对象不会让整题空白()
    {
        var file = ProblemFile.FromJson("""
        {
          "schema": 1,
          "objects": [
            { "id": "A", "type": "point", "at": [0, 0], "label": "A" },
            { "id": "B", "type": "point", "at": [4, 0], "label": "B" },
            { "id": "broken", "type": "nonsense" },
            { "id": "AB", "type": "segment", "a": "A", "b": "B" }
          ]
        }
        """)!;

        var result = ProblemMapper.Load(file);

        // 有错误，但好对象照样画得出来 —— "解析失败也要能出半张图"
        Assert.False(result.Success);
        Assert.NotNull(result.Scene.Point("A"));
        Assert.NotNull(result.Scene.Point("B"));
        Assert.NotNull(result.Scene.Line("AB"));
        Assert.Contains(result.Scene.BuildDisplayList(), s => s is LabelShape { Text: "A" });
    }

    [Fact]
    public void 保存时会自动记录视野范围()
    {
        var result = ProblemMapper.Load(ProblemFile.FromJson(MidpointProblem)!);
        var saved = ProblemMapper.Save(result.Scene);

        Assert.NotNull(saved.View);
        Assert.NotNull(saved.View.Bounds);
        Assert.Equal(4, saved.View.Bounds!.Length);
    }
}
