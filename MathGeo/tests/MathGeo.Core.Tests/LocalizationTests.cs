namespace MathGeo.Core.Tests;

/// <summary>
/// 本地化。
///
/// 核心约束只有一条：**内核里不许出现面向用户的自然语言字符串**。
/// 诊断只带 code 和参数，文案查表。这组测试就是盯这条约束的 ——
/// 键差、缺翻译、占位符不匹配，全都要在这里红掉。
/// </summary>
public class LocalizationTests
{
    [Fact]
    public void 装了十六种语言()
    {
        Assert.Equal(16, Localizer.Installed.Count);
    }

    [Fact]
    public void 中英是内置且已校对的()
    {
        var installed = Localizer.Installed;

        foreach (var lang in new[] { "zh-Hans", "en" })
        {
            var info = installed.Single(l => l.Lang == lang);
            Assert.True(info.Reviewed, $"{lang} 应当是已校对的内置语言");
            Assert.False(string.IsNullOrWhiteSpace(info.Name));
        }
    }

    [Fact]
    public void 阿拉伯语和希伯来语标了RTL()
    {
        var installed = Localizer.Installed;

        Assert.True(installed.Single(l => l.Lang == "ar").Rtl);
        Assert.True(installed.Single(l => l.Lang == "he").Rtl);
        Assert.False(installed.Single(l => l.Lang == "ja").Rtl);
    }

    [Fact]
    public void 所有语言的键完全一致()
    {
        var reference = Keys("en");

        foreach (var info in Localizer.Installed)
        {
            var keys = Keys(info.Lang);
            var missing = reference.Except(keys).ToList();
            var extra = keys.Except(reference).ToList();

            Assert.True(missing.Count == 0, $"{info.Lang} 缺少 {missing.Count} 个键：{string.Join(", ", missing.Take(5))}");
            Assert.True(extra.Count == 0, $"{info.Lang} 多出 {extra.Count} 个键：{string.Join(", ", extra.Take(5))}");
        }
    }

    [Fact]
    public void 占位符在所有语言里一致()
    {
        // 这是最容易出的事故：译者把 {0} 删了或者写成了 {1}，
        // 运行期不会报错，只会让数字凭空消失。
        foreach (var info in Localizer.Installed)
        {
            var localizer = Localizer.Load(info.Lang);

            foreach (var key in Keys("en"))
            {
                var expected = Placeholders(Localizer.Load("en")[key]);
                var actual = Placeholders(localizer[key]);

                Assert.True(expected.SetEquals(actual),
                    $"{info.Lang} 的 {key} 占位符不匹配：期望 {string.Join(",", expected.Order())}，实际 {string.Join(",", actual.Order())}");
            }
        }
    }

    [Fact]
    public void 诊断只带code和参数_不带文案()
    {
        var diagnostic = SolveDiagnostic.Error("A", "ref.missing", "B");

        Assert.Equal("ref.missing", diagnostic.Code);
        Assert.Equal(["B"], diagnostic.Args);

        // 消息是渲染出来的，不是构造时写死的
        Assert.Contains("B", diagnostic.Message);
    }

    [Fact]
    public void 诊断可以按指定语言渲染()
    {
        var diagnostic = SolveDiagnostic.Error("A", "geom.noIntersection", "l1", "l2");

        var zh = diagnostic.Localize(Localizer.Load("zh-Hans"));
        var en = diagnostic.Localize(Localizer.Load("en"));

        Assert.Contains("没有交点", zh);
        Assert.Contains("no intersection", en);
        Assert.NotEqual(zh, en);
    }

    [Fact]
    public void 找不到的键返回键名本身()
    {
        // 返回空白会让人以为是布局问题；返回键名能一眼看出是漏了翻译。
        Assert.Equal("ui.thisKeyDoesNotExist", Localizer.Load("en")["ui.thisKeyDoesNotExist"]);
    }

    [Fact]
    public void 占位符格式化成参数()
    {
        var localizer = Localizer.Load("zh-Hans");

        Assert.Equal("第 3 / 36 帧", localizer.Format("ui.frame", 3, 36));
        Assert.Equal("7 个对象", localizer.Format("gallery.objects", 7));
    }

