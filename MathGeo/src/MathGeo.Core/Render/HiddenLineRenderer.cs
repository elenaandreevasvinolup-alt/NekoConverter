namespace MathGeo.Core;

internal sealed record SolidGeometry(GeoPolyhedron Source, Vec3[] Vertices);

/// <summary>
/// 需要参与遮挡判定的辅助曲线（球面线框、展开图的折痕……）。
///
/// 它们不是多面体的棱，但同样会被体挡住，所以必须走同一个深度缓冲 ——
/// 否则球被立方体挡住的那些部分会被画成实线，图立刻就不对了。
/// </summary>
internal sealed record OccludedCurve(IReadOnlyList<Vec3> Points, string Stroke, double Width);

/// <summary>
/// 立体几何的隐藏线渲染器。
///
/// 为什么自写而不用 GPU：教学图要的不是光照和材质，是
///   ① 可见棱实线、被遮挡棱虚线，
///   ② 面半透明填充，
///   ③ 顶点标签不叠字、随旋转保持可读。
/// 前两条是**非真实感渲染**问题，GPU 图形管线天生不擅长；第三条是屏幕空间排版问题，
/// GPU 完全不管。而且自写之后五个平台行为完全一致。
///
/// 判定策略是两级的，这个组合是刻意的：
///   主判据 —— 一条棱只要有一个邻接面朝向相机，它在这条棱上就是"局部可见"的。
///             对凸体这是**精确**的，而且完全不依赖浮点比较，因此绝不会闪烁。
///   复核   —— 只有"局部可见"的棱才需要用深度缓冲去查"是不是被别的东西挡住了"
///             （非凸体、多个体互相遮挡）。这一步用大容差，把轮廓附近的数值噪声排除掉。
///
/// 只用深度缓冲做单一判据是行不通的：棱正好落在轮廓上时，缓冲里存的就是与它共面的那个面，
/// 深度差约等于 0，浮点噪声会让判定逐帧翻转 —— 屏幕上就是虚线在抖。
/// </summary>
internal static class HiddenLineRenderer
{
    public static void Build(
        IReadOnlyList<SolidGeometry> solids,
        IReadOnlyList<OccludedCurve> curves,
        CameraView view, Theme theme, List<Shape> output)
        => new Session(solids, curves, view, theme, output).Run();

    private sealed class Session
    {
        /// <summary>深度缓冲最大边长。教学图的棱不多，这个分辨率远超需要。</summary>
        private const int MaxBufferDimension = 384;

        /// <summary>复核遮挡用的容差，取场景尺寸的相对值。要远大于数值噪声、远小于真实遮挡厚度。</summary>
        private const double OcclusionBiasRatio = 1.5e-2;

        /// <summary>少于这么多个连续采样点的分类会被吞掉，作为抖动抑制的第二道保险。</summary>
        private const int MinimumRunSamples = 3;

        /// <summary>退化长度的相对阈值。见 _degenerateLength 的说明。</summary>
        private const double DegenerateRatio = 1e-6;

        private readonly IReadOnlyList<SolidGeometry> _solids;
        private readonly IReadOnlyList<OccludedCurve> _curves;
        private readonly CameraView _view;
        private readonly Theme _theme;
        private readonly List<Shape> _output;

        /// <summary>
        /// 是否需要深度缓冲。
        ///
        /// 单个凸体**不需要**：棱的可见性由邻接面朝向就能精确判定，没有任何别的东西能挡住它。
        /// 而建缓冲是这里最贵的一步（栅格化几十万个像素），每次拖拽都要重来一遍。
        /// 跳过它之后，常见场景（一个正方体/棱锥/棱柱）从几十毫秒降到不足一毫秒。
        ///
        /// 需要缓冲的三种情况：多个体互相遮挡、非凸体自己挡自己、有辅助曲线（球面线框）。
        /// </summary>
        private bool _useDepthBuffer;

