using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    }

    internal sealed class HostNetwork
    {
        public CableNetwork Graph;
        public readonly List<ModelItem[]> Geometry = new List<ModelItem[]>();
        public readonly List<RejectedComponent> Rejected = new List<RejectedComponent>();
        public readonly List<RejectedComponent> Recognized = new List<RejectedComponent>();
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
        }

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
                    bool excluded = frame.Excluded || frame.Item.IsHidden || metadata.Name == "Maintenance Volume";
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
                if (item.IsHidden || item.DisplayName == "Maintenance Volume") continue;
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

        public static HostNetwork Read(Document document, ModelItem selected)
        {
            // Metadata discovers components. It never partitions the physical connection graph by name or run.
            var roots = document.Models.RootItems.ToArray();
            if (roots.Length == 0) throw new InvalidOperationException("当前文档没有模型");
            var n = new HostNetwork { Document = document, Root = roots[0], Roots = roots };
            var candidates = Discover(roots, n);
            var pieces = new List<CablePiece>();
            var watch = Stopwatch.StartNew();
            var usedGeometry = new HashSet<string>();
            // Innermost metadata-bearing component owns a geometry instance, avoiding parent/child double counting.
            foreach (var candidate in candidates.OrderByDescending(c => c.Depth))
            {
                var info = candidate.Info;
                try
                {
                    if (watch.Elapsed.TotalSeconds > 120) { n.Incomplete = true; throw new InvalidOperationException("模型提取已达 120 秒上限，本构件未提取"); }
                    if (pieces.Count >= 2000) { n.Incomplete = true; throw new InvalidOperationException("本次端口图达到 2000 个构件上限，本构件未加入"); }
                    if (string.Equals(info.TypeClassification, "unknown", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("无法识别配件类型；未用名称、规格或包围盒猜测长度");
                    var nodes = VisibleGeometry(candidate, watch);
                    if (nodes.Length == 0) throw new InvalidOperationException("没有可见的实体三角网格（可能已隐藏、无几何或仅有检修空间）");
                    var nodeKeys = nodes.Select(Key).ToArray();
                    if (nodeKeys.Any(usedGeometry.Contains)) throw new InvalidOperationException("与已识别子构件共享同一几何实例，跳过父子重复范围");
                    var mesh = new List<Triangle>();
                    foreach (var node in nodes)
                    {
                        mesh.AddRange(GeometryReader.Read(document, node).Triangles);
                        if (mesh.Count > 200000) throw new InvalidOperationException("合并实体超过 200000 三角面上限");
                    }
                    var shape = FittingGeometry.Build(mesh, info.TypeClassification);
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
            n.Graph = new CableNetwork(pieces);
            return n;
        }
    }
}
