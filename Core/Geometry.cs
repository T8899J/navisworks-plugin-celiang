using System;
using System.Collections.Generic;
using System.Linq;

namespace JiePinPai.TrayMeasurement.Core
{
    public struct Vec
    {
        public double X, Y, Z;
        public Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double this[int i] { get { return i == 0 ? X : i == 1 ? Y : Z; } }
        public static Vec operator +(Vec a, Vec b) { return new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec operator -(Vec a, Vec b) { return new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec operator *(Vec a, double b) { return new Vec(a.X * b, a.Y * b, a.Z * b); }
        public double Dot(Vec b) { return X * b.X + Y * b.Y + Z * b.Z; }
        public Vec Cross(Vec b) { return new Vec(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X); }
        public double Norm { get { return Math.Sqrt(Dot(this)); } }
        public bool Finite { get { return IsFinite(X) && IsFinite(Y) && IsFinite(Z); } }
        public static bool IsFinite(double d) { return !double.IsNaN(d) && !double.IsInfinity(d); }
        public override string ToString() { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F6}, {1:F6}, {2:F6})", X, Y, Z); }
    }

    public struct Triangle
    {
        public Vec A, B, C;
        public Triangle(Vec a, Vec b, Vec c) { A = a; B = b; C = c; }
        public double Area { get { return (B - A).Cross(C - A).Norm * 0.5; } }
    }

    public static class GeometryTransform
    {
        // COM SAFEARRAY may start at 0 or 1. Navisworks stores a column-major 4 x 4 matrix.
        public static double[] ReadArray(Array a, int expected)
        {
            if (a == null || a.Rank != 1 || a.Length != expected) throw new InvalidOperationException("几何数组维度错误。");
            var values = new double[expected];
            for (int i = 0; i < expected; i++) values[i] = Convert.ToDouble(a.GetValue(i + a.GetLowerBound(0)));
            if (values.Any(v => !Vec.IsFinite(v))) throw new InvalidOperationException("几何包含无效坐标。");
            return values;
        }

        public static Vec Apply(Vec p, double[] m)
        {
            double w = m[3] * p.X + m[7] * p.Y + m[11] * p.Z + m[15];
            if (!Vec.IsFinite(w) || Math.Abs(w) < 1e-12) throw new InvalidOperationException("无效的局部到世界变换。");
            var result = new Vec((m[0] * p.X + m[4] * p.Y + m[8] * p.Z + m[12]) / w,
                (m[1] * p.X + m[5] * p.Y + m[9] * p.Z + m[13]) / w,
                (m[2] * p.X + m[6] * p.Y + m[10] * p.Z + m[14]) / w);
            if (!result.Finite) throw new InvalidOperationException("变换后的坐标无效。");
            return result;
        }

        public static bool IsWithinPath(int[] selection, int[] fragment)
        {
            if (selection == null || fragment == null || selection.Length == 0 || fragment.Length < selection.Length) return false;
            for (int i = 0; i < selection.Length; i++) if (selection[i] != fragment[i]) return false;
            return true;
        }
    }

    public sealed class Measurement
    {
        public bool IsStraightCandidate;
        public string Reason;
        public double SpanMetres, WidthMetres, HeightMetres, EigenRatio, SectionVariation;
        public Vec Axis, Start, End, CrossAxis1, CrossAxis2, PortStart, PortEnd;
    }

    public static class StraightMeasurement
    {
        // Surface-integrated PCA: triangle subdivision does not change the covariance.
        // All values entering this class are in metres. It never reads model attributes.
        public static Measurement Measure(IList<Triangle> mesh, bool knownStraight = false)
        {
            if (mesh == null || mesh.Count == 0) throw new InvalidOperationException("没有可测量的三角面。");
            Vec origin = mesh[0].A, first = new Vec();
            var second = new double[3, 3];
            double totalArea = 0;
            foreach (Triangle world in mesh)
            {
                if (!world.A.Finite || !world.B.Finite || !world.C.Finite) throw new InvalidOperationException("存在无效坐标。");
                var t = new Triangle(world.A - origin, world.B - origin, world.C - origin);
                double area = t.Area;
                if (area <= 1e-18) continue;
                Vec sum = t.A + t.B + t.C;
                totalArea += area;
                first += sum * (area / 3);
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        second[i, j] += area / 12 * (sum[i] * sum[j] + t.A[i] * t.A[j] + t.B[i] * t.B[j] + t.C[i] * t.C[j]);
            }
            if (totalArea <= 1e-15 || !Vec.IsFinite(totalArea)) throw new InvalidOperationException("几何面退化或尺寸无效。");
            Vec mean = first * (1 / totalArea);
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) second[i, j] = second[i, j] / totalArea - mean[i] * mean[j];
            double[] eigenvalues;
            Vec[] axes = Eigenvectors(second, out eigenvalues);
            var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            var projected = new List<Triangle>(mesh.Count);
            foreach (Triangle t in mesh)
            {
                Vec a = Project(t.A - origin - mean, axes), b = Project(t.B - origin - mean, axes), c = Project(t.C - origin - mean, axes);
                projected.Add(new Triangle(a, b, c));
                foreach (Vec p in new[] { a, b, c })
                    for (int i = 0; i < 3; i++) { min[i] = Math.Min(min[i], p[i]); max[i] = Math.Max(max[i], p[i]); }
            }
            var result = new Measurement {
                SpanMetres = max[0] - min[0], WidthMetres = max[1] - min[1], HeightMetres = max[2] - min[2],
                EigenRatio = eigenvalues[0] / Math.Max(eigenvalues[1], 1e-30), Axis = axes[0],
                Start = origin + mean + axes[0] * min[0], End = origin + mean + axes[0] * max[0],
                CrossAxis1 = axes[1], CrossAxis2 = axes[2],
                PortStart = origin + mean + axes[0] * min[0] + axes[1] * ((min[1] + max[1]) / 2) + axes[2] * ((min[2] + max[2]) / 2),
                PortEnd = origin + mean + axes[0] * max[0] + axes[1] * ((min[1] + max[1]) / 2) + axes[2] * ((min[2] + max[2]) / 2)
            };
            double crossSpan = Math.Max(result.WidthMetres, result.HeightMetres);
            // Explicit source metadata can disambiguate a short straight. Cross-section checks still apply.
            if (crossSpan < 1e-6 || result.SpanMetres < crossSpan * (knownStraight ? 1.05 : 1.5) || result.EigenRatio < (knownStraight ? 1.02 : 1.3))
                return Reject(result, "UNKNOWN_GEOMETRY：纵向不够明确，可能为短节、宽件或配件。");