        private ProjectedPoint[][] _projected = [];
        private bool[][] _faceFrontFacing = [];
        private DepthBuffer _buffer = null!;
        private double _occlusionBias;

        /// <summary>
        /// 退化长度阈值，按场景尺度取相对值。
        ///
        /// 这里**不能**用 1e-9 这类绝对阈值：正交投影里 cos(90°) 是 6.1e-17 而不是 0，
        /// 于是"沿视线方向、本应投影成一个点"的棱，屏幕长度会是 1e-16 量级 ——
        /// 它有时刚好越过绝对阈值，被当成真实棱画出来，变成一条零长度的虚线。
        /// 相对阈值才不受坐标系尺度和角度影响。
        /// </summary>
        private double _degenerateLength;

        public Session(
            IReadOnlyList<SolidGeometry> solids, IReadOnlyList<OccludedCurve> curves,
            CameraView view, Theme theme, List<Shape> output)
        {
            _solids = solids;
            _curves = curves;
            _view = view;
            _theme = theme;
            _output = output;
        }

        public void Run()
        {
            _projected = Project();

            var diagonal = SceneDiagonal();
            _occlusionBias = diagonal * OcclusionBiasRatio;
            _degenerateLength = diagonal * DegenerateRatio;

            ComputeFaceOrientations();

            _useDepthBuffer = _curves.Count > 0
                || _solids.Count > 1
                || _solids.Any(solid => !IsConvex(solid.Vertices, solid.Source.Faces));

            if (_useDepthBuffer)
            {
                _buffer = new DepthBuffer(ProjectedBounds(), MaxBufferDimension);

                // 所有面都灌进去，包括背面 —— 复核遮挡时需要"最近表面"的完整信息。
                for (var s = 0; s < _solids.Count; s++)
                    foreach (var face in _solids[s].Source.Faces)
                    {
                        if (face.Length < 3) continue;
                        for (var i = 1; i + 1 < face.Length; i++)
                            _buffer.Rasterize(_projected[s][face[0]], _projected[s][face[i]], _projected[s][face[i + 1]]);
                    }
            }

            DrawFaces();

            for (var s = 0; s < _solids.Count; s++) DrawEdges(s);

            // 辅助曲线最后处理：它们叠在体上，被挡住的部分同样要变成虚线。
            foreach (var curve in _curves) ClassifyCurve(curve);
        }

        /// <summary>
        /// 把一条三维折线整体采样、整体判遮挡、再合并成实线/虚线段。
        ///
        /// 和棱的区别：棱的两端在体上，可以先用"邻接面朝向"做零噪声的主判据；
        /// 辅助曲线悬在空中，只能老老实实逐点比深度。所以它用的是和棱的"复核"同一套逻辑。
        /// </summary>
        private void ClassifyCurve(OccludedCurve curve)
        {
            var world = curve.Points;
            if (world.Count < 2) return;

            var samples = new List<(Vec2 Screen, double Depth)>();

            for (var i = 0; i + 1 < world.Count; i++)
            {
                var fromScreen = _view.ProjectWithDepth(world[i], out var fromDepth);
                var toScreen = _view.ProjectWithDepth(world[i + 1], out var toDepth);

                var length = Vec2.Distance(fromScreen, toScreen);
                if (length < _degenerateLength) continue;

                var steps = Math.Clamp(
                    (int)Math.Ceiling(length * _buffer.PixelsPerUnit / 2.0), 1, 256);

                // 相邻段共享端点，所以除第一段外都跳过 s = 0。
                for (var s = samples.Count == 0 ? 0 : 1; s <= steps; s++)
                {
                    var t = (double)s / steps;
                    samples.Add((
                        Vec2.Lerp(fromScreen, toScreen, t),
                        fromDepth + (toDepth - fromDepth) * t));
                }
            }

            if (samples.Count < 2) return;

            var hidden = new bool[samples.Count];
            for (var i = 0; i < samples.Count; i++)
                hidden[i] = samples[i].Depth > _buffer.Sample(samples[i].Screen) + _occlusionBias;

            SuppressFlicker(hidden);

            var index = 0;
            while (index < samples.Count)
            {
                var start = index;
                var state = hidden[index];
                while (index < samples.Count && hidden[index] == state) index++;

                // 相邻段共享端点，所以末端用 index 而不是 index-1。
                var end = Math.Min(index, samples.Count - 1);
                var a = samples[start].Screen;
                var b = samples[end].Screen;

                if (Vec2.Distance(a, b) < _degenerateLength) continue;

                _output.Add(state
                    ? new LineShape(a, b, _theme.HelperStroke, _theme.HelperWidth, _theme.HiddenEdgeDash)
                    : new LineShape(a, b, curve.Stroke, curve.Width));
            }
        }

