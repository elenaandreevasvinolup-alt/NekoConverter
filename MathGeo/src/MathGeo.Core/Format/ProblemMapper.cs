namespace MathGeo.Core;

public sealed class ProblemLoadResult
{
    public required Scene Scene { get; init; }
    public IReadOnlyList<SolveDiagnostic> Diagnostics { get; init; } = [];

    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public IEnumerable<SolveDiagnostic> Errors
        => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// .problem.json ↔ 对象图 的双向映射。
///
/// 刻意保持"宽容读、严格报"：读的时候尽量把能画的都画出来（缺一个顶点不该导致整题空白），
/// 同时把每个问题记成诊断交给 UI 变成"待修复条件"。这是"解析失败也要能出半张图"的落点。
/// </summary>
public static class ProblemMapper
{
    public static ProblemLoadResult Load(ProblemFile file)
    {
        var diagnostics = new List<SolveDiagnostic>(Validate(file));
        var scene = new Scene();

        foreach (var dto in file.Objects)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                diagnostics.Add(Error(null, "schema.id"));
                continue;
            }

            if (scene.Find(dto.Id) is not null)
            {
                diagnostics.Add(Error(dto.Id, "schema.duplicate", dto.Id));
                continue;
            }

            var objects = MapObject(dto, diagnostics);

            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];

                // 参数化基本体会展开成"若干顶点 + 一个体"。可见性对所有展开对象生效，
                // 但样式角色只给主体 —— 否则给体设个 answer 样式，八个顶点会一起变红。
                obj.Visible = dto.Visible ?? true;
                if (i == objects.Count - 1) obj.Style = dto.Style;

                if (scene.Find(obj.Id) is not null)
                {
                    diagnostics.Add(Error(obj.Id, "schema.duplicate", obj.Id));
                    continue;
                }

