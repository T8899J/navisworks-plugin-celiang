using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using JiePinPai.TrayMeasurement;
using JiePinPai.TrayMeasurement.Core;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace TrayRouteExperiment
{
    public sealed class RejectedComponent
    {
        public string ModelItemId, DisplayName, RunName, Description, Size;
        public string TypeClassification, GeometryFailureReason;
        // World-metre bounding box, and distance to the measured route centreline when a route exists.
        public Vec? BoxMin, BoxMax;
        public double? DistanceToRoute;
    }

    public sealed class VirtualConnectorSuspect
    {
        // Rejected components near a virtual connector: the connector most likely bridges their missing geometry.
        public string EdgeId, SourceName, TargetName;
        public double Distance3D;
        public List<object> NearbyRejected;
    }

    public sealed class SpatialRegion
    {
        // World metres. Axis-aligned box around both endpoints, expanded by Margin on every side.
        public Vec Min, Max, A, B;
        public double Margin;
        public static SpatialRegion Around(Vec a, Vec b, double margin)
        {
            var d = new Vec(margin, margin, margin);
            return new SpatialRegion { Margin = margin, A = a, B = b,
                Min = new Vec(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)) - d,
                Max = new Vec(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)) + d };
        }
        public bool Intersects(Vec lower, Vec upper)
        {
            return lower.X <= Max.X && upper.X >= Min.X && lower.Y <= Max.Y && upper.Y >= Min.Y && lower.Z <= Max.Z && upper.Z >= Min.Z;
        }
        public bool Contains(SpatialRegion other)
        {
            return other.Min.X >= Min.X && other.Min.Y >= Min.Y && other.Min.Z >= Min.Z && other.Max.X <= Max.X && other.Max.Y <= Max.Y && other.Max.Z <= Max.Z;
        }
        public double SegmentDistance(Vec lower, Vec upper) { return BoxSegmentDistance(A, B, lower, upper); }
        // Distance from segment A-B to an axis-aligned box; convex in t, so ternary search is exact enough.
        public static double BoxSegmentDistance(Vec A, Vec B, Vec lower, Vec upper)
        {
            Func<double, double> at = t => {
                var p = A + (B - A) * t;
                double dx = Math.Max(0, Math.Max(lower.X - p.X, p.X - upper.X)), dy = Math.Max(0, Math.Max(lower.Y - p.Y, p.Y - upper.Y)), dz = Math.Max(0, Math.Max(lower.Z - p.Z, p.Z - upper.Z));
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            };
            double lo = 0, hi = 1;
            for (int i = 0; i < 60; i++) { double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3; if (at(m1) <= at(m2)) hi = m2; else lo = m1; }
            return at((lo + hi) / 2);
        }
    }

    public sealed class HostSettings
    {
        // Initial margin around the start/finish box, doubled after a path-not-found until the maximum. Zero disables.
        public double SpatialRegionMargin { get; set; } = 20;
        public double SpatialRegionMaxMargin { get; set; } = 160;
        // Reconstruct a straight whose run axis PCA cannot fix, using its nominal section.
        // Default OFF: enabling it also admits many 5cm fragments, whose extra free ports crowd out
        // virtual candidates and drop ambiguous physical joins, which lengthened the measured route.
        public bool StraightStubByNominalSection { get; set; }
    }

    internal sealed class HostNetwork
    {
        public CableNetwork Graph;
        public SpatialRegion Region;
        public int RegionCandidateCount, OutsideRegionCount;
        public readonly List<double> RegionMarginsTried = new List<double>();
        public readonly List<ModelItem[]> Geometry = new List<ModelItem[]>();
        public readonly List<RejectedComponent> Rejected = new List<RejectedComponent>();
        public readonly List<RejectedComponent> Recognized = new List<RejectedComponent>();
        public readonly List<CableJointHint> JointHints = new List<CableJointHint>();
        public ModelItem Root;
        public ModelItem[] Roots;
        public Document Document;
        public bool Incomplete;
        public int ScanNodeCount, ScanCandidateCount;
        public double ScanElapsedSeconds;
        const double ScanLimitSeconds = 60;

        sealed class Metadata
        {
            public string Name, RunName = "", Description = "", Size = "", Classification;
        }
        sealed class ScanFrame
        {
            public ModelItem Item;
            public int Depth;
            public bool TrayAncestor, Excluded;
        }
        sealed class Candidate
        {
            public ModelItem Item;
            public int Depth;
            public bool Excluded;
            public RejectedComponent Info;
            public Vec? Lower, Upper;
            public Candidate Source;
        }
        sealed class Catalog
        {
            public Document Document;
            public ModelItem[] Roots;
            public List<Candidate> Candidates;
            public int ScanNodeCount;
            public double ScanElapsedSeconds;
        }
        static Catalog catalog;

        public static string Key(ModelItem item)
        {
            var a = (Array)ComApiBridge.ToInwOaPath(item).ArrayData;
            return string.Join("/", a.Cast<object>().Select(Convert.ToInt32));
        }

        public static string Prop(ModelItem item, string name)
        {
            foreach (var category in item.PropertyCategories)
                foreach (var property in category.Properties)
                    if (string.Equals(property.DisplayName, name, StringComparison.OrdinalIgnoreCase) && property.Value.IsDisplayString)
                        return property.Value.ToDisplayString();
            return "";
        }

        static Metadata ReadMetadata(ModelItem item, Action checkBudget = null)
        {
            var result = new Metadata { Name = item.DisplayName ?? "" };
            if (result.Name == "Geometry" || result.Name == "Maintenance Volume") return result;
            bool gotRun = false, gotDescription = false, gotSize = false;
            int inspectedProperties = 0;
            foreach (var category in item.PropertyCategories)
            {
                if (checkBudget != null) checkBudget();
                foreach (var property in category.Properties)
                {
                    if (checkBudget != null && (++inspectedProperties % 32) == 0) checkBudget();
                    string name = property.DisplayName;
                    bool run = !gotRun && string.Equals(name, "RunName", StringComparison.OrdinalIgnoreCase);
                    bool description = !gotDescription && string.Equals(name, "Description", StringComparison.OrdinalIgnoreCase);
                    bool size = !gotSize && string.Equals(name, "Size", StringComparison.OrdinalIgnoreCase);
                    if (!run && !description && !size) continue;
                    var value = property.Value;
                    if (!value.IsDisplayString) continue;
                    string text = value.ToDisplayString() ?? "";
                    if (run) { result.RunName = text; gotRun = true; }
                    if (description) { result.Description = text; gotDescription = true; }
                    if (size) { result.Size = text; gotSize = true; }
                    if (gotRun && gotDescription && gotSize) return result;
                }
            }
            return result;
        }

        static bool IsCandidate(Metadata metadata, bool trayAncestor)
        {
            if (metadata.Name == "Geometry" || metadata.Name == "Maintenance Volume") return false;
            string run = metadata.RunName, description = metadata.Description;
            // RunName also exists on pipes and equipment. Use tray metadata only for discovery,
            // then let geometry connect all discovered runs without name/discipline equality.
            bool trayContext = metadata.Name.IndexOf("TRAY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                run.IndexOf("TRAY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                description.IndexOf("tray", StringComparison.OrdinalIgnoreCase) >= 0 || description.Contains("桥架") ||
                trayAncestor;
            // Empty descriptions outside tray ancestry cannot pass the explicit-type fixture rule.
            if (!trayContext && string.IsNullOrWhiteSpace(description)) return false;
            string classification = metadata.Classification = FittingGeometry.Classify(description, metadata.Name);
            if (trayContext && !string.IsNullOrWhiteSpace(run)) return true;
            bool known = !string.Equals(classification, "unknown", StringComparison.OrdinalIgnoreCase);
            // Explicit type-only descriptions also support IFC fixtures and generic tray exports.
            return known && (trayContext || string.Equals(description.Trim(), classification, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsCandidate(ModelItem item)
        {
            // Picking one object may inspect its ancestors. Whole-document discovery never does.
            var metadata = ReadMetadata(item);
            if (metadata.Name == "Geometry" || metadata.Name == "Maintenance Volume") return false;
            return IsCandidate(metadata, item.Ancestors.Any(a => (a.DisplayName ?? "").EndsWith("-TRAY", StringComparison.OrdinalIgnoreCase)));
        }

        // "150 mm x 150 mm" -> width, height in metres; zeros when the text is not an explicit two-part size.
        static void NominalSize(string text, out double width, out double height)
        {
            width = height = 0;
            var size = Regex.Split(text ?? "", @"\s*[xX×]\s*").Select(LengthText.ParseMetres).ToArray();
            if (size.Length == 2 && size[0].HasValue && size[1].HasValue) { width = size[0].Value; height = size[1].Value; }
        }

        // Diagnostic only: dumps the world triangles of named components so a rejected
        // reconstruction can be inspected without opening the model by hand.
        public static object DumpMesh(Document document, IEnumerable<string> ids, IEnumerable<string> names)
        {
            var wanted = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var wantedNames = new HashSet<string>(names ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var result = new List<object>();
            foreach (var item in document.Models.RootItems.SelectMany(r => r.DescendantsAndSelf))
            {
                string key = Key(item);
                if (!wanted.Contains(key) && !wantedNames.Contains(item.DisplayName ?? "")) continue;
                var triangles = new List<object>();
                // The component is a metadata group; its geometry lives in child nodes.
                foreach (var node in VisibleGeometry(new Candidate { Item = item, Info = new RejectedComponent() }, Stopwatch.StartNew()))
                    foreach (var t in GeometryReader.Read(document, node).Triangles)
                        triangles.Add(new { A = new[] { t.A.X, t.A.Y, t.A.Z }, B = new[] { t.B.X, t.B.Y, t.B.Z }, C = new[] { t.C.X, t.C.Y, t.C.Z } });
                result.Add(new { Id = key, item.DisplayName, Triangles = triangles });
            }
            return result;
        }

        static List<Candidate> Discover(ModelItem[] roots, HostNetwork network)
        {
            var watch = Stopwatch.StartNew(); var result = new List<Candidate>(); var pending = new Stack<ScanFrame>();
            Action checkBudget = () => {
                if (watch.Elapsed.TotalSeconds > ScanLimitSeconds)
                    throw new InvalidOperationException("桥架候选扫描达到 60 秒上限（已检查 " + network.ScanNodeCount + " 个节点），未生成部分路径图；请缩小模型后重试");
            };
            for (int i = roots.Length - 1; i >= 0; i--) pending.Push(new ScanFrame { Item = roots[i] });
            try
            {
                while (pending.Count > 0)
                {
                    checkBudget(); var frame = pending.Pop(); network.ScanNodeCount++;
                    var metadata = ReadMetadata(frame.Item, checkBudget);
                    bool excluded = frame.Excluded || metadata.Name == "Maintenance Volume";
                    if (IsCandidate(metadata, frame.TrayAncestor))
                    {
                        if (result.Count >= 6000) throw new InvalidOperationException("桥架候选超过 6000 个，请先在较小的模型中实验");
                        // Compute the instance key only after discovery accepts the object, and cache all metadata.
                        result.Add(new Candidate { Item = frame.Item, Depth = frame.Depth, Excluded = excluded, Info = new RejectedComponent {
                            ModelItemId = Key(frame.Item), DisplayName = metadata.Name, RunName = metadata.RunName,
                            Description = metadata.Description, Size = metadata.Size, TypeClassification = metadata.Classification
                        } });
                        network.ScanCandidateCount = result.Count;
                    }
                    checkBudget();
                    bool trayAncestor = frame.TrayAncestor || metadata.Name.EndsWith("-TRAY", StringComparison.OrdinalIgnoreCase);
                    var children = frame.Item.Children.ToArray();
                    for (int i = children.Length - 1; i >= 0; i--) pending.Push(new ScanFrame {
                        Item = children[i], Depth = frame.Depth + 1, TrayAncestor = trayAncestor, Excluded = excluded
                    });
                }
                return result;
            }
            finally { network.ScanElapsedSeconds = watch.Elapsed.TotalSeconds; }
        }

        static ModelItem[] VisibleGeometry(Candidate candidate, Stopwatch watch)
        {
            if (candidate.Excluded) return new ModelItem[0];
            var pending = new Stack<ModelItem>(); var result = new List<ModelItem>(); pending.Push(candidate.Item);
            while (pending.Count > 0)
            {
                if (watch.Elapsed.TotalSeconds > 120) throw new InvalidOperationException("模型提取已达 120 秒上限，本构件未提取");
                var item = pending.Pop();
                // Excluding an ancestor excludes its entire subtree; no repeated AncestorsAndSelf walk.
                if (item.DisplayName == "Maintenance Volume") continue;
                if (item.HasGeometry) result.Add(item);
                var children = item.Children.ToArray();
                for (int i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
            }
            return result.ToArray();
        }

        public static ModelItem Component(ModelItem item)
        {
            while (item != null)
            {
                if (IsCandidate(item)) return item;
                item = item.Parent;
            }
            throw new InvalidOperationException("请点击桥架实体；这个对象缺少可识别的构件类型");
        }

        public bool Valid()
        {
            return Document == NavApp.ActiveDocument && !Document.IsClear &&
                Roots.Length == Document.Models.RootItems.Count() && Roots.All(r => Document.Models.RootItems.Any(x => x.Equals(r)));
        }

        public int Index(ModelItem item)
        {
            var id = Key(Component(item));
            return Graph.Pieces.FindIndex(p => p.Shape.Id == id);
        }

        static CableNetworkOptions ReadNetworkOptions()
        {
            var path=Path.Combine(Path.GetDirectoryName(typeof(HostNetwork).Assembly.Location),"cable-path-settings.json");
            if(!File.Exists(path))return new CableNetworkOptions();
            var options=new JavaScriptSerializer().Deserialize<CableNetworkOptions>(File.ReadAllText(path));
            if(options==null)throw new InvalidOperationException("电缆路径配置为空: "+path);
            return options;
        }

        public static HostSettings ReadHostSettings()
        {
            var path=Path.Combine(Path.GetDirectoryName(typeof(HostNetwork).Assembly.Location),"cable-path-settings.json");
            if(!File.Exists(path))return new HostSettings();
            return new JavaScriptSerializer().Deserialize<HostSettings>(File.ReadAllText(path))??new HostSettings();
        }

        static List<Candidate> Candidates(Document document, ModelItem[] roots, HostNetwork n)
        {
            // Tree discovery depends only on the document, so repeated spatial builds reuse it.
            var c = catalog;
            if (c == null || c.Document != document || c.Roots.Length != roots.Length || !roots.All(r => c.Roots.Any(x => x.Equals(r))))
            {
                var probe = new HostNetwork();
                catalog = c = new Catalog { Document = document, Roots = roots, Candidates = Discover(roots, probe),
                    ScanNodeCount = probe.ScanNodeCount, ScanElapsedSeconds = probe.ScanElapsedSeconds };
            }
            n.ScanNodeCount = c.ScanNodeCount; n.ScanCandidateCount = c.Candidates.Count; n.ScanElapsedSeconds = c.ScanElapsedSeconds;
            // Each build reports its own failure reasons; never share mutable diagnostics between builds.
            return c.Candidates.Select(x => new Candidate { Item = x.Item, Depth = x.Depth, Excluded = x.Excluded, Lower = x.Lower, Upper = x.Upper, Source = x,
                Info = new RejectedComponent { ModelItemId = x.Info.ModelItemId, DisplayName = x.Info.DisplayName, RunName = x.Info.RunName,
                    Description = x.Info.Description, Size = x.Info.Size, TypeClassification = x.Info.TypeClassification } }).ToList();
        }

        static bool InRegion(Candidate candidate, SpatialRegion region, double scale)
        {
            EnsureBox(candidate, scale);
            return region.Intersects(candidate.Lower.Value, candidate.Upper.Value);
        }

        static void EnsureBox(Candidate candidate, double scale)
        {
            if (!candidate.Lower.HasValue)
            {
                var source = candidate.Source;
                var box = candidate.Item.BoundingBox();
                if (box == null || box.IsEmpty) { source.Lower = new Vec(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity); source.Upper = new Vec(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity); }
                else { source.Lower = new Vec(box.Min.X, box.Min.Y, box.Min.Z) * scale; source.Upper = new Vec(box.Max.X, box.Max.Y, box.Max.Z) * scale; }
                candidate.Lower = source.Lower; candidate.Upper = source.Upper;
            }
            if (Vec.IsFinite(candidate.Lower.Value.X)) { candidate.Info.BoxMin = candidate.Lower; candidate.Info.BoxMax = candidate.Upper; }
        }

        // Fills DistanceToRoute for rejected components and lists those within radius of each virtual connector.
        public List<VirtualConnectorSuspect> AnnotateRoute(CableRoute route, double radius = .5)
        {
            var result = new List<VirtualConnectorSuspect>();
            if (route == null) return result;
            var boxed = Rejected.Where(r => r.BoxMin.HasValue).ToList();
            foreach (var r in boxed) r.DistanceToRoute = null;
            foreach (var step in route.Steps)
            {
                var line = step.Centerline; if (line == null || line.Length < 2) continue;
                foreach (var r in boxed)
                    for (int i = 1; i < line.Length; i++)
                    {
                        double d = SpatialRegion.BoxSegmentDistance(line[i - 1], line[i], r.BoxMin.Value, r.BoxMax.Value);
                        if (!r.DistanceToRoute.HasValue || d < r.DistanceToRoute.Value) r.DistanceToRoute = d;
                    }
                var vc = step.Join == null ? null : step.Join.VirtualConnector;
                if (vc == null) continue;
                result.Add(new VirtualConnectorSuspect { EdgeId = step.EdgeId, SourceName = vc.SourceName, TargetName = vc.TargetName, Distance3D = vc.Distance3D,
                    NearbyRejected = boxed.Select(r => new { r, d = SpatialRegion.BoxSegmentDistance(vc.SourcePoint, vc.TargetPoint, r.BoxMin.Value, r.BoxMax.Value) })
                        .Where(x => x.d <= radius).OrderBy(x => x.d)
                        .Select(x => (object)new { x.r.ModelItemId, x.r.DisplayName, x.r.RunName, x.r.TypeClassification, x.r.GeometryFailureReason, Distance = x.d }).ToList() });
            }
            return result;
        }

        // Builds the graph only around the endpoints, doubling the margin while no route exists.
        // Returns the network of the last attempt; route is null when even the largest region has no path.
        public static HostNetwork ReadAround(Document document, Vec a, Vec b, Func<HostNetwork,CableRoute> find, out CableRoute route, out CablePathNotFoundException failure)
        {
            var settings = ReadHostSettings(); route = null; failure = null;
            if (settings.SpatialRegionMargin <= 0)
            {
                var whole = Read(document, null);
                try { route = find(whole); } catch (CablePathNotFoundException e) { failure = e; }
                return whole;
            }
            var tried = new List<double>();
            for (double margin = settings.SpatialRegionMargin; ; margin *= 2)
            {
                tried.Add(margin);
                var n = Read(document, null, null, SpatialRegion.Around(a, b, margin)); n.RegionMarginsTried.AddRange(tried);
                try { route = find(n); failure = null; return n; }
                catch (CablePathNotFoundException e) { failure = e; }
                // A truncated graph will not improve by growing the box further.
                if (n.Incomplete || n.OutsideRegionCount == 0 || margin * 2 > settings.SpatialRegionMaxMargin + 1e-9) return n;
            }
        }

        public static HostNetwork Read(Document document, ModelItem selected,CableNetworkOptions options=null,SpatialRegion region=null)
        {
            // Metadata discovers components. It never partitions the physical connection graph by name or run.
            var roots = document.Models.RootItems.ToArray();
            if (roots.Length == 0) throw new InvalidOperationException("当前文档没有模型");
            var n = new HostNetwork { Document = document, Root = roots[0], Roots = roots, Region = region };
            var candidates = Candidates(document, roots, n);
            int maxPieces = (options ?? ReadNetworkOptions()).MaxPieces;
            var hostSettings = ReadHostSettings();
            if (region != null)
            {
                double scale = UnitConversion.ScaleFactor(document.Units, Units.Meters);
                var inside = candidates.Where(c => InRegion(c, region, scale)).ToList();
                n.OutsideRegionCount = candidates.Count - inside.Count; candidates = inside;
            }
            n.RegionCandidateCount = candidates.Count;
            double boxScale = UnitConversion.ScaleFactor(document.Units, Units.Meters);
            foreach (var candidate in candidates) EnsureBox(candidate, boxScale);
            var pieces = new List<CablePiece>();
            var watch = Stopwatch.StartNew();
            var usedGeometry = new HashSet<string>();
            // Innermost metadata-bearing component owns a geometry instance, avoiding parent/child double counting.
            // Spatial builds fill the piece cap nearest-first: a nested group shares its outermost candidate's distance,
            // so depth ordering inside each group is preserved.
            IEnumerable<Candidate> ordered = candidates.OrderByDescending(c => c.Depth);
            if (region != null)
            {
                var byId = candidates.GroupBy(c => c.Info.ModelItemId).ToDictionary(g => g.Key, g => g.First());
                Func<Candidate, double> distance = c => {
                    var parts = c.Info.ModelItemId.Split('/'); var root = c;
                    for (int k = 1; k < parts.Length; k++) { Candidate outer; if (byId.TryGetValue(string.Join("/", parts, 0, k), out outer)) { root = outer; break; } }
                    return region.SegmentDistance(root.Lower.Value, root.Upper.Value);
                };
                ordered = candidates.Select(c => new { c, d = distance(c) }).OrderBy(x => x.d).ThenByDescending(x => x.c.Depth).Select(x => x.c).ToList();
            }
            foreach (var candidate in ordered)
            {
                var info = candidate.Info;
                try
                {
                    if (string.Equals(info.TypeClassification, "SpliceConnector", StringComparison.OrdinalIgnoreCase))
                    {
                        // Joint hints need no mesh reconstruction and never consume the piece cap.
                        if (!candidate.Lower.HasValue || !Vec.IsFinite(candidate.Lower.Value.X)) throw new InvalidOperationException("SPLICE_CONNECTOR_NO_BOX: 连接件没有包围盒，无法作为桥接提示");
                        var hint = new CableJointHint { Id = info.ModelItemId, Name = info.DisplayName, Kind = "SpliceConnector", Min = candidate.Lower.Value, Max = candidate.Upper.Value };
                        NominalSize(info.Size, out hint.NominalWidth, out hint.NominalHeight);
                        n.JointHints.Add(hint);
                        throw new InvalidOperationException("SPLICE_CONNECTOR_HINT: 连接件不重建中心线，只用包围盒桥接其连接的两个桥架端口");
                    }
                    if (watch.Elapsed.TotalSeconds > 120) { n.Incomplete = true; throw new InvalidOperationException("模型提取已达 120 秒上限，本构件未提取"); }
                    if (pieces.Count >= maxPieces) { n.Incomplete = true; throw new InvalidOperationException("本次端口图达到 " + maxPieces + " 个构件上限，本构件未加入"); }
                    if (string.Equals(info.TypeClassification, "unknown", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("无法识别配件类型；未用名称、规格或包围盒猜测长度");
                    var nodes = VisibleGeometry(candidate, watch);
                    if (nodes.Length == 0) throw new InvalidOperationException("没有实体三角网格（无几何或仅有检修空间；显示隐藏状态不影响建图）");
                    var nodeKeys = nodes.Select(Key).ToArray();
                    if (nodeKeys.Any(usedGeometry.Contains)) throw new InvalidOperationException("与已识别子构件共享同一几何实例，跳过父子重复范围");
                    var mesh = new List<Triangle>();
                    foreach (var node in nodes)
                    {
                        mesh.AddRange(GeometryReader.Read(document, node).Triangles);
                        if (mesh.Count > 200000) throw new InvalidOperationException("合并实体超过 200000 三角面上限");
                    }
                    double nominalWidth, nominalHeight; NominalSize(info.Size, out nominalWidth, out nominalHeight);
                    // A/B switch: with the fallback off these pieces stay rejected exactly as before.
                    if (!hostSettings.StraightStubByNominalSection) { nominalWidth = 0; nominalHeight = 0; }
                    var shape = FittingGeometry.Build(mesh, info.TypeClassification, nominalWidth, nominalHeight);
                    shape.Id = info.ModelItemId; shape.Name = info.DisplayName; shape.System = info.RunName;
                    var piece = new CablePiece { Shape = shape, Domain = "geometry" };
                    if (shape.Ports.Length > 0) { piece.WidthAxis = shape.Ports[0].WidthAxis; piece.HeightAxis = shape.Ports[0].HeightAxis; }
                    pieces.Add(piece); n.Geometry.Add(nodes); n.Recognized.Add(info);
                    foreach (var key in nodeKeys) usedGeometry.Add(key);
                }
                catch (Exception error)
                {
                    if (watch.Elapsed.TotalSeconds > 120) n.Incomplete = true;
                    info.GeometryFailureReason = error.Message;
                    n.Rejected.Add(info);
                }
            }
            n.Graph = new CableNetwork(pieces,options:options??ReadNetworkOptions(),jointHints:n.JointHints);
            return n;
        }
    }
}
