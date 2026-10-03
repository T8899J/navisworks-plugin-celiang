using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public sealed class Port
    {
        public Vec Point, Outward;
        public double Width, Height;
    }
    public sealed class Part
    {
        public string Id, Name, System, Kind;
        public Port[] Ports;
        public Vec[] Centerline;
        public double Length { get { return Centerline.Zip(Centerline.Skip(1),(a,b)=>(a-b).Norm).Sum(); } }
    }
    public sealed class Connection
    {
        public int A, B, PortA, PortB;
        public double Gap;
    }
    public sealed class Route
    {
        public int[] Items;
        public double Length, JointGaps;
    }
    public static class RouteGeometry
    {
        public static Part Straight(IList<Triangle> mesh, double nominalWidth, double nominalHeight)
        {
            var m=StraightMeasurement.Measure(mesh,true);
            if(!m.IsStraightCandidate) throw new InvalidOperationException("直段几何未通过截面检查");
            if(Math.Abs(m.WidthMetres-nominalWidth)>.05 || Math.Abs(m.HeightMetres-nominalHeight)>.005)
                throw new InvalidOperationException("直段截面与规格不符");
            return new Part { Kind="straight", Centerline=new[]{m.PortStart,m.PortEnd}, Ports=new[]{
                new Port{Point=m.PortStart,Outward=m.Axis*(-1),Width=m.WidthMetres,Height=m.HeightMetres},
                new Port{Point=m.PortEnd,Outward=m.Axis,Width=m.WidthMetres,Height=m.HeightMetres}} };
        }

        // Deliberately limited to source-labelled 90-degree vertical risers with two side profiles.
        // Reconstruct the polygonal model centerline; do not substitute a catalog radius or a PCA span.
        public static Part Riser90(IList<Triangle> mesh, double nominalWidth, double nominalHeight)
        {
            var m=StraightMeasurement.Measure(mesh);
            var vertices=mesh.SelectMany(t=>new[]{t.A,t.B,t.C}).ToArray();
            Vec origin=vertices[0];
            foreach(var normal in new[]{m.Axis,m.CrossAxis1,m.CrossAxis2})
            {
                var depth=vertices.Select(p=>(p-origin).Dot(normal)).ToArray();
                double lo=depth.Min(),hi=depth.Max();
                if(Math.Abs(hi-lo-nominalWidth)>.05) continue;
                if(depth.Any(d=>Math.Min(Math.Abs(d-lo),Math.Abs(d-hi))>.0005)) continue;
                Vec u=Math.Abs(normal.Z)<.8?normal.Cross(new Vec(0,0,1)):normal.Cross(new Vec(0,1,0));
                u=u*(1/u.Norm); Vec v=normal.Cross(u);
                var points=new List<Vec>();
                foreach(var world in vertices)
                {
                    var p=new Vec((world-origin).Dot(u),(world-origin).Dot(v),0);
                    if(!points.Any(q=>(p-q).Norm<.0003)) points.Add(p);
                }
                if(points.Count<10 || points.Count>64) continue;
                // Each profile point must occur on both side rails, preventing a one-sided surface from passing.
                if(points.Any(p=>!new[]{lo,hi}.All(z=>vertices.Any(w=>
                    Math.Abs((w-origin).Dot(normal)-z)<.0005 &&
                    (new Vec((w-origin).Dot(u),(w-origin).Dot(v),0)-p).Norm<.0005)))) continue;
                for(int a=0;a<points.Count-2;a++) for(int b=a+1;b<points.Count-1;b++) for(int c=b+1;c<points.Count;c++)
                {
                    Vec center;
                    if(!Circle(points[a],points[b],points[c],out center)) continue;
                    var radii=points.Select(p=>(p-center).Norm).ToArray();
                    double inner=radii.Min(),outer=radii.Max();
                    if(inner<.05 || Math.Abs(outer-inner-nominalHeight)>.002) continue;
                    if(radii.Any(r=>Math.Min(r-inner,outer-r)>.0005)) continue;
                    var inside=points.Where((p,i)=>Math.Abs(radii[i]-inner)<.0005).ToArray();
                    var outside=points.Where((p,i)=>Math.Abs(radii[i]-outer)<.0005).ToArray();
                    if(inside.Length<5 || inside.Length!=outside.Length) continue;
                    var stations=new List<Vec>();
                    foreach(var p in inside)
                    {
                        var dir=(p-center)*(1/(p-center).Norm);
                        var q=outside.OrderBy(o=>((o-center)*(1/(o-center).Norm)-dir).Norm).First();
                        if(((q-center)*(1/(q-center).Norm)-dir).Norm>.001) break;
                        stations.Add((p+q)*.5);
                    }
                    if(stations.Count!=inside.Length) continue;
                    var sorted=stations.OrderBy(p=>Angle(p-center)).ToArray();
                    int cut=0; double largest=-1;
                    for(int i=0;i<sorted.Length;i++)
                    {
                        double gap=Angle(sorted[(i+1)%sorted.Length]-center)-Angle(sorted[i]-center);
                        if(gap<=0) gap+=2*Math.PI;
                        if(gap>largest){largest=gap;cut=(i+1)%sorted.Length;}
                    }
                    if(Math.Abs((2*Math.PI-largest)-Math.PI/2)>.005) continue;
                    sorted=Enumerable.Range(0,sorted.Length).Select(i=>sorted[(i+cut)%sorted.Length]).ToArray();
                    bool gaps=false;
                    for(int i=1;i<sorted.Length;i++) if(Math.Acos(Clamp((sorted[i]-center).Dot(sorted[i-1]-center)/((sorted[i]-center).Norm*(sorted[i-1]-center).Norm)))>Math.PI/6) gaps=true;
                    if(gaps) continue;
                    Vec basis=origin+normal*((lo+hi)/2);
                    var line=sorted.Select(p=>basis+u*p.X+v*p.Y).ToArray();
                    var rad0=(sorted[0]-center)*(1/(sorted[0]-center).Norm);
                    var rad1=(sorted[sorted.Length-1]-center)*(1/(sorted[sorted.Length-1]-center).Norm);
                    Vec t0=u*(-rad0.Y)+v*rad0.X, t1=u*(-rad1.Y)+v*rad1.X;
                    if(t0.Dot(line[1]-line[0])>0) t0=t0*(-1);
                    if(t1.Dot(line[line.Length-2]-line[line.Length-1])>0) t1=t1*(-1);
                    return new Part { Kind="riser90", Centerline=line, Ports=new[]{
                        new Port{Point=line[0],Outward=t0,Width=hi-lo,Height=outer-inner},
                        new Port{Point=line[line.Length-1],Outward=t1,Width=hi-lo,Height=outer-inner}} };
                }
            }
            throw new InvalidOperationException("弯头几何不是可确认的等截面 90° 竖向弯");
        }
        static double Clamp(double d){return Math.Max(-1,Math.Min(1,d));}
        static double Angle(Vec p){var a=Math.Atan2(p.Y,p.X);return a<0?a+2*Math.PI:a;}
        static bool Circle(Vec a,Vec b,Vec c,out Vec center)
        {
            Vec p=b-a,q=c-a; double d=2*(p.X*q.Y-p.Y*q.X);
            center=new Vec(); if(Math.Abs(d)<1e-10)return false;
            center=a+new Vec((p.Dot(p)*q.Y-q.Dot(q)*p.Y)/d,(p.X*q.Dot(q)-q.X*p.Dot(p))/d,0);
            return center.Finite;
        }
        public static List<Connection> Connect(IList<Part> parts,double tolerance)
        {
            if(!Vec.IsFinite(tolerance)||tolerance<=0||tolerance>.02) throw new ArgumentOutOfRangeException("tolerance");
            var result=new List<Connection>();
            for(int a=0;a<parts.Count;a++) for(int b=a+1;b<parts.Count;b++)
            {
                if(string.IsNullOrEmpty(parts[a].System)||parts[a].System!=parts[b].System) continue;
                for(int i=0;i<2;i++) for(int j=0;j<2;j++)
                {
                    var p=parts[a].Ports[i];var q=parts[b].Ports[j]; double gap=(p.Point-q.Point).Norm;
                    if(gap>tolerance || p.Outward.Dot(q.Outward)>-Math.Cos(Math.PI/60)) continue;
                    if(Math.Abs(p.Width-q.Width)>.003 || Math.Abs(p.Height-q.Height)>.003)continue;
                    result.Add(new Connection{A=a,B=b,PortA=i,PortB=j,Gap=gap});
                }
            }
            // No silent choice between overlapping duplicates or ambiguous sockets.
            var counts=new Dictionary<string,int>();
            foreach(var e in result)foreach(var key in new[]{e.A+":"+e.PortA,e.B+":"+e.PortB}){if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;}
            if(counts.Values.Any(n=>n>1))throw new InvalidOperationException("接头存在多个候选，需要复核");
            return result;
        }
        public static Route Find(IList<Part> parts,IList<Connection> edges,int start,int end)
        {
            if(start<0||end<0||start>=parts.Count||end>=parts.Count) throw new ArgumentOutOfRangeException();
            var distance=Enumerable.Repeat(double.PositiveInfinity,parts.Count).ToArray();
            var previous=Enumerable.Repeat(-1,parts.Count).ToArray();var used=new bool[parts.Count];
            distance[start]=parts[start].Length;
            for(int step=0;step<parts.Count;step++)
            {
                int a=-1; for(int i=0;i<parts.Count;i++)if(!used[i]&&(a<0||distance[i]<distance[a]))a=i;
                if(a<0||double.IsInfinity(distance[a]))break;
                used[a]=true; if(a==end)break;
                foreach(var e in edges.Where(e=>e.A==a||e.B==a))
                { int b=e.A==a?e.B:e.A;double d=distance[a]+e.Gap+parts[b].Length;if(d<distance[b]){distance[b]=d;previous[b]=a;} }
            }
            if(double.IsInfinity(distance[end]))throw new InvalidOperationException("未找到连续路径");
            var path=new List<int>();for(int a=end;a!=-1;a=previous[a])path.Add(a);path.Reverse();
            double sum=path.Sum(i=>parts[i].Length);
            return new Route{Items=path.ToArray(),Length=distance[end],JointGaps=distance[end]-sum};
        }
    }
}
