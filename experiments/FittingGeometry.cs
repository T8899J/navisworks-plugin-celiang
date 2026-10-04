using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    // Geometry is always in metres. Labels select a reconstruction family, never a connection rule.
    // Unsupported profiles fail closed: no bounding-box/chord substitute is produced.
    public static partial class FittingGeometry
    {
        const double Tol = 0.0005;
        public static string Classify(string description, string name = "")
        {
            var classified = ClassifyText(description);
            return classified == "Unknown" ? ClassifyText(name) : classified;
        }

        static string ClassifyText(string text)
        {
            var s = (text ?? "").ToLowerInvariant();
            // Token boundaries matter: Stainless Steel contains the letters "tee".
            // Splice connectors join two tray ends; they have no centreline of their own and only become joint hints.
            if (Regex.IsMatch(s, @"\bsplice\b") || s.Contains("连接片") || s.Contains("连接板")) return "SpliceConnector";
            if (Regex.IsMatch(s, @"\bcross\b") || s.Contains("四通")) return "Cross";
            if (Regex.IsMatch(s, @"\btee\b") || s.Contains("三通")) return "Tee";
            if (s.Contains("reduc") || s.Contains("变径") || s.Contains("变宽")) return "Reducer";
            if (s.Contains("riser") || s.Contains("竖弯") || s.Contains("vertical bend"))
                return AngleClass(text, "Riser");
            if (s.Contains("elbow") || s.Contains("bend") || s.Contains("弯头"))
                return AngleClass(text, "Elbow");
            if (s.Contains("slope") || s.Contains("斜段") || s.Contains("incline")) return "Slope";
            if (s.Contains("straight") || s.Contains("直段") || s.Contains("直通")) return "Straight";
            return "Unknown";
        }

        static string AngleClass(string description, string family)
        {
            var source = (description ?? "").ToLowerInvariant();
            // Only an explicit angle in the description counts; 450 mm or a RunName suffix 45501
            // cannot turn a 90-degree fitting into a 45-degree fitting.
            foreach (var angle in new[] { "45", "90" })
                if (Regex.IsMatch(source, @"(?<!\d)" + angle + @"\s*(?:deg(?:ree)?s?\b|°|度)") ||
                    Regex.IsMatch(source, @"(?:elbow|riser|bend)[\s_\-]*" + angle + @"(?!\d)")) return family + angle;
            return family;
        }

        public static Part Build(IList<Triangle> mesh, string classification, double nominalWidth = 0, double nominalHeight = 0)
        {
            if (mesh == null || mesh.Count == 0) throw Fail("NO_GEOMETRY", "没有三角网格");
            if (mesh.Count > 200000) throw Fail("GEOMETRY_LIMIT", "三角面数量超过 200000");
            if (mesh.Any(t => !t.A.Finite || !t.B.Finite || !t.C.Finite))
                throw Fail("INVALID_COORDINATE", "网格含非有限坐标");
            var kind = classification ?? "Unknown";
            if (kind.Equals("Straight", StringComparison.OrdinalIgnoreCase) || kind.Equals("Slope", StringComparison.OrdinalIgnoreCase))
                return Straight(mesh, kind, nominalWidth, nominalHeight);
            if (kind.StartsWith("Elbow", StringComparison.OrdinalIgnoreCase) || kind.StartsWith("Riser", StringComparison.OrdinalIgnoreCase))
                return Bend(mesh, kind);
            if (kind.Equals("Reducer", StringComparison.OrdinalIgnoreCase)) return Reducer(mesh);
            if (kind.Equals("Tee", StringComparison.OrdinalIgnoreCase)) return Tee(mesh);
            if (kind.Equals("Cross", StringComparison.OrdinalIgnoreCase))
                throw Fail("CROSS_NOT_IMPLEMENTED", "图架构支持四个端口，四通几何重建尚未实现");
            throw Fail("UNCLASSIFIED", "无法确定配件几何重建类型");
        }

        static Part Straight(IList<Triangle> mesh, string kind, double nominalWidth = 0, double nominalHeight = 0)
        {
            var m = StraightMeasurement.Measure(mesh, true);
            if (!m.IsStraightCandidate)
            {
                var stub = NominalStub(mesh, nominalWidth, nominalHeight);
                if (stub == null) throw Fail("STRAIGHT_SECTION_FAILED", m.Reason + string.Format(System.Globalization.CultureInfo.InvariantCulture, " [tri={0} nominal={1:F3}x{2:F3} span={3:F4} w={4:F4} h={5:F4}]", mesh.Count, nominalWidth, nominalHeight, m.SpanMetres, m.WidthMetres, m.HeightMetres));
                m = stub;
            }
            var p = TwoPort(kind, new[] { m.PortStart, m.PortEnd }, m.Axis * -1, m.Axis,
                m.WidthMetres, m.HeightMetres, m.WidthMetres, m.HeightMetres,
                m.CrossAxis1, m.CrossAxis2, m.CrossAxis1, m.CrossAxis2);
            return p;
        }

        // Short cut-down stubs (span close to or below the cross-section) defeat PCA. Try each pair of
        // orthogonal dominant face normals as the cross-section frame; the remaining axis is longitudinal.
        // Accept only when exactly one frame has a continuous section whose height matches the nominal
        // Size and whose width is at least the nominal width (measured width includes flanges).
        const double NominalTolerance = .005;
        static Measurement NominalStub(IList<Triangle> mesh, double nominalWidth, double nominalHeight)
        {
            if (nominalWidth <= 0 || nominalHeight <= 0) return null;
            var normals = mesh.Where(t => t.Area > 1e-10).GroupBy(t => DirectionKey(Unit((t.B - t.A).Cross(t.C - t.A))))
                .Select(g => new { n = g.Key, area = g.Sum(t => t.Area) }).OrderByDescending(g => g.area).Take(6).Select(g => g.n).ToList();
            var accepted = new List<Measurement>();
            var tried = new List<Vec>();
            foreach (var a in normals) foreach (var b in normals)
            {
                if (Math.Abs(a.Dot(b)) > 1e-3) continue;
                var axis = Unit(a.Cross(b));
                if (tried.Any(t => Math.Abs(t.Dot(axis)) > 1 - 1e-6)) continue;
                tried.Add(axis);
                var m = StraightMeasurement.MeasureFrame(mesh, axis, a, b);
                if (!m.IsStraightCandidate) continue;
                // Orient so CrossAxis2 carries the nominal height.
                bool swap = Math.Abs(m.WidthMetres - nominalHeight) <= NominalTolerance && Math.Abs(m.HeightMetres - nominalHeight) > NominalTolerance;
                if (swap) m = StraightMeasurement.MeasureFrame(mesh, axis * -1, b, a);
                if (Math.Abs(m.HeightMetres - nominalHeight) <= NominalTolerance && m.WidthMetres >= nominalWidth - NominalTolerance && m.SpanMetres > NominalTolerance)
                {
                    // Same length-to-section requirement as the PCA path: a piece shorter than its own
                    // widest side is a plate or fragment, not a tray run, however well its section matches.
                    double crossSpan = Math.Max(m.WidthMetres, m.HeightMetres);
                    if (m.SpanMetres < crossSpan * 1.15) continue;
                    accepted.Add(m);
                }
            }
            return accepted.Count == 1 ? accepted[0] : null;
        }

        static Vec DirectionKey(Vec n)
        {
            // Opposite faces share one axis; quantise so coplanar triangles group together.
            if (n.X < -1e-9 || Math.Abs(n.X) <= 1e-9 && (n.Y < -1e-9 || Math.Abs(n.Y) <= 1e-9 && n.Z < 0)) n = n * -1;
            return Unit(new Vec(Math.Round(n.X, 4), Math.Round(n.Y, 4), Math.Round(n.Z, 4)));
        }

        static Part Bend(IList<Triangle> mesh, string kind)
        {
            var vertices = Unique(mesh.SelectMany(t => new[] { t.A, t.B, t.C }));
            var origin = vertices[0];
            foreach (var normal in CandidateAxes(mesh).Take(24))
            {
                Vec u, v; Basis(normal, out u, out v);
                var profile = Unique(vertices.Select(w => new Vec((w - origin).Dot(u), (w - origin).Dot(v), 0)));
                // Limits keep pathological meshes bounded without simplifying their geometry silently.
                if (profile.Count < 8 || profile.Count > 600) continue;
                var depth = vertices.Select(p => (p - origin).Dot(normal)).ToArray();
                double lo = depth.Min(), hi = depth.Max();
                if (hi - lo < 0.002) continue;
                foreach (var center in CircleCandidates(profile))
                {
                    Part result;
                    if (TryBendProfile(mesh, vertices, profile, origin, normal, u, v, lo, hi, center, kind, out result))
                        return result;
                }
            }
            Part polyline;
            if (TryPolylineBend(mesh, vertices, kind, out polyline)) return polyline;
            throw Fail("BEND_PROFILE_FAILED", "未找到连续的圆弧轮廓或可配对的折线侧边及两个开口；不使用端口弦长替代弯曲长度");
        }

        static bool TryBendProfile(IList<Triangle> mesh, IList<Vec> vertices, IList<Vec> points,
            Vec origin, Vec normal, Vec u, Vec v, double lo, double hi, Vec center, string kind, out Part result)
        {
            result = null;
            var radii = points.Select(p => (p - center).Norm).ToArray();
            double inner = radii.Min(), outer = radii.Max();
            if (inner < 0.005 || outer - inner < 0.002 || outer / inner > 20) return false;
            var inside = points.Where((p, i) => Math.Abs(radii[i] - inner) < Tol).ToArray();
            var outside = points.Where((p, i) => Math.Abs(radii[i] - outer) < Tol).ToArray();
            if (inside.Length < 4 || outside.Length < 4) return false;
            var ordered = inside.OrderBy(p => Angle(p - center)).ToArray();
            int cut = 0; double largest = -1;
            for (int i = 0; i < ordered.Length; i++)
            {
                double gap = PositiveAngle(Angle(ordered[(i + 1) % ordered.Length] - center) - Angle(ordered[i] - center));
                if (gap > largest) { largest = gap; cut = (i + 1) % ordered.Length; }
            }
            var sorted = Enumerable.Range(0, ordered.Length).Select(i => ordered[(i + cut) % ordered.Length]).ToArray();
            double startAngle = Angle(sorted[0] - center), sweep = 2 * Math.PI - largest;
            // The label is checked against the recovered angle; it does not supply a radius or a length.
            double expected = kind.EndsWith("45", StringComparison.Ordinal) ? Math.PI / 4 : kind.EndsWith("90", StringComparison.Ordinal) ? Math.PI / 2 : sweep;
            if (sweep < Math.PI / 12 || sweep > Math.PI * 1.05 || Math.Abs(sweep - expected) > 0.008) return false;
            var angles = sorted.Select(p => PositiveAngle(Angle(p - center) - startAngle)).ToArray();
            angles[0] = 0;
            if (angles.Zip(angles.Skip(1), (a, b) => b - a).Any(d => d <= 0 || d > Math.PI / 6)) return false;
            var lines = new List<Vec>();
            foreach (double a in angles)
            {
                var radial = new Vec(Math.Cos(startAngle + a), Math.Sin(startAngle + a), 0);
                if (!outside.Any(p => ((p - center) * (1 / (p - center).Norm) - radial).Norm < Tol / Math.Max(outer, 0.01))) return false;
                lines.Add(origin + normal * ((lo + hi) / 2) + u * (center.X + radial.X * ((inner + outer) / 2)) + v * (center.Y + radial.Y * ((inner + outer) / 2)));
            }
            // All model vertices must belong to the same annular sweep. Extra branches and detached
            // geometry cannot be hidden by considering only the best-fitting subset.
            foreach (var p in points)
            {
                double a = PositiveAngle(Angle(p - center) - startAngle);
                if (a > sweep + 0.003 && a < 2 * Math.PI - 0.003) return false;
                if (a > 0.003 && a < sweep - 0.003 && !angles.Any(x => Math.Abs(x - a) < 0.003)) return false;
            }
            // Measure actual mesh sections through every model segment midpoint; verify that a
            // discontinuous pair of rails is not accepted as an unbroken bend.
            for (int i = 1; i < lines.Count; i++)
            {
                double a = startAngle + (angles[i - 1] + angles[i]) / 2;
                var radial = u * Math.Cos(a) + v * Math.Sin(a);
                var tangent = u * -Math.Sin(a) + v * Math.Cos(a);
                Vec planeOrigin = origin + u * center.X + v * center.Y;
                var section = Slice(mesh, planeOrigin, tangent, radial, normal, 0);
                if (section == null) return false;
                double halfAngle = (angles[i] - angles[i - 1]) / 2;
                if (Math.Abs(section[0] - inner * Math.Cos(halfAngle)) > Tol * 2 || Math.Abs(section[1] - outer * Math.Cos(halfAngle)) > Tol * 2 ||
                    Math.Abs(section[2] - lo) > Tol * 2 || Math.Abs(section[3] - hi) > Tol * 2) return false;
            }
            var r0 = u * Math.Cos(startAngle) + v * Math.Sin(startAngle);
            var r1 = u * Math.Cos(startAngle + sweep) + v * Math.Sin(startAngle + sweep);
            var t0 = u * Math.Sin(startAngle) - v * Math.Cos(startAngle);
            var t1 = u * -Math.Sin(startAngle + sweep) + v * Math.Cos(startAngle + sweep);
            bool riser = kind.StartsWith("Riser", StringComparison.OrdinalIgnoreCase);
            double width = riser ? hi - lo : outer - inner, height = riser ? outer - inner : hi - lo;
            result = TwoPort(kind, lines.ToArray(), t0, t1, width, height, width, height,
                riser ? normal : r0, riser ? r0 : normal, riser ? normal : r1, riser ? r1 : normal);
            return true;
        }

        static Part Reducer(IList<Triangle> mesh)
        {
            var vertices = Unique(mesh.SelectMany(t => new[] { t.A, t.B, t.C }));
            var origin = vertices[0];
            foreach (var axis in CandidateAxes(mesh).Take(24))
            {
                foreach (var cross in CandidateAxes(mesh).Where(n => Math.Abs(n.Dot(axis)) < 0.01).Take(8))
                {
                    var u = Unit(cross - axis * cross.Dot(axis)); var v = axis.Cross(u);
                    var along = vertices.Select(p => (p - origin).Dot(axis)).ToArray();
                    double low = along.Min(), high = along.Max(), length = high - low;
                    if (length < 0.01) continue;
                    var sections = Enumerable.Range(1, 19).Select(i => Slice(mesh, origin, axis, u, v, low + length * i / 20)).ToArray();
                    if (sections.Any(s => s == null || s[1] - s[0] < .002 || s[3] - s[2] < .002)) continue;
                    var first = sections[0]; var last = sections[sections.Length - 1];
                    var delta = Enumerable.Range(0, 4).Select(i => (last[i] - first[i]) / .9).ToArray();
                    var start = Enumerable.Range(0, 4).Select(i => first[i] - delta[i] * .05).ToArray();
                    var end = Enumerable.Range(0, 4).Select(i => start[i] + delta[i]).ToArray();
                    if (Math.Abs((start[1] - start[0]) - (end[1] - end[0])) < Tol && Math.Abs((start[3] - start[2]) - (end[3] - end[2])) < Tol) continue;
                    bool linear = true;
                    for (int k = 0; k < sections.Length; k++)
                        for (int c = 0; c < 4; c++)
                            if (Math.Abs(sections[k][c] - start[c] - delta[c] * (k + 1) / 20) > Tol) linear = false;
                    if (!linear) continue;
                    // Every vertex must remain within the taper, and both end profiles must exist.
                    if (vertices.Any(p =>
                    {
                        var w = p - origin; var f = (w.Dot(axis) - low) / length;
                        return w.Dot(u) < start[0] + delta[0] * f - Tol || w.Dot(u) > start[1] + delta[1] * f + Tol ||
                            w.Dot(v) < start[2] + delta[2] * f - Tol || w.Dot(v) > start[3] + delta[3] * f + Tol;
                    })) continue;
                    if (!EndProfile(vertices, origin, axis, u, v, low, start) || !EndProfile(vertices, origin, axis, u, v, high, end)) continue;
                    var a = origin + axis * low + u * ((start[0] + start[1]) / 2) + v * ((start[2] + start[3]) / 2);
                    var b = origin + axis * high + u * ((end[0] + end[1]) / 2) + v * ((end[2] + end[3]) / 2);
                    return TwoPort("Reducer", new[] { a, b }, axis * -1, axis,
                        start[1] - start[0], start[3] - start[2], end[1] - end[0], end[3] - end[2], u, v, u, v);
                }
            }
            throw Fail("REDUCER_SECTION_FAILED", "两端或 19 个中间截面不符合连续线性变径；支持偏心变径，未以规格差异拒绝连接");
        }

        static bool EndProfile(IList<Vec> points, Vec origin, Vec axis, Vec u, Vec v, double station, double[] expected)
        {
            var end = points.Where(p => Math.Abs((p - origin).Dot(axis) - station) < .00002).ToArray();
            if (end.Length < 3) return false;
            var bounds = new[] { end.Min(p => (p - origin).Dot(u)), end.Max(p => (p - origin).Dot(u)), end.Min(p => (p - origin).Dot(v)), end.Max(p => (p - origin).Dot(v)) };
            return Enumerable.Range(0, 4).All(i => Math.Abs(bounds[i] - expected[i]) < Tol * 2);
        }

        static Part Tee(IList<Triangle> mesh)
        {
            var points = Unique(mesh.SelectMany(t => new[] { t.A, t.B, t.C }));
            var origin = points[0];
            foreach (var normal in CandidateAxes(mesh).Take(24))
            {
                Vec basisU, basisV; Basis(normal, out basisU, out basisV);
                foreach (var loop in PlanarOutlines(mesh, origin, normal, basisU, basisV))
                {
                    var polygon = Simplify(loop);
                    if (polygon.Count != 8) continue;
                    if (SignedArea(polygon) < 0) polygon.Reverse();
                    var concave = Enumerable.Range(0, polygon.Count).Where(i =>
                        Cross2(polygon[i] - polygon[(i + polygon.Count - 1) % polygon.Count], polygon[(i + 1) % polygon.Count] - polygon[i]) < -1e-8).ToArray();
                    if (concave.Length != 2) continue;
                    var pu = Unit(polygon[concave[1]] - polygon[concave[0]]);
                    var pv = new Vec(-pu.Y, pu.X, 0);
                    if (polygon.Where((p, i) =>
                    {
                        var d = Unit(polygon[(i + 1) % polygon.Count] - p);
                        return Math.Abs(d.Dot(pu)) > .005 && Math.Abs(d.Dot(pv)) > .005;
                    }).Any()) continue;
                    var q = polygon.Select(p => new Vec(p.Dot(pu), p.Dot(pv), 0)).ToArray();
                    double minU = q.Min(p => p.X), maxU = q.Max(p => p.X), minV = q.Min(p => p.Y), maxV = q.Max(p => p.Y);
                    double left = Math.Min(q[concave[0]].X, q[concave[1]].X), right = Math.Max(q[concave[0]].X, q[concave[1]].X), joinV = q[concave[0]].Y;
                    if (Math.Abs(q[concave[1]].Y - joinV) > Tol || right - left < .002 || left - minU < .002 || maxU - right < .002) continue;
                    // The branch end has two outline vertices; the uninterrupted main side has two
                    // vertices at the outer main ends. This determines the opening without a label.
                    bool positive = q.Where(p => Math.Abs(p.Y - maxV) < Tol).All(p => p.X >= left - Tol && p.X <= right + Tol);
                    double farV = positive ? minV : maxV, tipV = positive ? maxV : minV;
                    if (Math.Abs(joinV - farV) < .002 || Math.Abs(tipV - joinV) < .002) continue;
                    if (!q.All(p => InT(p.X, p.Y, minU, maxU, left, right, farV, joinV, tipV, Tol))) continue;
                    var u = basisU * pu.X + basisV * pu.Y; var v = basisU * pv.X + basisV * pv.Y;
                    var depths = points.Select(p => (p - origin).Dot(normal)).ToArray(); double lo = depths.Min(), hi = depths.Max();
                    if (hi - lo < .002) continue;
                    if (points.Any(p => !InT((p - origin).Dot(u), (p - origin).Dot(v), minU, maxU, left, right, farV, joinV, tipV, Tol))) continue;
                    double midV = (farV + joinV) / 2, midU = (left + right) / 2, depth = (lo + hi) / 2;
                    Func<double, double, Vec> world = (x, y) => origin + u * x + v * y + normal * depth;
                    var junction = world(midU, midV);
                    var ports = new[] {
                        new Port { Id="p0",Point=world(minU,midV),Outward=u*-1,Width=Math.Abs(joinV-farV),Height=hi-lo,WidthAxis=v,HeightAxis=normal },
                        new Port { Id="p1",Point=world(maxU,midV),Outward=u,Width=Math.Abs(joinV-farV),Height=hi-lo,WidthAxis=v,HeightAxis=normal },
                        new Port { Id="p2",Point=world(midU,tipV),Outward=v*(positive?1:-1),Width=right-left,Height=hi-lo,WidthAxis=u,HeightAxis=normal }
                    };
                    // Validate the floor/side extent throughout each arm, including inside the fitting.
                    if (ports.Any(port => !ArmPresent(mesh, port.Point, junction, normal))) continue;
                    return new Part { Kind="Tee",Ports=ports,Centerline=new Vec[0],
                        Junctions=new[]{new PartJunction{Id="j0",Point=junction}},
                        InternalEdges=ports.Select((p,i)=>new InternalEdge{Id="arm"+i,From=p.Id,To="j0",Centerline=new[]{p.Point,junction}}).ToArray() };
                }
            }
            throw Fail("TEE_PROFILE_FAILED", "无法从连续 T 形底面确定三个开口与内部交点；仅支持三条直臂的平面 T 形，不推测缺失支臂");
        }

        static bool ArmPresent(IList<Triangle> mesh, Vec start, Vec end, Vec height)
        {
            var axis = Unit(end - start); var width = Unit(height.Cross(axis));
            for (int i = 1; i < 10; i++)
            {
                var section = Slice(mesh, start, axis, width, height, (end - start).Norm * i / 10);
                if (section == null || section[1] - section[0] < .002 || section[3] - section[2] < .002) return false;
            }
            return true;
        }

        static bool InT(double x, double y, double min, double max, double left, double right, double far, double join, double tip, double tol)
        {
            return x >= min - tol && x <= max + tol && y >= Math.Min(far, join) - tol && y <= Math.Max(far, join) + tol ||
                x >= left - tol && x <= right + tol && y >= Math.Min(join, tip) - tol && y <= Math.Max(join, tip) + tol;
        }

        static IEnumerable<List<Vec>> PlanarOutlines(IList<Triangle> mesh, Vec origin, Vec normal, Vec u, Vec v)
        {
            var planes = new Dictionary<long, List<Triangle>>();
            foreach (var t in mesh)
            {
                var n = (t.B - t.A).Cross(t.C - t.A);
                if (n.Norm < 1e-12 || Math.Abs(Unit(n).Dot(normal)) < .99999) continue;
                long key = (long)Math.Round((t.A - origin).Dot(normal) / .00001);
                if (!planes.ContainsKey(key)) planes[key] = new List<Triangle>();
                planes[key].Add(t);
            }
            foreach (var faces in planes.Values.OrderByDescending(g => g.Sum(t => t.Area)).Take(4))
            {
                var vertices = new List<Vec>(); var indices = new Dictionary<string, List<int>>(); var counts = new Dictionary<string, int>(); var ends = new Dictionary<string, int[]>();
                Func<Vec, int> index = p =>
                {
                    var q = new Vec((p - origin).Dot(u), (p - origin).Dot(v), 0);
                    // Adjacent COM triangles can differ by nanometres across a rounding boundary.
                    // Weld by distance in neighbouring 10 micrometre cells, not rounded-key equality.
                    const double weld = .00001;
                    long x = (long)Math.Floor(q.X/weld), y = (long)Math.Floor(q.Y/weld);
                    for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                    {
                        List<int> near;
                        if(indices.TryGetValue((x+dx)+":"+(y+dy),out near))
                            foreach(int previous in near)if((vertices[previous]-q).Norm<=weld)return previous;
                    }
                    string k=x+":"+y;List<int> bucket;if(!indices.TryGetValue(k,out bucket))indices[k]=bucket=new List<int>();
                    int id=vertices.Count;bucket.Add(id);vertices.Add(q);return id;
                };
                foreach (var t in faces)
                {
                    var ids = new[] { index(t.A), index(t.B), index(t.C) };
                    for (int i = 0; i < 3; i++)
                    {
                        int a = ids[i], b = ids[(i + 1) % 3]; if (a == b) continue;
                        string k = Math.Min(a, b) + ":" + Math.Max(a, b);
                        if (!counts.ContainsKey(k)) { counts[k] = 0; ends[k] = new[] { a, b }; }
                        counts[k]++;
                    }
                }
                var boundary = ends.Where(e => counts[e.Key] == 1).Select(e => e.Value).ToArray();
                var neighbours = new Dictionary<int, List<int>>();
                foreach (var e in boundary) foreach (int a in e) { if (!neighbours.ContainsKey(a)) neighbours[a] = new List<int>(); neighbours[a].Add(e[0] == a ? e[1] : e[0]); }
                if (neighbours.Count < 4 || neighbours.Values.Any(n => n.Count != 2)) continue;
                var remaining = new HashSet<int>(neighbours.Keys); var loops = new List<List<Vec>>();
                while (remaining.Count > 0)
                {
                    int first = remaining.First(), current = first, prev = -1; var path = new List<Vec>();
                    do
                    {
                        if (!remaining.Remove(current)) { path.Clear(); break; }
                        path.Add(vertices[current]); int next = neighbours[current].First(n => n != prev); prev = current; current = next;
                    } while (current != first && path.Count <= boundary.Length);
                    if (path.Count > 0) loops.Add(path);
                }
                // Multiple disconnected floor components/holes are not silently treated as a Tee.
                if (loops.Count == 1) yield return loops[0];
            }
        }

        static List<Vec> Simplify(List<Vec> original)
        {
            var p = original.ToList(); bool changed = true;
            while (changed && p.Count > 3)
            {
                changed = false;
                for (int i = 0; i < p.Count; i++)
                {
                    var a = p[i] - p[(i + p.Count - 1) % p.Count]; var b = p[(i + 1) % p.Count] - p[i];
                    if (a.Norm < Tol || b.Norm < Tol || Math.Abs(Cross2(a, b)) / (a.Norm * b.Norm) < .001 && a.Dot(b) > 0)
                    { p.RemoveAt(i); changed = true; break; }
                }
            }
            return p;
        }

        static Part TwoPort(string kind, Vec[] line, Vec out0, Vec out1, double w0, double h0, double w1, double h1, Vec u0, Vec v0, Vec u1, Vec v1)
        {
            return new Part { Kind=kind,Centerline=line,Junctions=new PartJunction[0],
                Ports=new[]{new Port{Id="p0",Point=line[0],Outward=out0,Width=w0,Height=h0,WidthAxis=u0,HeightAxis=v0},
                    new Port{Id="p1",Point=line[line.Length-1],Outward=out1,Width=w1,Height=h1,WidthAxis=u1,HeightAxis=v1}},
                InternalEdges=new[]{new InternalEdge{Id="centerline",From="p0",To="p1",Centerline=line}} };
        }

        static IEnumerable<Vec> CandidateAxes(IList<Triangle> mesh)
        {
            var candidates = new List<Vec>();
            try { var m = StraightMeasurement.Measure(mesh); candidates.AddRange(new[] { m.Axis, m.CrossAxis1, m.CrossAxis2 }); } catch (InvalidOperationException) { }
            candidates.AddRange(mesh.Where(t => t.Area > 1e-10).OrderByDescending(t => t.Area).Take(1000).Select(t => Unit((t.B - t.A).Cross(t.C - t.A))));
            var unique = new List<Vec>();
            foreach (var n in candidates)
            {
                // A nearly aligned PCA axis is not the exact mesh-plane normal. Retain the
                // latter so quantized coplanar-face grouping also works after arbitrary rotation.
                if (!n.Finite || n.Norm < .99 || unique.Any(q => Math.Abs(q.Dot(n)) > 1-1e-10)) continue;
                unique.Add(n); yield return n;
                if (unique.Count >= 24) yield break;
            }
        }

        static IEnumerable<Vec> CircleCandidates(IList<Vec> p)
        {
            // Deterministic RANSAC, reproducible and bounded. Every candidate is subsequently checked
            // against every vertex and mesh section, not merely the three sampled points.
            var emitted = new HashSet<string>(); var random = new Random(137);
            for (int iteration = 0; iteration < 1800; iteration++)
            {
                int a = random.Next(p.Count), b = random.Next(p.Count), c = random.Next(p.Count);
                if (a == b || b == c || c == a) continue;
                var x = p[b] - p[a]; var y = p[c] - p[a]; double d = 2 * Cross2(x, y);
                if (Math.Abs(d) < 1e-10) continue;
                var center = p[a] + new Vec((x.Dot(x) * y.Y - y.Dot(y) * x.Y) / d, (x.X * y.Dot(y) - y.X * x.Dot(x)) / d, 0);
                if (center.Finite && emitted.Add(Key(center))) yield return center;
            }
        }

        static double[] Slice(IList<Triangle> mesh, Vec origin, Vec axis, Vec u, Vec v, double station)
        {
            var bounds = new[] { double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity };
            Action<Vec, Vec> edge = (a, b) =>
            {
                a = a - origin; b = b - origin;
                double x = a.Dot(axis), y = b.Dot(axis);
                if (Math.Abs(y - x) < 1e-12 || station < Math.Min(x, y) - 1e-10 || station > Math.Max(x, y) + 1e-10) return;
                var p = a + (b - a) * ((station - x) / (y - x)); double pu = p.Dot(u), pv = p.Dot(v);
                bounds[0] = Math.Min(bounds[0], pu); bounds[1] = Math.Max(bounds[1], pu); bounds[2] = Math.Min(bounds[2], pv); bounds[3] = Math.Max(bounds[3], pv);
            };
            foreach (var t in mesh) { edge(t.A, t.B); edge(t.B, t.C); edge(t.C, t.A); }
            return double.IsInfinity(bounds[0]) ? null : bounds;
        }
        static List<Vec> Unique(IEnumerable<Vec> points)
        {
            var seen = new HashSet<string>(); var result = new List<Vec>();
            foreach (var p in points) if (seen.Add(Key(p))) result.Add(p);
            return result;
        }
        static string Key(Vec p) { return Math.Round(p.X / .00001).ToString(CultureInfo.InvariantCulture) + "/" + Math.Round(p.Y / .00001).ToString(CultureInfo.InvariantCulture) + "/" + Math.Round(p.Z / .00001).ToString(CultureInfo.InvariantCulture); }
        static Vec Unit(Vec p) { return p * (1 / p.Norm); }
        static void Basis(Vec n, out Vec u, out Vec v) { u = Unit(n.Cross(Math.Abs(n.Z) < .8 ? new Vec(0, 0, 1) : new Vec(0, 1, 0))); v = n.Cross(u); }
        static double Cross2(Vec a, Vec b) { return a.X * b.Y - a.Y * b.X; }
        static double SignedArea(IList<Vec> p) { double a = 0; for (int i = 0; i < p.Count; i++) a += Cross2(p[i], p[(i + 1) % p.Count]); return a / 2; }
        static double Angle(Vec p) { return Math.Atan2(p.Y, p.X); }
        static double PositiveAngle(double x) { while (x < 0) x += Math.PI * 2; while (x >= Math.PI * 2) x -= Math.PI * 2; return x; }
        static InvalidOperationException Fail(string code, string detail) { return new InvalidOperationException(code + ": " + detail); }
    }
}