        // ————————————————————————— 投影与朝向 —————————————————————————

        private ProjectedPoint[][] Project()
        {
            var result = new ProjectedPoint[_solids.Count][];

            for (var s = 0; s < _solids.Count; s++)
            {
                var vertices = _solids[s].Vertices;
                var points = new ProjectedPoint[vertices.Length];

                for (var i = 0; i < vertices.Length; i++)
                    points[i] = new ProjectedPoint(_view.ProjectWithDepth(vertices[i], out var depth), depth);

                result[s] = points;
            }

            return result;
        }

        private void ComputeFaceOrientations()
        {
            _faceFrontFacing = new bool[_solids.Count][];

            for (var s = 0; s < _solids.Count; s++)
            {
                var vertices = _solids[s].Vertices;
                var faces = _solids[s].Source.Faces;
                var flags = new bool[faces.Count];

                for (var f = 0; f < faces.Count; f++)
                {
                    var face = faces[f];
                    if (face.Length < 3) continue;

                    var a = vertices[face[0]];
                    var b = vertices[face[1]];
                    var c = vertices[face[2]];

                    // 面的绕向约定是"从体外看逆时针"，所以法向量朝外。
                    var normal = Vec3.Cross(b - a, c - a);
                    flags[f] = Vec3.Dot(normal, _view.Eye - a) > 0;
                }

                _faceFrontFacing[s] = flags;
            }
        }

        // ————————————————————————————— 面 —————————————————————————————

        private void DrawFaces()
        {
            var faces = new List<(double Depth, PolygonShape Shape)>();

            for (var s = 0; s < _solids.Count; s++)
            {
                var solid = _solids[s].Source;
                if (!solid.Filled) continue;

                var points = _projected[s];
                var highlighted = new HashSet<int>(solid.HighlightFaces);

                for (var f = 0; f < solid.Faces.Count; f++)
                {
                    var face = solid.Faces[f];
                    if (face.Length < 3) continue;

                    var polygon = new List<Vec2>(face.Length);
                    var depth = 0.0;

                    foreach (var index in face)
                    {
                        polygon.Add(points[index].Screen);
                        depth += points[index].Depth;
                    }
                    depth /= face.Length;

                    var isHighlighted = highlighted.Contains(f);

                    faces.Add((depth, new PolygonShape(
                        polygon,
                        isHighlighted ? _theme.FaceFillHighlight : _theme.PolygonFill,
                        isHighlighted ? _theme.HighlightStroke : _theme.FaceStroke,
                        _theme.LineWidth * 0.7)));
                }
            }

            // 远的先画。半透明面靠这个顺序叠出正确的观感。
            faces.Sort((x, y) => y.Depth.CompareTo(x.Depth));
            foreach (var (_, shape) in faces) _output.Add(shape);
        }

        // ————————————————————————————— 棱 —————————————————————————————