            // Cross-sections at 19 stations: catches common elbows, tees, reducers and gaps.
            // A candidate is not semantic proof of a straight cable tray; manual validation remains required.
            double[] reference = null;
            double maxVariation = 0;
            for (int station = 1; station <= 19; station++)
            {
                double x = min[0] + result.SpanMetres * station / 20;
                double[] section = Section(projected, x);
                if (section == null) return Reject(result, "UNKNOWN_GEOMETRY：截面不连续，可能选中了多个分离构件。");
                if (reference == null) reference = section;
                for (int i = 0; i < 4; i++) maxVariation = Math.Max(maxVariation, Math.Abs(section[i] - reference[i]));
            }
            result.SectionVariation = maxVariation;
            if (maxVariation > Math.Max(0.002, crossSpan * 0.025))
                return Reject(result, "UNKNOWN_GEOMETRY：截面位置或尺寸变化，疑似弯头、三通、变径或组合件。");
            result.IsStraightCandidate = true;
            result.Reason = "直线候选 · 实验值，请与人工测量对照";
            return result;
        }

        private static Measurement Reject(Measurement r, string reason) { r.Reason = reason; return r; }
        private static Vec Project(Vec p, Vec[] axes) { return new Vec(p.Dot(axes[0]), p.Dot(axes[1]), p.Dot(axes[2])); }

        private static double[] Section(List<Triangle> mesh, double x)
        {
            var r = new[] { double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity };
            foreach (Triangle t in mesh) { Edge(t.A, t.B, x, r); Edge(t.B, t.C, x, r); Edge(t.C, t.A, x, r); }
            return double.IsInfinity(r[0]) ? null : r;
        }

        private static void Edge(Vec a, Vec b, double x, double[] r)
        {
            if (x < Math.Min(a.X, b.X) || x > Math.Max(a.X, b.X) || Math.Abs(a.X - b.X) < 1e-15) return;
            Vec p = a + (b - a) * ((x - a.X) / (b.X - a.X));
            r[0] = Math.Min(r[0], p.Y); r[1] = Math.Max(r[1], p.Y); r[2] = Math.Min(r[2], p.Z); r[3] = Math.Max(r[3], p.Z);
        }

        private static Vec[] Eigenvectors(double[,] input, out double[] values)
        {
            var a = (double[,])input.Clone();
            var v = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int iteration = 0; iteration < 50; iteration++)
            {
                int p = 0, q = 1;
                if (Math.Abs(a[0, 2]) > Math.Abs(a[p, q])) { p = 0; q = 2; }
                if (Math.Abs(a[1, 2]) > Math.Abs(a[p, q])) { p = 1; q = 2; }
                if (Math.Abs(a[p, q]) <= 1e-14 * Math.Max(1e-30, Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]))) break;
                double angle = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                double c = Math.Cos(angle), s = Math.Sin(angle), app = a[p, p], aqq = a[q, q], apq = a[p, q];
                for (int k = 0; k < 3; k++) if (k != p && k != q)
                {
                    double kp = a[k, p], kq = a[k, q];
                    a[k, p] = a[p, k] = c * kp - s * kq;
                    a[k, q] = a[q, k] = s * kp + c * kq;
                }
                a[p, p] = c * c * app - 2 * s * c * apq + s * s * aqq;
                a[q, q] = s * s * app + 2 * s * c * apq + c * c * aqq;
                a[p, q] = a[q, p] = 0;
                for (int k = 0; k < 3; k++) { double kp = v[k, p], kq = v[k, q]; v[k, p] = c * kp - s * kq; v[k, q] = s * kp + c * kq; }
            }
            int[] order = Enumerable.Range(0, 3).OrderByDescending(i => a[i, i]).ToArray();
            values = order.Select(i => a[i, i]).ToArray();
            return order.Select(i => new Vec(v[0, i], v[1, i], v[2, i])).ToArray();
        }
    }
}
