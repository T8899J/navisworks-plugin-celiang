using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using JiePinPai.TrayMeasurement.Core;
using Com = Autodesk.Navisworks.Api.Interop.ComApi;

namespace JiePinPai.TrayMeasurement
{
    public sealed class GeometrySnapshot
    {
        public readonly List<Triangle> Triangles = new List<Triangle>();
        public string ItemName, Path, DocumentUnits;
        public int AcceptedFragments, OtherInstanceFragments, DuplicateFragments;
        public double ElapsedMilliseconds;
        public Measurement Result;
        public readonly List<LengthProperty> Lengths = new List<LengthProperty>();
    }

    public sealed class LengthProperty
    {
        public string Source;
        public double? Metres;
        public override string ToString() { return Source; }
    }

    public static class GeometryReader
    {
        public static GeometrySnapshot Read(Document document, ModelItem item)
        {
            var watch = Stopwatch.StartNew();
            double scale = UnitConversion.ScaleFactor(document.Units, Units.Meters);
            if (!Vec.IsFinite(scale) || scale <= 0) throw new InvalidOperationException("无法识别文档单位。");
            var path = ComApiBridge.ToInwOaPath(item);
            int[] selectedPath = PathArray(path);
            if (selectedPath.Length <= 1) throw new InvalidOperationException("请选择一段具体桥架，不能测整个文件根节点。");
            var snapshot = new GeometrySnapshot { ItemName = item.DisplayName, Path = string.Join("/", selectedPath), DocumentUnits = document.Units.ToString() };
            var seen = new HashSet<string>();
            var retainedFragments = new List<Com.InwOaFragment3>();
            var collector = new PrimitiveCollector(snapshot.Triangles, watch);
            foreach (Com.InwOaFragment3 fragment in path.Fragments())
            {
                if (watch.Elapsed.TotalSeconds > 20) throw new InvalidOperationException("几何提取超过 20 秒，请选择更小的单段构件。");
                int[] fragmentPath = PathArray(fragment.path);
                if (!GeometryTransform.IsWithinPath(selectedPath, fragmentPath)) { snapshot.OtherInstanceFragments++; continue; }
                // COM identity plus instance path; a path can legitimately contain multiple fragments.
                IntPtr identity = Marshal.GetIUnknownForObject(fragment);
                string key;
                try { key = string.Join("/", fragmentPath) + ":" + identity.ToInt64(); }
                finally { Marshal.Release(identity); }
                if (!seen.Add(key)) { snapshot.DuplicateFragments++; continue; }
                retainedFragments.Add(fragment);
                if (++snapshot.AcceptedFragments > 256) throw new InvalidOperationException("选中范围超过 256 个 Fragment，请缩小到单段桥架。");
                collector.Matrix = GeometryTransform.ReadArray((Array)fragment.GetLocalToWorldMatrix().Matrix, 16);
                collector.Scale = scale;
                fragment.GenerateSimplePrimitives((Com.nwEVertexProperty)0, collector);
                if (collector.Failure != null) throw new InvalidOperationException(collector.Failure);
            }
            GC.KeepAlive(retainedFragments);
            if (snapshot.Triangles.Count == 0) throw new InvalidOperationException("当前实例没有可用三角面。请在选择树中选择包含桥架实体的节点；线、点不作为实体长度依据。");

            // Detect wrong instance, matrix convention or unit scale instead of quietly returning a length.
            BoundingBox3D box = item.BoundingBox();
            if (box.IsEmpty) throw new InvalidOperationException("选中构件没有有效包围盒。");
            var lower = new Vec(box.Min.X, box.Min.Y, box.Min.Z) * scale;
            var upper = new Vec(box.Max.X, box.Max.Y, box.Max.Z) * scale;
            double tolerance = Math.Max(0.002, (upper - lower).Norm * 1e-4);
            foreach (Triangle triangle in snapshot.Triangles)
                foreach (Vec vertex in new[] { triangle.A, triangle.B, triangle.C })
                    for (int axis = 0; axis < 3; axis++)
                        if (vertex[axis] < lower[axis] - tolerance || vertex[axis] > upper[axis] + tolerance)
                            throw new InvalidOperationException("几何顶点超出选中实例范围：变换或单位校验失败，已停止测量。");

            snapshot.Result = StraightMeasurement.Measure(snapshot.Triangles);
            ReadLengths(item, scale, snapshot.Lengths);
            string names = item.DisplayName;
            // Explicit fitting names override the purely geometric heuristic.
            if (System.Text.RegularExpressions.Regex.IsMatch(names ?? "", @"弯头|三通|四通|变径|异径|elbow|\btee\b|reducer|bend", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            { snapshot.Result.IsStraightCandidate = false; snapshot.Result.Reason = "FITTING：名称包含配件标识，仅提供几何跨度诊断。"; }
            snapshot.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
            return snapshot;
        }

        private static int[] PathArray(Com.InwOaPath path)
        {
            var array = path.ArrayData as Array;
            if (array == null || array.Rank != 1) throw new InvalidOperationException("无法核验 Fragment 的实例路径。");
            var result = new int[array.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Convert.ToInt32(array.GetValue(array.GetLowerBound(0) + i));
            return result;
        }

        private static void ReadLengths(ModelItem item, double scale, List<LengthProperty> result)
        {
            // Use the nearest level that actually exposes a length. Never add values from different levels.
            for (int level = 0; item != null && level < 5; level++, item = item.Parent)
            {
                foreach (PropertyCategory category in item.PropertyCategories)
                    foreach (DataProperty property in category.Properties)
                    {
                        string name = property.DisplayName ?? "";
                        if (!(name.IndexOf("长度", StringComparison.Ordinal) >= 0 || name.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        try
                        {
                            double? metres = null;
                            var value = property.Value;
                            if (value.IsDoubleLength) metres = value.ToDoubleLength() * scale;
                            else if (value.IsDisplayString) metres = LengthText.ParseMetres(value.ToDisplayString());
                            if (metres.HasValue && (!Vec.IsFinite(metres.Value) || metres <= 0)) metres = null;
                            result.Add(new LengthProperty { Metres = metres, Source = item.DisplayName + " / " + category.DisplayName + " / " + name + " = " + value.ToString() + (metres.HasValue ? "" : "（单位未确认）") });
                        }
                        catch (Exception ex) { result.Add(new LengthProperty { Source = name + "（读取失败：" + ex.Message + "）" }); }
                    }
                if (result.Count > 0) return;
            }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class PrimitiveCollector : Com.InwSimplePrimitivesCB
    {
        private readonly List<Triangle> triangles;
        private readonly Stopwatch watch;
        public double[] Matrix;
        public double Scale;
        public string Failure;
        public PrimitiveCollector(List<Triangle> triangles, Stopwatch watch) { this.triangles = triangles; this.watch = watch; }
        private Vec Read(Com.InwSimpleVertex vertex)
        {
            double[] p = GeometryTransform.ReadArray((Array)vertex.coord, 3);
            return GeometryTransform.Apply(new Vec(p[0], p[1], p[2]), Matrix) * Scale;
        }
        public void Triangle(Com.InwSimpleVertex a, Com.InwSimpleVertex b, Com.InwSimpleVertex c)
        {
            if (Failure != null) return;
            // Do not throw through the COM callback; fail the entire result after GenerateSimplePrimitives.
            if (triangles.Count >= 200000 || watch.Elapsed.TotalSeconds > 20) { Failure = "几何超过实验上限（20 秒 / 200000 面），请缩小选择范围。"; return; }
            try { triangles.Add(new Triangle(Read(a), Read(b), Read(c))); }
            catch (Exception ex) { Failure = "读取三角面失败：" + ex.Message; }
        }
        public void Line(Com.InwSimpleVertex a, Com.InwSimpleVertex b) { }
        public void Point(Com.InwSimpleVertex a) { }
        public void SnapPoint(Com.InwSimpleVertex a) { }
    }
}