        private void DrawEdges(int solidIndex)
        {
            var solid = _solids[solidIndex].Source;
            var points = _projected[solidIndex];
            var vertices = _solids[solidIndex].Vertices;
            var frontFacing = _faceFrontFacing[solidIndex];

            // 一条棱最多被两个面共享，两边都要看：只要有一个面朝向相机，棱就局部可见。
            var adjacency = new Dictionary<(int, int), (bool AnyFrontFacing, bool AnyFace)>();
            var ordered = new List<(int A, int B, bool FrontFacing)>();

            for (var f = 0; f < solid.Faces.Count; f++)
            {
                var face = solid.Faces[f];
                for (var i = 0; i < face.Length; i++)
                {
                    var a = face[i];
                    var b = face[(i + 1) % face.Length];
                    var key = a < b ? (a, b) : (b, a);

                    if (adjacency.TryGetValue(key, out var existing))
                    {
                        adjacency[key] = (existing.AnyFrontFacing || frontFacing[f], true);
                    }
                    else
                    {
                        adjacency[key] = (frontFacing[f], true);
                        ordered.Add((a, b, frontFacing[f]));
                    }
                }
            }

            foreach (var (a, b, _) in ordered)
            {
                var key = a < b ? (a, b) : (b, a);
                var locallyVisible = adjacency[key].AnyFrontFacing;

                ClassifyEdge(points[a], points[b], vertices[a], vertices[b],
                    locallyVisible, solid.ShowHiddenEdges, _theme, _output);
            }
        }

        /// <summary>
        /// 把一条棱沿屏幕方向采样，判断遮挡，再合并成实线/虚线段。
        ///
        /// locallyVisible 为 false 时（所有邻接面都背对相机）不需要查缓冲 ——
        /// 对闭合体来说这条棱必然被自己挡住，直接是虚线。这是凸体上零噪声的那一半。
        /// </summary>
        private void ClassifyEdge(
            ProjectedPoint from, ProjectedPoint to, Vec3 fromWorld, Vec3 toWorld,
            bool locallyVisible, bool showHidden, Theme theme, List<Shape> output)
        {
            var screenLength = Vec2.Distance(from.Screen, to.Screen);

            // 退化棱（沿视线方向，投影成一个点）：无论可见与否都没有线段可画。
            // 这个判定必须在"被遮挡"分支之前 —— 否则被遮挡的退化棱会走到那条分支，
            // 被原样输出成一条零长度的虚线。
            if (screenLength < _degenerateLength) return;

            if (!locallyVisible)
            {
                if (showHidden)
                    output.Add(new LineShape(from.Screen, to.Screen,
                        theme.HelperStroke, theme.HelperWidth, theme.HiddenEdgeDash));
                return;
            }

            // 不需要深度缓冲时，局部可见的棱就是**完全**可见的 —— 直接整条画实线，
            // 连采样都省了。这是常见场景（一个凸体）快下来的第二个原因：
            // 既不用建几十万像素的缓冲，也不用逐点采样。
            if (!_useDepthBuffer)
            {
                output.Add(new LineShape(from.Screen, to.Screen, theme.LineStroke, theme.LineWidth));
                return;
            }

            // 每约 2 个缓冲像素采一个点：够密，能画出干净的虚线边界；又不至于慢。
            var samples = Math.Clamp(
                (int)Math.Ceiling(screenLength * _buffer.PixelsPerUnit / 2.0), 2, 512);

            var hidden = new bool[samples + 1];

            for (var i = 0; i <= samples; i++)
            {
                var t = (double)i / samples;

                // 深度必须按世界坐标插值再投影，不能直接插屏幕坐标 ——
                // 透视下屏幕线性插值出来的深度是错的。
                var world = Vec3.Lerp(fromWorld, toWorld, t);
                var screen = Vec2.Lerp(from.Screen, to.Screen, t);
                _view.ProjectWithDepth(world, out var depth);

                // 不需要缓冲时，"没有被别的东西挡住"就是事实，不用查。
                hidden[i] = _useDepthBuffer && depth > _buffer.Sample(screen) + _occlusionBias;
            }

            SuppressFlicker(hidden);

            var index = 0;
            while (index <= samples)
            {
                var start = index;
                var state = hidden[index];
                while (index <= samples && hidden[index] == state) index++;

                if (state && !showHidden) continue;

                // 系数必须夹在 [0,1]：内层循环结束时 index 可能是 samples+1，
                // 直接拿它插值会让线段超出端点一点点。视觉上看不出来，
                // 但会把包围盒撑大，进而让三视图的对齐和缩放算错。
                var a = Vec2.Lerp(from.Screen, to.Screen, Math.Clamp((double)start / samples, 0, 1));
                var b = Vec2.Lerp(from.Screen, to.Screen, Math.Clamp((double)index / samples, 0, 1));
                if (Vec2.Distance(a, b) < _degenerateLength) continue;

                output.Add(state
                    ? new LineShape(a, b, theme.HelperStroke, theme.HelperWidth, theme.HiddenEdgeDash)
                    : new LineShape(a, b, theme.LineStroke, theme.LineWidth));
            }
        }

