using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public sealed class CablePiece
    {
        public Part Shape;
        public string Domain;
        public Vec WidthAxis, HeightAxis;
    }
    public sealed class CableLocation
    {
        public int Piece;
        public double Station;
        public Vec Point;
    }
    public sealed class CableJoin
    {
        public CableLocation A, B;
        public string Kind;
        public bool RequiresReview;
        public double SurfaceGap;
        public double Length { get { return (A.Point-B.Point).Norm; } }
    }
    public sealed class CableStep
    {
        public int Piece;
        public double From, To, Length;
        public Vec A, B;
        public CableJoin Join;
    }
    public sealed class CableRoute
    {
        public double Length;
        public List<CableStep> Steps;
        public bool RequiresReview;
        public int[] Pieces;
    }
    public sealed class CableNetwork
    {
        public readonly List<CablePiece> Pieces;
        public readonly List<CableJoin> Joins=new List<CableJoin>();
        public readonly List<string> Ambiguities=new List<string>();
        public readonly double Tolerance;
        static readonly double CosAngle=Math.Cos(Math.PI/60);
        public CableNetwork(List<CablePiece> pieces,double tolerance=.002)
        {
            if(!Vec.IsFinite(tolerance)||tolerance<=0||tolerance>.02)throw new ArgumentOutOfRangeException("tolerance");
            if(pieces.Count>2000)throw new InvalidOperationException("本次网络超过 2000 个可识别构件，请缩小范围");
            Pieces=pieces;Tolerance=tolerance;BuildJoins();
        }
        public static CablePiece Straight(IList<Triangle> mesh,double width,double height)
        {
            var m=StraightMeasurement.Measure(mesh,true);
            if(!m.IsStraightCandidate)throw new InvalidOperationException("直段截面检查未通过");
            if(Math.Abs(m.WidthMetres-width)>.05||Math.Abs(m.HeightMetres-height)>.005)throw new InvalidOperationException("截面与规格不符");
            return new CablePiece{WidthAxis=m.CrossAxis1,HeightAxis=m.CrossAxis2,Shape=new Part{Kind="straight",Centerline=new[]{m.PortStart,m.PortEnd},Ports=new[]{
                new Port{Point=m.PortStart,Outward=m.Axis*(-1),Width=m.WidthMetres,Height=m.HeightMetres},
                new Port{Point=m.PortEnd,Outward=m.Axis,Width=m.WidthMetres,Height=m.HeightMetres}}}};
        }
        public CableLocation At(int piece,double station)
        {
            var shape=Pieces[piece].Shape;
            if(!Vec.IsFinite(station)||station<-.000001||station>shape.Length+.000001)throw new ArgumentOutOfRangeException("station");
            station=Math.Max(0,Math.Min(shape.Length,station));double accumulated=0;var points=shape.Centerline;
            for(int i=1;i<points.Length;i++)
            {
                double l=(points[i]-points[i-1]).Norm;
                if(station<=accumulated+l+1e-9)return new CableLocation{Piece=piece,Station=station,Point=points[i-1]+(points[i]-points[i-1])*Math.Min(1,(station-accumulated)/l)};
                accumulated+=l;
            }
            return new CableLocation{Piece=piece,Station=shape.Length,Point=points.Last()};
        }
        public CableLocation Project(int piece,Vec point)
        {
            var line=Pieces[piece].Shape.Centerline;double best=double.PositiveInfinity,station=0,total=0;
            for(int i=1;i<line.Length;i++)
            {
                var delta=line[i]-line[i-1];double length=delta.Norm;
                double t=Math.Max(0,Math.Min(1,(point-line[i-1]).Dot(delta)/(length*length)));
                double distance=(point-line[i-1]-delta*t).Norm;
                if(distance<best){best=distance;station=total+length*t;}total+=length;
            }
            return At(piece,station);
        }
        bool Compatible(int a,int b){return a!=b&&!string.IsNullOrEmpty(Pieces[a].Domain)&&Pieces[a].Domain==Pieces[b].Domain;}
        string Socket(CableLocation l){return l.Piece+":"+(l.Station<.000001?"0":"1");}
        void BuildJoins()
        {
            var ends=new List<CableJoin>();
            for(int a=0;a<Pieces.Count;a++)for(int b=a+1;b<Pieces.Count;b++)
            {
                if(!Compatible(a,b))continue;
                for(int i=0;i<2;i++)for(int j=0;j<2;j++)
                {
                    var p=Pieces[a].Shape.Ports[i];var q=Pieces[b].Shape.Ports[j];
                    if((p.Point-q.Point).Norm>Tolerance||p.Outward.Dot(q.Outward)>-CosAngle)continue;
                    bool sizeChange=Math.Abs(p.Width-q.Width)>.003||Math.Abs(p.Height-q.Height)>.003;
                    ends.Add(new CableJoin{A=At(a,i==0?0:Pieces[a].Shape.Length),B=At(b,j==0?0:Pieces[b].Shape.Length),Kind=sizeChange?"size-change":"end-to-end",RequiresReview=sizeChange,SurfaceGap=(p.Point-q.Point).Norm});
                }
            }
            var endCounts=new Dictionary<string,int>();
            foreach(var j in ends)foreach(var s in new[]{Socket(j.A),Socket(j.B)}){if(!endCounts.ContainsKey(s))endCounts[s]=0;endCounts[s]++;}
            foreach(var j in ends)
            {
                if(endCounts[Socket(j.A)]>1||endCounts[Socket(j.B)]>1){Ambiguities.Add("端口重叠: "+Pieces[j.A.Piece].Shape.Name);continue;}
                Joins.Add(j);
            }
            var sides=new List<CableJoin>();
            for(int a=0;a<Pieces.Count;a++)for(int b=0;b<Pieces.Count;b++)
            {
                if(!Compatible(a,b)||Pieces[b].Shape.Kind!="straight")continue;
                var main=Pieces[b];var axis=main.Shape.Ports[1].Outward;
                for(int end=0;end<2;end++)
                {
                    var source=At(a,end==0?0:Pieces[a].Shape.Length);
                    if(endCounts.ContainsKey(Socket(source)))continue;
                    var port=Pieces[a].Shape.Ports[end];var target=Project(b,port.Point);
                    // Only a terminating branch facing the side of a straight main can create a junction.
                    if(Math.Abs(port.Outward.Dot(main.WidthAxis))<CosAngle||Math.Abs(port.Outward.Dot(axis))>Math.Sin(Math.PI/60))continue;
                    if(port.Width>main.Shape.Ports[0].Width+.003)continue;
                    if(target.Station<port.Width/2+Tolerance||target.Station>main.Shape.Length-port.Width/2-Tolerance)continue;
                    Vec delta=target.Point-port.Point;double advance=delta.Dot(port.Outward);
                    if(advance<=0)continue;
                    double vertical=Math.Abs(delta.Dot(main.HeightAxis));
                    if(vertical+port.Height/2>main.Shape.Ports[0].Height/2+Tolerance)continue;
                    double sideDistance=Math.Abs(delta.Dot(main.WidthAxis));
                    double gap=sideDistance-main.Shape.Ports[0].Width/2;
                    if(Math.Abs(gap)>Tolerance)continue;
                    // Geometry establishes a possible entry, not proof of a cut opening in the side rail.
                    sides.Add(new CableJoin{A=source,B=target,Kind="branch-to-middle",RequiresReview=true,SurfaceGap=gap});
                }
            }
            foreach(var group in sides.GroupBy(j=>Socket(j.A)))
            {
                if(group.Count()!=1){Ambiguities.Add("分支接入多个主路候选: "+Pieces[group.First().A.Piece].Shape.Name);continue;}
                Joins.Add(group.Single());
            }
        }
        sealed class Edge{public int A,B;public CableStep Step;}
        public CableRoute Find(CableLocation start,CableLocation finish,bool allowCandidates=true)
        {
            // Revalidate caller locations against this graph, rather than trusting arbitrary XYZ values.
            start=At(start.Piece,start.Station);finish=At(finish.Piece,finish.Station);
            var nodes=new List<CableLocation>();var edges=new List<Edge>();
            Func<CableLocation,int> node=l=>{int n=nodes.FindIndex(x=>x.Piece==l.Piece&&Math.Abs(x.Station-l.Station)<1e-8);if(n>=0)return n;nodes.Add(l);return nodes.Count-1;};
            int first=node(start),last=node(finish);
            foreach(var j in Joins.Where(j=>allowCandidates||!j.RequiresReview))
                edges.Add(new Edge{A=node(j.A),B=node(j.B),Step=new CableStep{Piece=-1,A=j.A.Point,B=j.B.Point,Length=j.Length,Join=j}});
            foreach(var group in nodes.Select((p,i)=>new{p,i}).GroupBy(x=>x.p.Piece))
            {
                var ordered=group.OrderBy(x=>x.p.Station).ToArray();
                for(int i=1;i<ordered.Length;i++)
                {
                    var a=ordered[i-1];var b=ordered[i];
                    edges.Add(new Edge{A=a.i,B=b.i,Step=new CableStep{Piece=group.Key,From=a.p.Station,To=b.p.Station,Length=b.p.Station-a.p.Station,A=a.p.Point,B=b.p.Point}});
                }
            }
            var distances=Enumerable.Repeat(double.PositiveInfinity,nodes.Count).ToArray();var prior=new Edge[nodes.Count];var visited=new bool[nodes.Count];distances[first]=0;
            for(int iteration=0;iteration<nodes.Count;iteration++)
            {
                int a=-1;for(int i=0;i<nodes.Count;i++)if(!visited[i]&&(a<0||distances[i]<distances[a]))a=i;
                if(a<0||double.IsInfinity(distances[a])||a==last)break;visited[a]=true;
                foreach(var edge in edges.Where(e=>e.A==a||e.B==a))
                {
                    int b=edge.A==a?edge.B:edge.A;double distance=distances[a]+edge.Step.Length;
                    if(distance<distances[b]){distances[b]=distance;prior[b]=edge;}
                }
            }
            if(double.IsInfinity(distances[last]))throw new InvalidOperationException("未找到连续路径；可能存在未识别配件或接入间隙");
            var steps=new List<CableStep>();int current=last;
            while(current!=first)
            {
                var edge=prior[current];bool forward=edge.B==current;var s=edge.Step;
                steps.Add(new CableStep{Piece=s.Piece,From=forward?s.From:s.To,To=forward?s.To:s.From,A=forward?s.A:s.B,B=forward?s.B:s.A,Length=s.Length,Join=s.Join});current=forward?edge.A:edge.B;
            }
            steps.Reverse();var ids=steps.Where(s=>s.Piece>=0).Select(s=>s.Piece).Concat(new[]{start.Piece,finish.Piece}).Distinct().ToArray();
            return new CableRoute{Length=distances[last],Steps=steps,Pieces=ids,RequiresReview=steps.Any(s=>s.Join!=null&&s.Join.RequiresReview)};
        }
    }
}