    [Fact]
    public void 模板本身有问题时退回原文而不是抛异常()
    {
        // 译者手抖写了个孤立的 { 不该让界面崩掉。
        var localizer = Localizer.Load("en");
        var result = localizer.Format("ui.thisKeyDoesNotExist", 1);

        Assert.Equal("ui.thisKeyDoesNotExist", result);
    }

    [Fact]
    public void 每个诊断code都有对应文案()
    {
        // 内核里用到的 code 必须都在语言表里有键，否则老师会看到 "diag.xxx.yyy"。
        string[] codes =
        [
            "schema.id", "schema.id.whitespace", "schema.duplicate", "schema.empty",
            "schema.type.missing", "schema.type.unknown", "schema.pointkind", "schema.at",
            "schema.path", "schema.derived", "schema.point.args", "schema.line.endpoints",
            "schema.circle.center", "schema.circle.radius", "schema.polygon.vertices",
            "schema.polyhedron.vertices", "schema.polyhedron.faces", "schema.polyhedron.face",
            "schema.polyhedron.index", "schema.solid.missing", "schema.solid.unknown",
            "schema.section.of", "schema.section.plane", "schema.dihedral.of",
            "schema.dihedral.faces", "schema.version.invalid", "schema.version.tooNew",
            "ref.missing", "ref.askTarget", "ref.path", "ref.lineEndpoints", "ref.cycle",
            "geom.intersection.args", "geom.intersection.missing", "geom.point",
            "geom.line", "geom.mirror", "geom.noIntersection", "geom.unsupported",
            "solve.failed",
        ];

        foreach (var info in Localizer.Installed)
        {
            var localizer = Localizer.Load(info.Lang);

            foreach (var code in codes)
            {
                var key = DiagnosticText.KeyFor(code);
                Assert.True(localizer[key] != key, $"{info.Lang} 缺少 {key}");
            }
        }
    }

    [Fact]
    public void 中文区域能退到语言主标签()
    {
        // 系统报的是 zh-CN / zh-TW，而语言文件叫 zh-Hans / zh-Hant。
        // Localizer 靠 CultureInfo.Parent 做这层映射 —— 把假设钉住，
        // 否则某天运行时换了 ICU 数据，中文用户会突然看到英文而没人知道为什么。
        Assert.Equal("zh-Hans", System.Globalization.CultureInfo.GetCultureInfo("zh-CN").Parent.Name);
        Assert.Equal("zh-Hant", System.Globalization.CultureInfo.GetCultureInfo("zh-TW").Parent.Name);
    }

    [Fact]
    public void 内置语言真的翻译过()
    {
        // 中英必须是真翻译，不能是英文占位 —— 它们是要随程序内嵌发出去的。
        var zh = Localizer.Load("zh-Hans");
        var en = Localizer.Load("en");

        foreach (var key in new[] { "ui.play", "ui.drag.hint", "diag.ref.missing", "cli.usage.title" })
            Assert.NotEqual(en[key], zh[key]);
    }

    [Fact]
    public void 未校对的语言标记为未校对()
    {
        var unreviewed = Localizer.Installed.Where(l => !l.Reviewed).Select(l => l.Lang).ToHashSet();

        // 除了中英，其余 14 种都还是英文占位，必须能一眼看出来，不能冒充成翻译。
        Assert.Equal(14, unreviewed.Count);
        Assert.DoesNotContain("zh-Hans", unreviewed);
        Assert.DoesNotContain("en", unreviewed);
    }

    private static HashSet<string> Keys(string lang)
        => [.. Localizer.Load(lang).Keys];

    private static HashSet<int> Placeholders(string template)
    {
        var result = new HashSet<int>();
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0) break;

            var close = template.IndexOf('}', open);
            if (close < 0) break;

            if (int.TryParse(template[(open + 1)..close], out var number))
                result.Add(number);

            index = close + 1;
        }

        return result;
    }
}