        /// <summary>
        /// 把过短的分类段吞进邻居。
        /// 没有这一步，旋转到棱与面几乎相切的角度时，虚线段会随浮点噪声逐帧跳变 ——
        /// 那看起来就像软件坏了，老师会立刻不敢用。
        /// </summary>
        private static void SuppressFlicker(bool[] hidden)
        {
            var changed = true;
            var guard = 0;

            while (changed && guard++ < 16)
            {
                changed = false;

                var index = 0;
                while (index < hidden.Length)
                {
                    var start = index;
                    var state = hidden[index];
                    while (index < hidden.Length && hidden[index] == state) index++;

                    if (index - start >= MinimumRunSamples) continue;

                    // 整段翻转成前一段的分类；没有前一段就用后一段的。
                    var replacement = start > 0
                        ? hidden[start - 1]
                        : hidden[Math.Min(index, hidden.Length - 1)];

                    if (replacement == state) continue;

                    for (var i = start; i < index; i++) hidden[i] = replacement;
                    changed = true;
                }
            }
        }

        // ————————————————————————— 场景度量 —————————————————————————

        private Bounds2 ProjectedBounds()
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;

            foreach (var solid in _projected)
                foreach (var point in solid)
                {
                    minX = Math.Min(minX, point.Screen.X);
                    minY = Math.Min(minY, point.Screen.Y);
                    maxX = Math.Max(maxX, point.Screen.X);
                    maxY = Math.Max(maxY, point.Screen.Y);
                }

            if (!double.IsFinite(minX)) return new Bounds2(0, 0, 1, 1);

            // 留一点余量，避免棱正好落在缓冲边界上被截掉。
            var margin = Math.Max(maxX - minX, maxY - minY) * 0.02 + 1e-6;
            return new Bounds2(minX - margin, minY - margin, maxX + margin, maxY + margin);
        }

        /// <summary>
        /// 凸性判定：每个面的所有顶点都必须落在该面的内侧。
        /// 教学体（正方体、棱柱、棱锥、正四面体）都是凸的，于是这条判定几乎总是为真，
        /// 深度缓冲就被省掉了。
        /// </summary>
        private static bool IsConvex(IReadOnlyList<Vec3> vertices, IReadOnlyList<int[]> faces)
        {
            foreach (var face in faces)
            {
                if (face.Length < 3) continue;

                var a = vertices[face[0]];
                var normal = Vec3.Cross(vertices[face[1]] - a, vertices[face[2]] - a);
                if (normal.Length < MathUtil.Epsilon) continue;

                normal = normal.Normalized();

                foreach (var vertex in vertices)
                    if (Vec3.Dot(vertex - a, normal) > MathUtil.Tolerance * 1e3)
                        return false;
            }

            return true;
        }