                scene.Add(obj);
            }
        }

        scene.Camera = ReadCamera(file.View);

        var solve = scene.Solve();
        diagnostics.AddRange(solve.Diagnostics);

        return new ProblemLoadResult { Scene = scene, Diagnostics = diagnostics };
    }

    public static ProblemFile Save(Scene scene, ProblemFile? template = null)
    {
        var file = template ?? new ProblemFile();

        file.Objects = scene.Objects.Select(ToDto).ToList();

        if (scene.Camera is { } camera)
        {
            file.View ??= new ProblemViewDto();
            file.View.Camera = [camera.AzimuthDeg, camera.ElevationDeg, camera.Distance];
            if (camera.Orthographic) file.View.Orthographic = true;
            if (camera.Target != Vec3.Zero)
                file.View.CameraTarget = [camera.Target.X, camera.Target.Y, camera.Target.Z];
        }
        else if (file.View is null && scene.IsSolved)
        {
            var bounds = scene.ComputeBounds();
            if (double.IsFinite(bounds.MinX))
                file.View = new ProblemViewDto
                {
                    Bounds = [bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY],
                };
        }

        return file;
    }

    // —————————————————————————— 读 ——————————————————————————

    private static IReadOnlyList<GeoObject> MapObject(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        switch (dto.Type?.Trim().ToLowerInvariant())
        {
            case "point":
                return Single(MapPoint(dto, diagnostics));

            case "segment" or "ray" or "line":
                return Single(MapLine(dto, diagnostics));

            case "circle":
                return Single(MapCircle(dto, diagnostics));

            case "polygon":
                return Single(MapPolygon(dto, diagnostics));

            case "solid":
                return MapSolid(dto, diagnostics);

            case "polyhedron":
                return MapPolyhedron(dto, diagnostics);

            case "section":
                return Single(MapSection(dto, diagnostics));

            case "dihedral":
                return Single(MapDihedral(dto, diagnostics));

            case "angle":
                return Single(MapAngle(dto, diagnostics));

            case "three-views":
                return Single(MapThreeViews(dto, diagnostics));

            case "sphere":
                return Single(MapSphere(dto, diagnostics));

            case null or "":
                diagnostics.Add(Error(dto.Id, "schema.type.missing"));
                return [];

            default:
                diagnostics.Add(Error(dto.Id, "schema.type.unknown", dto.Type));
                return [];
        }
    }

    private static IReadOnlyList<GeoObject> Single(GeoObject? obj)
        => obj is null ? [] : [obj];

    private static GeoLine? MapLine(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.A) || string.IsNullOrWhiteSpace(dto.B))
        {
            diagnostics.Add(Error(dto.Id, "schema.line.endpoints"));
            return null;
        }

        var kind = dto.Type!.Trim().ToLowerInvariant() switch
        {
            "segment" => LineKind.Segment,
            "ray" => LineKind.Ray,
            _ => LineKind.Infinite,
        };

        return new GeoLine { Id = dto.Id, AId = dto.A, BId = dto.B, Kind = kind, Label = dto.Label };
    }

    private static GeoCircle? MapCircle(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.Center))
        {
            diagnostics.Add(Error(dto.Id, "schema.circle.center"));
            return null;
        }

        if (dto.Through is null or { Length: 0 } && dto.Radius is null)
            diagnostics.Add(Warning(dto.Id, "schema.circle.radius"));

        return dto.Through is { Length: > 0 }
            ? GeoCircle.Through(dto.Id, dto.Center, dto.Through, dto.Label)
            : GeoCircle.Fixed(dto.Id, dto.Center, dto.Radius ?? 0, dto.Label);
    }

    private static GeoPolygon? MapPolygon(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        var vertices = dto.Vertices ?? [];
        if (vertices.Length < 3)
        {
            diagnostics.Add(Error(dto.Id, "schema.polygon.vertices"));
            return null;
        }

        return new GeoPolygon
        {
            Id = dto.Id,
            VertexIds = vertices,
            Filled = dto.Filled ?? true,
            Label = dto.Label,
        };
    }

    /// <summary>
    /// 参数化基本体。这是"3 秒拉一个正方体"的实现 ——
    /// 老师（和 agent）只写一行 box 加三个尺寸，顶点、面、课本命名全部自动生成。
    /// </summary>
    private static IReadOnlyList<GeoObject> MapSolid(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        var origin = ToVec3(dto.Origin);
        var parameters = SolidPresets.Normalize(dto.Solid, dto.Sides, dto.Prefix, dto.Apex);

        if (!parameters.IsKnown)
        {
            diagnostics.Add(parameters.Solid.Length == 0
                ? Error(dto.Id, "schema.solid.missing")
                : Error(dto.Id, "schema.solid.unknown", dto.Solid));
            return [];
        }

        var size = dto.Size ?? [];
        var width = Positive(size.ElementAtOrDefault(0), 4);

        var expansion = parameters.Solid switch
        {
            "prism" => SolidPresets.Prism(dto.Id, parameters.Sides,
                Positive(dto.Radius ?? 0, 2), Positive(dto.Height ?? 0, 3), origin, parameters.Letters),

            "pyramid" => SolidPresets.Pyramid(dto.Id, parameters.Sides,
                Positive(dto.Radius ?? 0, 2), Positive(dto.Height ?? 0, 3), origin,
                parameters.Apex, parameters.Letters),

            _ => SolidPresets.Box(dto.Id,
                width,
                Positive(size.ElementAtOrDefault(1), width),
                Positive(size.ElementAtOrDefault(2), width),
                origin, parameters.Letters),
        };

        ApplySolidOptions(expansion.Solid, dto);

        var objects = new List<GeoObject>(expansion.Vertices.Count + 1);
        objects.AddRange(expansion.Vertices);
        objects.Add(expansion.Solid);
        return objects;
    }

    /// <summary>
    /// 显式顶点 + 面的多面体。用于"正方体切掉一个角"这类自定义体，
    /// 也就是参数化基本体覆盖不到的那部分。
    /// </summary>
    private static IReadOnlyList<GeoObject> MapPolyhedron(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        var vertices = dto.Vertices ?? [];
        var faces = dto.Faces ?? [];

        if (vertices.Length < 4)
        {
            diagnostics.Add(Error(dto.Id, "schema.polyhedron.vertices"));
            return [];
        }

        if (faces.Length < 4)
        {
            diagnostics.Add(Error(dto.Id, "schema.polyhedron.faces"));
            return [];
        }

        foreach (var face in faces)
        {
            if (face.Length < 3)
            {
                diagnostics.Add(Error(dto.Id, "schema.polyhedron.face"));
                return [];
            }

            foreach (var index in face)
                if (index < 0 || index >= vertices.Length)
                {
                    diagnostics.Add(Error(dto.Id, "schema.polyhedron.index", index));
                    return [];
                }
        }

        var solid = GeoPolyhedron.Of(dto.Id, vertices, faces, dto.Label);
        ApplySolidOptions(solid, dto);
        return [solid];
    }

    private static void ApplySolidOptions(GeoPolyhedron solid, ProblemObjectDto dto)
    {
        if (dto.HighlightFaces is { Length: > 0 }) solid.HighlightFaces = dto.HighlightFaces;
        if (dto.Filled is { } filled) solid.Filled = filled;
        if (dto.ShowHiddenEdges is { } showHidden) solid.ShowHiddenEdges = showHidden;
    }

    private static GeoSection? MapSection(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.Of))
        {
            diagnostics.Add(Error(dto.Id, "schema.section.of"));
            return null;
        }

        if (dto.Plane is not { Length: >= 3 })
        {
            diagnostics.Add(Error(dto.Id, "schema.section.plane"));
            return null;
        }

        return new GeoSection
        {
            Id = dto.Id,
            SolidId = dto.Of,
            PlanePointIds = dto.Plane,
            Filled = dto.Filled ?? true,
            Label = dto.Label,
        };
    }

    private static GeoDihedral? MapDihedral(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.Of))
        {
            diagnostics.Add(Error(dto.Id, "schema.dihedral.of"));
            return null;
        }

        if (dto.FaceA is not { } faceA || dto.FaceB is not { } faceB)
        {
            diagnostics.Add(Error(dto.Id, "schema.dihedral.faces"));
            return null;
        }

        return new GeoDihedral
        {
            Id = dto.Id,
            SolidId = dto.Of,
            FaceA = faceA,
            FaceB = faceB,
            RadiusRatio = dto.RadiusRatio is > 0 ? dto.RadiusRatio.Value : 0.35,
            ShowValue = dto.ShowValue ?? true,
            Label = dto.Label,
        };
    }

    private static GeoSphere? MapSphere(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.Of))
        {
            diagnostics.Add(Error(dto.Id, "schema.sphere.of", "球需要 of：多面体 Id"));
            return null;
        }

        var kind = dto.Sphere?.Trim().ToLowerInvariant() switch
        {
            null or "" or "circumscribed" or "circum" or "outer" => SphereKind.Circumscribed,
            "inscribed" or "inscribe" or "inner" or "in" => SphereKind.Inscribed,
            _ => (SphereKind?)null,
        };

        if (kind is null)
        {
            diagnostics.Add(Error(dto.Id, "schema.sphere.kind",
                $"球的类型无法识别：'{dto.Sphere}'（应为 circumscribed 或 inscribed）"));
            return null;
        }

        return new GeoSphere
        {
            Id = dto.Id,
            SolidId = dto.Of,
            Kind = kind.Value,
            Meridians = Math.Clamp(dto.Meridians ?? 3, 0, 12),
            ShowCenter = dto.ShowCenter ?? true,
            ShowRadius = dto.ShowRadius ?? true,
            ShowValue = dto.ShowValue ?? true,
            Label = dto.Label,
        };
    }

    private static GeoAngle? MapAngle(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.Vertex)
            || string.IsNullOrWhiteSpace(dto.From)
            || string.IsNullOrWhiteSpace(dto.To))
        {
            diagnostics.Add(Error(dto.Id, "schema.angle.points",
                "角需要 vertex / from / to 三个点 Id"));
            return null;
        }

        return new GeoAngle
        {
            Id = dto.Id,
            VertexId = dto.Vertex,
            FromId = dto.From,
            ToId = dto.To,
            RadiusRatio = dto.RadiusRatio is > 0 ? dto.RadiusRatio.Value : 0.28,
            ShowValue = dto.ShowValue ?? true,
            Label = dto.Label,
        };
    }

    private static GeoThreeViews? MapThreeViews(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        // 允许两种写法：of 给一个体，solids 给一组。前者是绝大多数情况。
        var solids = dto.Solids is { Length: > 0 }
            ? dto.Solids
            : string.IsNullOrWhiteSpace(dto.Of) ? [] : new[] { dto.Of };

        if (solids.Length == 0)
        {
            diagnostics.Add(Error(dto.Id, "schema.threeViews.solids",
                "三视图需要 of（一个体）或 solids（一组体）"));
            return null;
        }

        return new GeoThreeViews
        {
            Id = dto.Id,
            SolidIds = solids,
            Gap = dto.Gap is > 0 ? dto.Gap.Value : 0.35,
            ShowNames = dto.ShowNames ?? true,
            ShowHiddenEdges = dto.ShowHiddenEdges ?? true,
            // 复用 origin 字段做版面偏移：三视图的 origin 是二维的，实体的 origin 是三维的，
            // 两者不会同时出现在一个对象上，所以不冲突。
            Offset = dto.Origin is { Length: >= 2 } origin
                ? new Vec2(origin[0], origin[1])
                : Vec2.Zero,
            Label = dto.Label,
        };
    }

    private static GeoPoint? MapPoint(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        var kind = ParsePointKind(dto.PointKind) ?? InferPointKind(dto);

        if (kind is null)
        {
            diagnostics.Add(Error(dto.Id, "schema.pointkind"));
            return null;
        }

        GeoPoint point;

        switch (kind)
        {
            case PointKind.Free:
                if (dto.At is null)
                    diagnostics.Add(Warning(dto.Id, "schema.at"));
                point = GeoPoint.Free(dto.Id, ToVec3(dto.At), dto.Label);
                break;

            case PointKind.OnPath:
                if (string.IsNullOrWhiteSpace(dto.Path))
                {
                    diagnostics.Add(Error(dto.Id, "schema.path"));
                    return null;
                }
                point = GeoPoint.OnPath(dto.Id, dto.Path, dto.Param ?? 0.5, dto.Label);
                break;

            case PointKind.Derived:
            {
                var derived = ParseDerivedKind(dto.Derived);
                if (derived is null)
                {
                    diagnostics.Add(Error(dto.Id, "schema.derived", dto.Derived));
                    return null;
                }

                if (dto.Args is null or { Length: 0 })
                {
                    diagnostics.Add(Error(dto.Id, "schema.point.args"));
                    return null;
                }

                point = GeoPoint.Derived(dto.Id, derived.Value, dto.Args);
                point.Label = dto.Label;
                break;
            }

            default:
                return null;
        }

        if (dto.Driver is { } driver)
        {
            point.Driver = new Driver
            {
                Min = driver.Min,
                Max = driver.Max,
                Value = driver.Value,
                Speed = driver.Speed,
                PingPong = driver.PingPong,
            };

            // 驱动器是参数的权威来源，初始值要同步过去，否则首帧会跳。
            point.Param = MathUtil.Clamp(driver.Value, driver.Min, driver.Max);
        }

        return point;
    }

    private static Camera? ReadCamera(ProblemViewDto? view)
    {
        if (view?.Camera is not { Length: >= 2 } values) return null;

        return new Camera
        {
            AzimuthDeg = values[0],
            ElevationDeg = values[1],
            Distance = values.Length > 2 && values[2] > 0 ? values[2] : 24,
            Orthographic = view.Orthographic ?? false,
            Target = ToVec3(view.CameraTarget),
        };
    }

    // —————————————————————————— 写 ——————————————————————————

    private static ProblemObjectDto ToDto(GeoObject obj)
    {
        var dto = new ProblemObjectDto
        {
            Id = obj.Id,
            Label = obj.Label,
            Style = obj.Style,
            Visible = obj.Visible ? null : false,
        };

        switch (obj)
        {
            case GeoPoint point:
                dto.Type = "point";
                dto.PointKind = point.Kind switch
                {
                    PointKind.Free => "free",
                    PointKind.OnPath => "path",
                    _ => "derived",
                };

                switch (point.Kind)
                {
                    case PointKind.Free:
                        dto.At = [point.FreePosition.X, point.FreePosition.Y, point.FreePosition.Z];
                        break;
                    case PointKind.OnPath:
                        dto.Path = point.PathId;
                        dto.Param = point.Param;
                        break;
                    case PointKind.Derived:
                        dto.Derived = ToCamel(point.DerivedKind.ToString());
                        dto.Args = [.. point.Args];
                        break;
                }

                if (point.Driver is { } driver)
                    dto.Driver = new ProblemDriverDto
                    {
                        Min = driver.Min,
                        Max = driver.Max,
                        Value = driver.Value,
                        Speed = driver.Speed,
                        PingPong = driver.PingPong,
                    };
                break;

            case GeoLine line:
                dto.Type = line.Kind switch
                {
                    LineKind.Segment => "segment",
                    LineKind.Ray => "ray",
                    _ => "line",
                };
                dto.A = line.AId;
                dto.B = line.BId;
                break;

            case GeoCircle circle:
                dto.Type = "circle";
                dto.Center = circle.CenterId;
                if (circle.ThroughId is { Length: > 0 }) dto.Through = circle.ThroughId;
                else dto.Radius = circle.Radius;
                break;

            case GeoPolygon polygon:
                dto.Type = "polygon";
                dto.Vertices = [.. polygon.VertexIds];
                dto.Filled = polygon.Filled ? null : false;
                break;

            // 存盘统一写成显式顶点 + 面，而不是还原成 box/prism/pyramid。
            // 理由：显式形式是无损的（参数化形式表达不了"正方体切掉一个角"这类改动），
            // 而"读得进参数化、写得出显式"这个不对称方向对用户是安全的。
            case GeoPolyhedron polyhedron:
                dto.Type = "polyhedron";
                dto.Vertices = [.. polyhedron.VertexIds];
                dto.Faces = [.. polyhedron.Faces.Select(f => (int[])f.Clone())];
                if (polyhedron.HighlightFaces.Count > 0) dto.HighlightFaces = [.. polyhedron.HighlightFaces];
                if (!polyhedron.Filled) dto.Filled = false;
                if (!polyhedron.ShowHiddenEdges) dto.ShowHiddenEdges = false;
                break;

            case GeoSection section:
                dto.Type = "section";
                dto.Of = section.SolidId;
                dto.Plane = [.. section.PlanePointIds];
                if (!section.Filled) dto.Filled = false;
                break;

            case GeoDihedral dihedral:
                dto.Type = "dihedral";
                dto.Of = dihedral.SolidId;
                dto.FaceA = dihedral.FaceA;
                dto.FaceB = dihedral.FaceB;
                if (Math.Abs(dihedral.RadiusRatio - 0.35) > 1e-9) dto.RadiusRatio = dihedral.RadiusRatio;
                if (!dihedral.ShowValue) dto.ShowValue = false;
                break;

            case GeoAngle angle:
                dto.Type = "angle";
                dto.Vertex = angle.VertexId;
                dto.From = angle.FromId;
                dto.To = angle.ToId;
                if (Math.Abs(angle.RadiusRatio - 0.28) > 1e-9) dto.RadiusRatio = angle.RadiusRatio;
                if (!angle.ShowValue) dto.ShowValue = false;
                break;

            case GeoThreeViews threeViews:
                dto.Type = "three-views";
                dto.Solids = [.. threeViews.SolidIds];
                if (Math.Abs(threeViews.Gap - 0.35) > 1e-9) dto.Gap = threeViews.Gap;
                if (!threeViews.ShowNames) dto.ShowNames = false;
                if (!threeViews.ShowHiddenEdges) dto.ShowHiddenEdges = false;
                if (threeViews.Offset != Vec2.Zero) dto.Origin = [threeViews.Offset.X, threeViews.Offset.Y];
                break;

            case GeoSphere sphere:
                dto.Type = "sphere";
                dto.Of = sphere.SolidId;
                dto.Sphere = sphere.Kind == SphereKind.Circumscribed ? "circumscribed" : "inscribed";
                if (sphere.Meridians != 3) dto.Meridians = sphere.Meridians;
                if (!sphere.ShowCenter) dto.ShowCenter = false;
                if (!sphere.ShowRadius) dto.ShowRadius = false;
                if (!sphere.ShowValue) dto.ShowValue = false;
                break;
        }

        return dto;
    }

    // —————————————————————————— 校验 ——————————————————————————

    /// <summary>
    /// 只做结构校验，不做几何求解 —— 这是给 MCP 的 validate_problem 工具用的，
    /// 让 agent 在写文件之前就知道自己写错了什么。
    /// </summary>
    public static IReadOnlyList<SolveDiagnostic> Validate(ProblemFile file)
    {
        var diagnostics = new List<SolveDiagnostic>();

        if (file.Schema <= 0)
            diagnostics.Add(Error(null, "schema.version.invalid", file.Schema));
        else if (file.Schema > 1)
            diagnostics.Add(Error(null, "schema.version.tooNew", file.Schema, 1));

        if (file.Objects.Count == 0)
            diagnostics.Add(Error(null, "schema.empty"));

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dto in file.Objects)
        {
            if (string.IsNullOrWhiteSpace(dto.Id)) continue;

            if (!ids.Add(dto.Id))
                diagnostics.Add(Error(dto.Id, "schema.duplicate", dto.Id));

            if (dto.Id.Any(char.IsWhiteSpace))
                diagnostics.Add(Error(dto.Id, "schema.id.whitespace"));
        }

        // 参数化基本体在展开时会生成一批顶点 Id。校验必须先知道它们，
        // 否则文件里写 "cube.A" 会被误判成"引用了不存在的对象" —— 而那是完全合法的写法。
        foreach (var dto in file.Objects)
        {
            if (string.IsNullOrWhiteSpace(dto.Id)) continue;
            if (dto.Type?.Trim().ToLowerInvariant() != "solid") continue;

            var parameters = SolidPresets.Normalize(dto.Solid, dto.Sides, dto.Prefix, dto.Apex);
            if (!parameters.IsKnown) continue;

            foreach (var vertexId in SolidPresets.PredictVertexIds(dto.Id, parameters))
                ids.Add(vertexId);
        }

        // 引用完整性：所有被引用的 id 必须存在。
        foreach (var dto in file.Objects)
        {
            foreach (var reference in References(dto))
                if (!string.IsNullOrWhiteSpace(reference) && !ids.Contains(reference))
                    diagnostics.Add(Error(dto.Id, "ref.missing", reference));
        }

        if (file.Ask?.Target is { Length: > 0 } target && !ids.Contains(target))
            diagnostics.Add(Error(null, "ref.askTarget", target));

        // 结构性下限检查：这几类错误 agent 最容易犯，而且如果不在这里报，
        // 求解阶段会以"图形凭空消失"的形式出现，很难定位。
        foreach (var dto in file.Objects)
            CheckStructure(dto, diagnostics);

        return diagnostics;
    }

    private static void CheckStructure(ProblemObjectDto dto, List<SolveDiagnostic> diagnostics)
    {
        switch (dto.Type?.Trim().ToLowerInvariant())
        {
            case "polygon" when (dto.Vertices?.Length ?? 0) < 3:
                diagnostics.Add(Error(dto.Id, "schema.polygon.vertices"));
                break;

            case "solid" when string.IsNullOrWhiteSpace(dto.Solid):
                diagnostics.Add(Error(dto.Id, "schema.solid.missing"));
                break;

            case "polyhedron":
                if ((dto.Vertices?.Length ?? 0) < 4)
                    diagnostics.Add(Error(dto.Id, "schema.polyhedron.vertices"));
                if ((dto.Faces?.Length ?? 0) < 4)
                    diagnostics.Add(Error(dto.Id, "schema.polyhedron.faces"));
                break;

            case "section":
                if (string.IsNullOrWhiteSpace(dto.Of))
                    diagnostics.Add(Error(dto.Id, "schema.section.of"));
                if ((dto.Plane?.Length ?? 0) < 3)
                    diagnostics.Add(Error(dto.Id, "schema.section.plane"));
                break;

            case "dihedral":
                if (string.IsNullOrWhiteSpace(dto.Of))
                    diagnostics.Add(Error(dto.Id, "schema.dihedral.of", "二面角需要 of：多面体 Id"));
                if (dto.FaceA is null || dto.FaceB is null)
                    diagnostics.Add(Error(dto.Id, "schema.dihedral.faces", "二面角需要 faceA 和 faceB：两个面的下标"));
                break;

            case "angle":
                if (string.IsNullOrWhiteSpace(dto.Vertex)
                    || string.IsNullOrWhiteSpace(dto.From)
                    || string.IsNullOrWhiteSpace(dto.To))
                    diagnostics.Add(Error(dto.Id, "schema.angle.points", "角需要 vertex / from / to 三个点 Id"));
                break;

            case "three-views":
                if ((dto.Solids?.Length ?? 0) == 0 && string.IsNullOrWhiteSpace(dto.Of))
                    diagnostics.Add(Error(dto.Id, "schema.threeViews.solids",
                        "三视图需要 of（一个体）或 solids（一组体）"));
                break;

            case "sphere":
                if (string.IsNullOrWhiteSpace(dto.Of))
                    diagnostics.Add(Error(dto.Id, "schema.sphere.of", "球需要 of：多面体 Id"));
                break;
        }
    }

    private static IEnumerable<string?> References(ProblemObjectDto dto)
    {
        yield return dto.A;
        yield return dto.B;
        yield return dto.Center;
        yield return dto.Through;
        yield return dto.Path;
        yield return dto.Of;
        yield return dto.Vertex;
        yield return dto.From;
        yield return dto.To;

        if (dto.Solids is not null)
            foreach (var v in dto.Solids) yield return v;

        if (dto.Plane is not null)
            foreach (var v in dto.Plane) yield return v;

        if (dto.Vertices is not null)
            foreach (var v in dto.Vertices) yield return v;

        // 派生点的 args 里混着 id 和数值。数值不是引用，混进来会误报，
        // 所以统一用 GeoRef 判断 —— 和依赖解析共用同一份规则。
        if (dto.Args is not null)
            foreach (var arg in dto.Args)
                if (GeoRef.LooksLikeId(arg))
                    yield return arg;
    }

    // —————————————————————————— 解析辅助 ——————————————————————————

    /// <summary>
    /// 枚举解析全部手写，不用 Enum.TryParse：一是裁剪/AOT 下反射路径不可靠，
    /// 二是这里要接受 agent 可能写的别名，手写才控得住。
    /// </summary>
    private static PointKind? ParsePointKind(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "free" => PointKind.Free,
        "path" or "onpath" or "on_path" => PointKind.OnPath,
        "derived" => PointKind.Derived,
        _ => null,
    };

    private static PointKind? InferPointKind(ProblemObjectDto dto)
    {
        if (dto.Derived is { Length: > 0 }) return PointKind.Derived;
        if (dto.Path is { Length: > 0 }) return PointKind.OnPath;
        if (dto.At is not null) return PointKind.Free;
        return null;
    }

    private static DerivedKind? ParseDerivedKind(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "midpoint" or "mid" => DerivedKind.Midpoint,
        "centroid" => DerivedKind.Centroid,
        "intersection" or "intersect" => DerivedKind.Intersection,
        "foot" or "perpendicularfoot" or "projection" => DerivedKind.Foot,
        "reflection" or "reflect" or "mirror" => DerivedKind.Reflection,
        "rotate" or "rotation" => DerivedKind.Rotate,
        "scale" or "homothety" or "dilate" => DerivedKind.Scale,
        "translate" or "translation" => DerivedKind.Translate,
        "onsegment" or "on_segment" or "ratio" => DerivedKind.OnSegment,
        _ => null,
    };

    private static Vec3 ToVec3(double[]? values) => values switch
    {
        null => Vec3.Zero,
        { Length: >= 3 } => new Vec3(values[0], values[1], values[2]),
        { Length: 2 } => new Vec3(values[0], values[1], 0),
        { Length: 1 } => new Vec3(values[0], 0, 0),
        _ => Vec3.Zero,
    };

    private static double Positive(double value, double fallback) => value > 0 ? value : fallback;

    /// <summary>派生方式从 "Midpoint" 输出成 "midpoint"。</summary>
    private static string ToCamel(string name)
        => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static SolveDiagnostic Error(string? id, string code, params object?[] args)
        => SolveDiagnostic.Error(id, code, args);

    private static SolveDiagnostic Warning(string? id, string code, params object?[] args)
        => SolveDiagnostic.Warning(id, code, args);
}
