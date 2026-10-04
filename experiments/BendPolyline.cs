using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public static partial class FittingGeometry
    {
        sealed class RailCorner
        {
            public Vec Point;
            public double Before, After;
        }

        // Some exports contain a folded U-channel, not sampled concentric circles. Recover
        // the two open ends from a continuous face outline, then pair the two side rails.
        // Radius labels and neighbouring components never supply the fitting's length.
        static bool TryPolylineBend(IList<Triangle> mesh, IList<Vec> vertices, string kind, out Part result)
        {
            result = null;
            var origin = vertices[0];
            foreach (var normal in CandidateAxes(mesh))
            {
                Vec u, v; Basis(normal, out u, out v);
                var depth = vertices.Select(p => (p - origin).Dot(normal)).ToArray();
                double lo = depth.Min(), hi = depth.Max();
                if (hi - lo < .002) continue;
                foreach (var outline in PlanarOutlines(mesh, origin, normal, u, v))
                {
                    var polygon = Simplify(outline);
                    if (polygon.Count < 6 || polygon.Count > 128) continue;
                    if (SignedArea(polygon) < 0) polygon.Reverse();
                    // A fitting with detached geometry or an extra branch is not a two-port bend.
                    if (vertices.Any(p => !InsideOutline(new Vec((p-origin).Dot(u), (p-origin).Dot(v), 0), polygon))) continue;
                    for (int a = 0; a < polygon.Count; a++) for (int b = a + 2; b < polygon.Count; b++)
                    {
                        Vec outA, outB; double sizeA, sizeB;
                        if (!Mouth(polygon, a, out outA, out sizeA) || !Mouth(polygon, b, out outB, out sizeB)) continue;
                        if (Math.Abs(sizeA-sizeB) > Tol * 2) continue;
                        var direction = outA * -1;
                        double turn = Math.Atan2(Cross2(direction, outB), direction.Dot(outB));
                        double sweep = Math.Abs(turn);
                        double expected = kind.EndsWith("45", StringComparison.Ordinal) ? Math.PI/4 : kind.EndsWith("90", StringComparison.Ordinal) ? Math.PI/2 : sweep;
                        if (sweep < Math.PI/12 || sweep > Math.PI*.99 || Math.Abs(sweep-expected) > .008) continue;
                        var first = new List<Vec>(); var second = new List<Vec>();
                        for (int i = a+1; i <= b; i++) first.Add(polygon[i]);
                        for (int i = a; ; i = (i + polygon.Count - 1) % polygon.Count)
                        {
                            second.Add(polygon[i]);
                            if (i == (b+1) % polygon.Count) break;
                        }
                        List<RailCorner> left, right;
                        if (!Rail(first, direction, outB, Math.Sign(turn), sweep, out left) ||
                            !Rail(second, direction, outB, Math.Sign(turn), sweep, out right)) continue;
                        // A bevel may add corners to only one rail. Pair corners by their shared
                        // turning-angle interval, not vertex index or fractional perimeter length.
                        var pairs = (from x in left from y in right
                            let low = Math.Max(x.Before,y.Before) let high = Math.Min(x.After,y.After)
                            where high-low > .0002
                            orderby low, high
                            select (x.Point+y.Point)*.5).ToList();
                        double envelope = (from x in left from y in right
                            where Math.Min(x.After,y.After)-Math.Max(x.Before,y.Before)>.0002
                            select (x.Point-y.Point).Norm).DefaultIfEmpty(sizeA).Max();
                        if (pairs.Count == 0) continue;
                        var line = new List<Vec> { (first[0]+second[0])*.5 };
                        line.AddRange(pairs); line.Add((first.Last()+second.Last())*.5);
                        if (line.Zip(line.Skip(1),(x,y)=>(y-x).Norm).Any(d=>d<Tol)) continue;
                        if (Unit(line[1]-line[0]).Dot(direction) < .9999 || Unit(line.Last()-line[line.Count-2]).Dot(outB) < .9999) continue;
                        var world = line.Select(p => origin + u*p.X + v*p.Y + normal*((lo+hi)/2)).ToArray();
                        double sectionSize = (sizeA+sizeB)/2;
                        if (!PolylineSections(mesh, world, normal, sectionSize, Math.Max(sectionSize,envelope), hi-lo)) continue;
                        bool riser = kind.StartsWith("Riser",StringComparison.OrdinalIgnoreCase);
                        var crossA = u*(-outA.Y)+v*outA.X; var crossB = u*(-outB.Y)+v*outB.X;
                        result = TwoPort(kind, world, u*outA.X+v*outA.Y, u*outB.X+v*outB.Y,
                            riser?hi-lo:sectionSize, riser?sectionSize:hi-lo,
                            riser?hi-lo:sectionSize, riser?sectionSize:hi-lo,
                            riser?normal:crossA, riser?crossA:normal, riser?normal:crossB, riser?crossB:normal);
                        result.InternalEdges[0].RequiresReview = true;
                        result.InternalEdges[0].ReviewReason = "折线配件按两侧实际轮廓重建中心线；折弯/倒角处需人工复核";
                        return true;
                    }
                }
            }
            return false;
        }

        static bool Mouth(IList<Vec> polygon, int edge, out Vec outward, out double size)
        {
            int count = polygon.Count;
            var delta = polygon[(edge+1)%count]-polygon[edge]; size=delta.Norm; outward=new Vec();
            if (size < .002) return false;
            var across=Unit(delta); outward=new Vec(across.Y,-across.X,0);
            var before=Unit(polygon[edge]-polygon[(edge+count-1)%count]);
            var after=Unit(polygon[(edge+2)%count]-polygon[(edge+1)%count]);
            return before.Dot(outward)>.9999 && after.Dot(outward)<-.9999;
        }

        static bool Rail(IList<Vec> points, Vec first, Vec last, int sign, double sweep, out List<RailCorner> corners)
        {
            corners=new List<RailCorner>();
            if(points.Count<3)return false;
            var directions=points.Zip(points.Skip(1),(a,b)=>Unit(b-a)).ToArray();
            if(directions[0].Dot(first)<.9999 || directions.Last().Dot(last)<.9999)return false;
            double angle=0;
            for(int i=1;i<directions.Length;i++)
            {
                double turn=sign*Math.Atan2(Cross2(directions[i-1],directions[i]),directions[i-1].Dot(directions[i]));
                if(turn<.0002 || turn>Math.PI*.75)return false;
                corners.Add(new RailCorner { Point=points[i],Before=angle,After=angle+turn }); angle+=turn;
            }
            return Math.Abs(angle-sweep)<.008;
        }

        static bool InsideOutline(Vec point, IList<Vec> polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                var a=polygon[j];var b=polygon[i];var d=b-a;
                double t=Math.Max(0,Math.Min(1,(point-a).Dot(d)/d.Dot(d)));
                if((point-a-d*t).Norm<=Tol)return true;
                if((a.Y>point.Y)!=(b.Y>point.Y) && point.X<(b.X-a.X)*(point.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
            }
            return inside;
        }

        static bool PolylineSections(IList<Triangle> mesh, Vec[] line, Vec normal, double width, double envelope, double depth)
        {
            for(int i=1;i<line.Length;i++)
            {
                var delta=line[i]-line[i-1];var tangent=Unit(delta);var across=Unit(normal.Cross(tangent));
                foreach(double fraction in new[]{.2,.5,.8})
                {
                    var center=line[i-1]+delta*fraction;
                    var section=Slice(mesh,center,tangent,across,normal,0);
                    if(section==null)return false;
                    // Bevels can widen the local envelope, but cannot introduce missing sections,
                    // a different extrusion depth, or an unrelated branch disguised as a bend.
                    if(section[0]>-width*.30 || section[1]<width*.30 || section[1]-section[0]>envelope+Tol ||
                        Math.Abs(section[2]+depth/2)>Tol*2 || Math.Abs(section[3]-depth/2)>Tol*2)return false;
                }
            }
            return true;
        }
    }
}