        private double SceneDiagonal()
        {
            var min = new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            var max = new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

            foreach (var solid in _solids)
                foreach (var v in solid.Vertices)
                {
                    min = new Vec3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
                    max = new Vec3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
                }

            var diagonal = Vec3.Distance(min, max);
            return double.IsFinite(diagonal) && diagonal > 0 ? diagonal : 1;
        }
    }

    private readonly record struct ProjectedPoint(Vec2 Screen, double Depth);

    // ————————————————————————— 深度缓冲 —————————————————————————

    private sealed class DepthBuffer
    {
        private readonly double[] _depth;
        private readonly int _width;
        private readonly int _height;
        private readonly Vec2 _min;

        public DepthBuffer(Bounds2 bounds, int maxDimension)
        {
            var width = Math.Max(bounds.Width, 1e-6);
            var height = Math.Max(bounds.Height, 1e-6);

            PixelsPerUnit = maxDimension / Math.Max(width, height);
            _width = Math.Max(4, (int)Math.Ceiling(width * PixelsPerUnit) + 2);
            _height = Math.Max(4, (int)Math.Ceiling(height * PixelsPerUnit) + 2);
            _min = new Vec2(bounds.MinX, bounds.MinY);

            _depth = new double[_width * _height];
            Array.Fill(_depth, double.PositiveInfinity);
        }

        public double PixelsPerUnit { get; }

        /// <summary>把三角形灌进深度缓冲，每个像素只保留最近的那一层。</summary>
        public void Rasterize(ProjectedPoint a, ProjectedPoint b, ProjectedPoint c)
        {
            var (ax, ay) = ToPixel(a.Screen);
            var (bx, by) = ToPixel(b.Screen);
            var (cx, cy) = ToPixel(c.Screen);

            var minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            var maxX = Math.Min(_width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            var maxY = Math.Min(_height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));

            if (minX > maxX || minY > maxY) return;

            var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(area) < 1e-12) return;   // 退化三角形（面几乎侧对相机）

            var inverseArea = 1.0 / area;

            // 增量式边函数。
            // 原来的写法每个像素要算两次叉积（六次乘法），是这一步最大的开销；
            // 而边函数在 x 方向上的增量是常数，于是行内每个像素只要两次加法。
            // 深度缓冲是整个渲染里最贵的一步，这个改动直接决定了拖拽能不能跟手。
            var stepA = (by - cy) * inverseArea;
            var stepB = (cy - ay) * inverseArea;

            var depthA = a.Depth;
            var depthB = b.Depth;
            var depthC = c.Depth;

            for (var y = minY; y <= maxY; y++)
            {
                var py = y + 0.5;
                var px = minX + 0.5;

                // 每行只在起点算一次
                var wa = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * inverseArea;
                var wb = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * inverseArea;

                var rowOffset = y * _width;

                for (var x = minX; x <= maxX; x++)
                {
                    if (wa >= -1e-9 && wb >= -1e-9)
                    {
                        var wc = 1 - wa - wb;
                        if (wc >= -1e-9)
                        {
                            var depth = wa * depthA + wb * depthB + wc * depthC;
                            var index = rowOffset + x;
                            if (depth < _depth[index]) _depth[index] = depth;
                        }
                    }

                    wa += stepA;
                    wb += stepB;
                }
            }
        }

        /// <summary>取该屏幕位置最近表面的深度。缓冲外返回正无穷（视为没有遮挡）。</summary>
        public double Sample(Vec2 screen)
        {
            var (x, y) = ToPixel(screen);
            var ix = (int)x;
            var iy = (int)y;

            if (ix < 0 || ix >= _width || iy < 0 || iy >= _height) return double.PositiveInfinity;
            return _depth[iy * _width + ix];
        }

        private (double X, double Y) ToPixel(Vec2 screen) => (
            (screen.X - _min.X) * PixelsPerUnit + 1,
            (screen.Y - _min.Y) * PixelsPerUnit + 1);
    }
}
