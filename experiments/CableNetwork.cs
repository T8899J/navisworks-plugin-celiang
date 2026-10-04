using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public sealed class CablePiece { public Part Shape; public string Domain; public Vec WidthAxis,HeightAxis; }
    public sealed class CableLocation { public int Piece,Edge; public int Port=-1; public double Station; public Vec Point; }
    public sealed class CableJoin
    {
        public CableLocation A,B;
        public string Kind,ReviewReason;
        public bool RequiresReview;
        public double SurfaceGap;
        public double OverlapLength;
        public double GapDistance,LateralOffset,VerticalOffset;
        // A sleeve join is stationed inside both terminal edges. Retain the physical
        // socket identities separately for ambiguity detection and side-entry exclusion.
        public int SocketPortA=-1,SocketPortB=-1;
        public Vec ConnectionPoint;
        public bool IsSideEntry { get { return Kind=="branch-to-middle"; } }
        public bool IsGapBridge { get { return Kind=="gap-bridge"; } }
        public double ConnectionLength { get { return IsGapBridge?GapDistance:(A.Point-(IsSideEntry?ConnectionPoint:B.Point)).Norm; } }
        public double AccessLength { get { return IsSideEntry?(ConnectionPoint-B.Point).Norm:0; } }
        // Compatibility summary only. A graph ConnectionEdge uses the small ConnectionLength.
        public double Length { get { return ConnectionLength+AccessLength; } }
    }
    public sealed class CableStep
    {
        public int Piece,Edge;
        public string EdgeId,ReviewReason;
        public CableEdgeKind Kind;
        public double From,To,Length;
        public Vec A,B;
        public Vec[] Centerline;
        public CableJoin Join;
        public bool RequiresReview;
    }
    public sealed class CableRoute
    {
        public double Length;
        public int GapBridgeCount;
        public double GapBridgeLength;
        public List<CableStep> Steps,InternalEdges;
        public bool RequiresReview;
        public int[] Pieces;
        public List<CableFittingContribution> FittingContributions;
        public List<CableJoin> ReviewConnections;
    }
    public sealed class CableNetwork
    {
        public readonly List<CablePiece> Pieces;
        public readonly List<CableJoin> Joins=new List<CableJoin>();
        public readonly List<string> Ambiguities=new List<string>();
        public readonly List<CableGraphNode> GraphNodes;
        public readonly List<CableGraphEdge> GraphEdges;
        public readonly double Tolerance;
        public double PhysicalTolerance { get { return Tolerance; } }
        public readonly double GapBridgeMaxDistance,GapBridgeWidthAxisTolerance,GapBridgeHeightAxisTolerance,GapBridgeSizeTolerance;
        static readonly double CosAngle=Math.Cos(Math.PI/60);
        const double PositionEpsilon=1e-7;
        public CableNetwork(List<CablePiece> pieces,double tolerance=.002,CableNetworkOptions options=null)
        {
            if(!Vec.IsFinite(tolerance)||tolerance<=0||tolerance>.02)throw new ArgumentOutOfRangeException("tolerance");
            options=options??new CableNetworkOptions();
            foreach(var setting in new[]{options.GapBridgeMaxDistance,options.GapBridgeWidthAxisTolerance,options.GapBridgeHeightAxisTolerance,options.GapBridgeSizeTolerance})
                if(!Vec.IsFinite(setting)||setting<0)throw new ArgumentOutOfRangeException("options","Gap bridge distances and tolerances must be finite, non-negative world metres.");
            if(pieces==null)throw new ArgumentNullException("pieces");
            if(pieces.Count>2000)throw new InvalidOperationException("本次网络超过 2000 个可识别构件，请缩小范围");
            Pieces=pieces;Tolerance=tolerance;
            GapBridgeMaxDistance=options.GapBridgeMaxDistance;GapBridgeWidthAxisTolerance=options.GapBridgeWidthAxisTolerance;
            GapBridgeHeightAxisTolerance=options.GapBridgeHeightAxisTolerance;GapBridgeSizeTolerance=options.GapBridgeSizeTolerance;
            for(int i=0;i<Pieces.Count;i++)ValidatePart(i);
            BuildJoins();var graph=BuildGraph(null,null);GraphNodes=graph.Nodes;GraphEdges=graph.Edges;
        }
        void ValidatePart(int piece)
        {
            var p=Pieces[piece]==null?null:Pieces[piece].Shape;
            Action<string> fail=s=>{throw new InvalidOperationException("构件 "+piece+" ["+(p==null?"":p.Id)+"]: "+s);};
            if(p==null||p.Ports==null||p.Ports.Length<2){fail("至少需要两个几何 Port");return;}
            var points=new Dictionary<string,Vec>(StringComparer.Ordinal);
            for(int i=0;i<p.Ports.Length;i++)
            {
                var port=p.Ports[i];if(port==null){fail("Port 为空");return;}
                if(string.IsNullOrEmpty(port.Id))port.Id="P"+i;
                if(port.Id.StartsWith("$",StringComparison.Ordinal))fail("Port Id 不能使用自动节点的保留前缀 $");
                if(points.ContainsKey(port.Id))fail("Port/Junction Id 重复: "+port.Id);
                if(!port.Point.Finite||!port.Outward.Finite||port.Outward.Norm<1e-10)fail("Port 坐标或朝向无效: "+port.Id);
                if(!Vec.IsFinite(port.Width)||!Vec.IsFinite(port.Height)||port.Width<=0||port.Height<=0)fail("Port 几何截面无效: "+port.Id);
                port.Outward=port.Outward*(1/port.Outward.Norm);points.Add(port.Id,port.Point);
            }
            p.Junctions=p.Junctions??new PartJunction[0];
            foreach(var j in p.Junctions)
            {
                if(j==null||string.IsNullOrEmpty(j.Id)||!j.Point.Finite)fail("内部 Junction 定义无效");
                if(j.Id.StartsWith("$",StringComparison.Ordinal))fail("Junction Id 不能使用自动节点的保留前缀 $");
                if(points.ContainsKey(j.Id))fail("Port/Junction Id 重复: "+j.Id);points.Add(j.Id,j.Point);
            }
            if(p.InternalEdges==null||p.InternalEdges.Length==0)
            {
                if(p.Ports.Length!=2||p.Junctions.Length!=0)fail("多端口或含 Junction 的构件必须显式提供 InternalEdges");
                if(p.Centerline==null||p.Centerline.Length<2)fail("缺少内部中心线");
                p.InternalEdges=new[]{new InternalEdge{Id="centerline",From=p.Ports[0].Id,To=p.Ports[1].Id,Centerline=p.Centerline}};
            }
            var edgeIds=new HashSet<string>(StringComparer.Ordinal);var degree=points.Keys.ToDictionary(k=>k,k=>0,StringComparer.Ordinal);
            for(int i=0;i<p.InternalEdges.Length;i++)
            {
                var edge=p.InternalEdges[i];if(edge==null){fail("InternalEdge 为空");return;}
                if(string.IsNullOrEmpty(edge.Id))edge.Id="E"+i;if(!edgeIds.Add(edge.Id))fail("InternalEdge Id 重复: "+edge.Id);
                if(string.IsNullOrEmpty(edge.From)||string.IsNullOrEmpty(edge.To)||edge.From==edge.To||!points.ContainsKey(edge.From)||!points.ContainsKey(edge.To))fail("InternalEdge 引用无效: "+edge.Id);
                if(edge.Centerline==null||edge.Centerline.Length<2||edge.Centerline.Any(v=>!v.Finite))fail("InternalEdge 中心线无效: "+edge.Id);
                var clean=new List<Vec>{edge.Centerline[0]};foreach(var v in edge.Centerline.Skip(1))if((v-clean.Last()).Norm>1e-10)clean.Add(v);edge.Centerline=clean.ToArray();
                if(edge.Centerline.Length<2||!Vec.IsFinite(edge.Length)||edge.Length<=1e-9)fail("InternalEdge 长度无效: "+edge.Id);
                if((edge.Centerline[0]-points[edge.From]).Norm>PositionEpsilon||(edge.Centerline.Last()-points[edge.To]).Norm>PositionEpsilon)fail("中心线端点未落在引用的 Port/Junction: "+edge.Id);
                string kind=(p.Kind??"").ToLowerInvariant();if((kind.Contains("elbow")||kind.Contains("riser"))&&(edge.Centerline.Length<3||edge.Length-(edge.Centerline.Last()-edge.Centerline[0]).Norm<=1e-8))fail("弯头/竖弯必须有弯曲中心线，不能使用端口直线距离");
                degree[edge.From]++;degree[edge.To]++;
            }
            if(p.Ports.Any(port=>degree[port.Id]!=1))fail("每个物理 Port 必须连接一条内部边；分流需显式 Junction");
            if(p.Junctions.Any(j=>degree[j.Id]<2))fail("内部 Junction 至少需要两条内部边");
            var reached=new HashSet<string>{p.Ports[0].Id};bool changed;
            do{changed=false;foreach(var e in p.InternalEdges)if(reached.Contains(e.From)||reached.Contains(e.To)){changed|=reached.Add(e.From);changed|=reached.Add(e.To);}}while(changed);
            if(reached.Count!=points.Count)fail("构件内部边不连通");
        }
        public static CablePiece Straight(IList<Triangle> mesh,double width,double height)
        {
            var m=StraightMeasurement.Measure(mesh,true);if(!m.IsStraightCandidate)throw new InvalidOperationException("直段截面检查未通过");
            // Nominal labels are diagnostic only; measured geometry defines the port dimensions.
            return new CablePiece{WidthAxis=m.CrossAxis1,HeightAxis=m.CrossAxis2,Shape=new Part{Kind="straight",Centerline=new[]{m.PortStart,m.PortEnd},Ports=new[]{
                new Port{Id="P0",Point=m.PortStart,Outward=m.Axis*(-1),Width=m.WidthMetres,Height=m.HeightMetres,WidthAxis=m.CrossAxis1,HeightAxis=m.CrossAxis2},
                new Port{Id="P1",Point=m.PortEnd,Outward=m.Axis,Width=m.WidthMetres,Height=m.HeightMetres,WidthAxis=m.CrossAxis1,HeightAxis=m.CrossAxis2}}}};
        }
        void CheckPiece(int piece){if(piece<0||piece>=Pieces.Count)throw new ArgumentOutOfRangeException("piece");}
        public CableLocation At(int piece,double station)
        {
            CheckPiece(piece);if(Pieces[piece].Shape.InternalEdges.Length!=1)throw new InvalidOperationException("多内部边构件必须指定 edge 或使用 AtPort/Project");return At(piece,0,station);
        }
        public CableLocation At(int piece,int edge,double station)
        {
            CheckPiece(piece);var shape=Pieces[piece].Shape;if(edge<0||edge>=shape.InternalEdges.Length)throw new ArgumentOutOfRangeException("edge");
            var path=shape.InternalEdges[edge];if(!Vec.IsFinite(station)||station<-PositionEpsilon||station>path.Length+PositionEpsilon)throw new ArgumentOutOfRangeException("station");
            station=Math.Max(0,Math.Min(path.Length,station));int port=-1;
            if(station<=1e-8)port=Array.FindIndex(shape.Ports,p=>p.Id==path.From);else if(path.Length-station<=1e-8)port=Array.FindIndex(shape.Ports,p=>p.Id==path.To);
            return new CableLocation{Piece=piece,Edge=edge,Port=port,Station=station,Point=PointAt(path.Centerline,station)};
        }
        public CableLocation AtPort(int piece,int port)
        {
            CheckPiece(piece);var shape=Pieces[piece].Shape;if(port<0||port>=shape.Ports.Length)throw new ArgumentOutOfRangeException("port");string id=shape.Ports[port].Id;
            for(int edge=0;edge<shape.InternalEdges.Length;edge++){var e=shape.InternalEdges[edge];if(e.From==id)return At(piece,edge,0);if(e.To==id)return At(piece,edge,e.Length);}
            throw new InvalidOperationException("Port 没有内部路径: "+id);
        }
        public CableLocation Project(int piece,Vec point)
        {
            CheckPiece(piece);if(!point.Finite)throw new ArgumentOutOfRangeException("point");double best=double.PositiveInfinity;CableLocation result=null;
            for(int edge=0;edge<Pieces[piece].Shape.InternalEdges.Length;edge++)
            {
                var line=Pieces[piece].Shape.InternalEdges[edge].Centerline;double total=0;
                for(int i=1;i<line.Length;i++){var delta=line[i]-line[i-1];double length=delta.Norm,t=Math.Max(0,Math.Min(1,(point-line[i-1]).Dot(delta)/(length*length)));double d=(point-line[i-1]-delta*t).Norm;if(d<best){best=d;result=At(piece,edge,total+length*t);}total+=length;}
            }
            return result;
        }
        static Vec PointAt(Vec[] line,double station)
        {
            double total=0;for(int i=1;i<line.Length;i++){double length=(line[i]-line[i-1]).Norm;if(station<=total+length+1e-10)return line[i-1]+(line[i]-line[i-1])*Math.Max(0,Math.Min(1,(station-total)/length));total+=length;}return line.Last();
        }
        static Vec[] Slice(Vec[] line,double start,double end)
        {
            var points=new List<Vec>{PointAt(line,start)};double station=0;for(int i=1;i<line.Length;i++){station+=(line[i]-line[i-1]).Norm;if(station>start+1e-9&&station<end-1e-9)points.Add(line[i]);}points.Add(PointAt(line,end));return points.ToArray();
        }
        static string Socket(CableLocation l){return l.Piece+":"+l.Port;}
        static string Socket(CableJoin join,bool first){var location=first?join.A:join.B;int port=first?join.SocketPortA:join.SocketPortB;return location.Piece+":"+(port<0?location.Port:port);}
        string Label(int p){return Pieces[p].Shape.Id+" / "+Pieces[p].Shape.Name;}
        static bool HasUsableFrame(Port port)
        {
            if(!port.WidthAxis.Finite||!port.HeightAxis.Finite||port.WidthAxis.Norm<1e-9||port.HeightAxis.Norm<1e-9)return false;
            var width=port.WidthAxis*(1/port.WidthAxis.Norm);var height=port.HeightAxis*(1/port.HeightAxis.Norm);double angleTolerance=Math.Sin(Math.PI/60);
            return Math.Abs(width.Dot(port.Outward))<angleTolerance&&Math.Abs(height.Dot(port.Outward))<angleTolerance&&Math.Abs(width.Dot(height))<angleTolerance;
        }
        static bool CrossSectionRollDiffers(Port a,Port b)
        {
            if(!HasUsableFrame(a)||!HasUsableFrame(b))return false;
            // A reconstructed axis may point either way. Compare the cross-section axes as
            // undirected lines, so PCA/port orientation conventions do not create false reviews.
            return Math.Abs(a.WidthAxis.Dot(b.WidthAxis)/(a.WidthAxis.Norm*b.WidthAxis.Norm))<CosAngle||
                Math.Abs(a.HeightAxis.Dot(b.HeightAxis)/(a.HeightAxis.Norm*b.HeightAxis.Norm))<CosAngle;
        }
        void BuildJoins()
        {
            var ends=new List<CableJoin>();
            for(int a=0;a<Pieces.Count;a++)for(int b=a+1;b<Pieces.Count;b++)for(int i=0;i<Pieces[a].Shape.Ports.Length;i++)for(int j=0;j<Pieces[b].Shape.Ports.Length;j++)
            {
                var p=Pieces[a].Shape.Ports[i];var q=Pieces[b].Shape.Ports[j];double gap=(p.Point-q.Point).Norm;if(p.Outward.Dot(q.Outward)>-CosAngle)continue;
                if(gap>Tolerance)
                {
                    CableJoin overlap;
                    if(TrySleeveJoin(a,i,b,j,out overlap))ends.Add(overlap);
                    continue;
                }
                bool sizeChange=Math.Abs(p.Width-q.Width)>.003||Math.Abs(p.Height-q.Height)>.003;bool rollChange=CrossSectionRollDiffers(p,q);
                var reviewReasons=new List<string>();if(sizeChange)reviewReasons.Add("相邻端口几何截面不同，需确认过渡/接头可通行");if(rollChange)reviewReasons.Add("端口截面朝向不同，需确认滚转转接可通行");
                ends.Add(new CableJoin{A=AtPort(a,i),B=AtPort(b,j),Kind=sizeChange?"size-change":"end-to-end",RequiresReview=sizeChange||rollChange,ReviewReason=reviewReasons.Count==0?null:string.Join("；",reviewReasons),SurfaceGap=gap,ConnectionPoint=q.Point});
            }
            var endCounts=new Dictionary<string,int>();foreach(var j in ends)foreach(var s in new[]{Socket(j,true),Socket(j,false)}){if(!endCounts.ContainsKey(s))endCounts[s]=0;endCounts[s]++;}
            foreach(var j in ends){if(endCounts[Socket(j,true)]>1||endCounts[Socket(j,false)]>1){Ambiguities.Add("端口存在重叠候选: "+Label(j.A.Piece)+" -> "+Label(j.B.Piece));continue;}Joins.Add(j);}
            var sides=new List<CableJoin>();
            for(int b=0;b<Pieces.Count;b++)
            {
                var main=Pieces[b];Vec axis,widthAxis,heightAxis;double mainWidth,mainHeight;if(!StraightFrame(main,out axis,out widthAxis,out heightAxis,out mainWidth,out mainHeight))continue;
                for(int a=0;a<Pieces.Count;a++)if(a!=b)for(int end=0;end<Pieces[a].Shape.Ports.Length;end++)
                {
                    var source=AtPort(a,end);if(endCounts.ContainsKey(Socket(source)))continue;var port=Pieces[a].Shape.Ports[end];
                    if(Math.Abs(port.Outward.Dot(widthAxis))<CosAngle||Math.Abs(port.Outward.Dot(axis))>Math.Sin(Math.PI/60))continue;
                    var target=Project(b,port.Point);var delta=target.Point-port.Point;if(delta.Dot(port.Outward)<=0)continue;
                    // Measured envelope containment prevents stacked trays from becoming side connections.
                    if(Math.Abs(delta.Dot(heightAxis))+port.Height/2>mainHeight/2+Tolerance)continue;
                    double gap=Math.Abs(delta.Dot(widthAxis))-mainWidth/2;if(Math.Abs(gap)>Tolerance)continue;
                    double advance=gap/Math.Abs(port.Outward.Dot(widthAxis));if(Math.Abs(advance)>Tolerance)continue;var sidePoint=port.Point+port.Outward*advance;target=Project(b,sidePoint);
                    if(target.Station<port.Width/2+Tolerance||target.Station>main.Shape.InternalEdges[0].Length-port.Width/2-Tolerance)continue;
                    sides.Add(new CableJoin{A=source,B=target,Kind="branch-to-middle",RequiresReview=true,ReviewReason="分支端口接近主路侧面；侧壁开口、内部接入中心线与实际可穿缆性需复核",SurfaceGap=gap,ConnectionPoint=sidePoint});
                }
            }
            var occupied=new HashSet<string>(endCounts.Keys,StringComparer.Ordinal);
            foreach(var group in sides.GroupBy(j=>Socket(j.A))){occupied.Add(group.Key);if(group.Count()!=1){Ambiguities.Add("分支接入多个主路候选: "+Label(group.First().A.Piece));continue;}Joins.Add(group.Single());}
            BuildGapBridges(occupied);
        }
        void BuildGapBridges(HashSet<string> occupied)
        {
            if(GapBridgeMaxDistance<=PhysicalTolerance)return;
            // Only free sockets on real straight centreline geometry participate. Keep physical
            // ambiguity reserved too: a gap must never bypass a conflicting physical connection.
            var free=new List<CableLocation>();
            for(int piece=0;piece<Pieces.Count;piece++)
            {
                Vec axis,widthAxis,heightAxis;double width,height;
                if(!StraightFrame(Pieces[piece],out axis,out widthAxis,out heightAxis,out width,out height))continue;
                for(int port=0;port<Pieces[piece].Shape.Ports.Length;port++)
                {
                    var location=AtPort(piece,port);var socket=Pieces[piece].Shape.Ports[port];
                    var inward=location.Station==0?axis:axis*(-1);
                    if(!occupied.Contains(Socket(location))&&HasUsableFrame(socket)&&socket.Outward.Dot(inward)<-CosAngle)free.Add(location);
                }
            }
            var candidates=new List<CableJoin>();var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            for(int i=0;i<free.Count;i++)for(int j=i+1;j<free.Count;j++)
            {
                if(free[i].Piece==free[j].Piece)continue;CableJoin candidate;
                if(!TryGapBridge(free[i],free[j],out candidate))continue;
                candidates.Add(candidate);
                foreach(var socket in new[]{Socket(candidate.A),Socket(candidate.B)}){int count;counts.TryGetValue(socket,out count);counts[socket]=count+1;}
            }
            // Evaluate the whole candidate set before accepting anything. Uniqueness must hold
            // at BOTH ends; nearest-first or greedy pairing can silently choose a wrong tray.
            foreach(var candidate in candidates)
            {
                if(counts[Socket(candidate.A)]!=1||counts[Socket(candidate.B)]!=1)
                {
                    Ambiguities.Add("断截端口存在多个合法候选: "+Label(candidate.A.Piece)+" / "+Pieces[candidate.A.Piece].Shape.Ports[candidate.A.Port].Id+
                        " ("+counts[Socket(candidate.A)]+") -> "+Label(candidate.B.Piece)+" / "+Pieces[candidate.B.Piece].Shape.Ports[candidate.B.Port].Id+" ("+counts[Socket(candidate.B)]+")");
                    continue;
                }
                Joins.Add(candidate);
            }
        }
        bool TryGapBridge(CableLocation a,CableLocation b,out CableJoin join)
        {
            join=null;var p=Pieces[a.Piece].Shape.Ports[a.Port];var q=Pieces[b.Piece].Shape.Ports[b.Port];
            var delta=q.Point-p.Point;double gap=delta.Norm;
            if(gap<=PhysicalTolerance||gap>GapBridgeMaxDistance+1e-10||p.Outward.Dot(q.Outward)>-CosAngle)return false;
            if(delta.Dot(p.Outward)/gap<CosAngle||(delta*(-1)).Dot(q.Outward)/gap<CosAngle)return false;
            if(Math.Abs(p.Width-q.Width)>GapBridgeSizeTolerance||Math.Abs(p.Height-q.Height)>GapBridgeSizeTolerance||CrossSectionRollDiffers(p,q))return false;
            double lateral=Math.Max(Math.Abs(delta.Dot(p.WidthAxis)/p.WidthAxis.Norm),Math.Abs(delta.Dot(q.WidthAxis)/q.WidthAxis.Norm));
            double vertical=Math.Max(Math.Abs(delta.Dot(p.HeightAxis)/p.HeightAxis.Norm),Math.Abs(delta.Dot(q.HeightAxis)/q.HeightAxis.Norm));
            if(lateral>GapBridgeWidthAxisTolerance||vertical>GapBridgeHeightAxisTolerance)return false;
            join=new CableJoin{A=a,B=b,Kind="gap-bridge",GapDistance=gap,LateralOffset=lateral,VerticalOffset=vertical,SurfaceGap=gap,
                ConnectionPoint=q.Point,RequiresReview=true,ReviewReason="直线断截桥接待复核: gap="+(gap*1000).ToString("0.000",CultureInfo.InvariantCulture)+
                    " mm; 横向偏差="+(lateral*1000).ToString("0.000",CultureInfo.InvariantCulture)+" mm; 竖向偏差="+(vertical*1000).ToString("0.000",CultureInfo.InvariantCulture)+" mm"};
            return true;
        }
        bool TrySleeveJoin(int a,int portA,int b,int portB,out CableJoin join)
        {
            join=null;var p=Pieces[a].Shape.Ports[portA];var q=Pieces[b].Shape.Ports[portB];
            var delta=q.Point-p.Point;double overlap=-delta.Dot(p.Outward);
            // Actual inward overlap is different from an empty model gap. Do not widen
            // the 2 mm gap tolerance or snap separated/parallel trays together.
            if(overlap<=Tolerance || overlap>.20 || (delta+p.Outward*overlap).Norm>Tolerance)return false;
            if(Math.Abs(p.Width-q.Width)>.003 || Math.Abs(p.Height-q.Height)>.003 || CrossSectionRollDiffers(p,q))return false;
            var endA=AtPort(a,portA);var endB=AtPort(b,portB);
            var lineA=Pieces[a].Shape.InternalEdges[endA.Edge].Centerline;
            var lineB=Pieces[b].Shape.InternalEdges[endB.Edge].Centerline;
            var nextA=endA.Station==0?lineA[1]:lineA[lineA.Length-2];
            var nextB=endB.Station==0?lineB[1]:lineB[lineB.Length-2];
            var inwardA=nextA-p.Point;var inwardB=nextB-q.Point;
            // Both physical ends must lie inside the opposite terminal straight leg.
            // This excludes side penetration, bends crossing in their middle and long duplicates.
            if(overlap>inwardA.Norm+PositionEpsilon || overlap>inwardB.Norm+PositionEpsilon ||
                inwardA.Dot(p.Outward)/inwardA.Norm>-CosAngle || inwardB.Dot(q.Outward)/inwardB.Norm>-CosAngle)return false;
            var midpoint=(p.Point+q.Point)*.5;
            Func<CableLocation,Vec,CableLocation> station=(end,inward)=>{
                double advance=(midpoint-end.Point).Dot(inward)/inward.Norm;
                return At(end.Piece,end.Edge,end.Station+(end.Station==0?advance:-advance));
            };
            var atA=station(endA,inwardA);var atB=station(endB,inwardB);
            if((atA.Point-atB.Point).Norm>Tolerance)return false;
            join=new CableJoin{A=atA,B=atB,SocketPortA=portA,SocketPortB=portB,Kind="sleeve-overlap",OverlapLength=overlap,
                SurfaceGap=(atA.Point-atB.Point).Norm,ConnectionPoint=atB.Point,RequiresReview=true,
                ReviewReason="端部中心线存在 "+(overlap*1000).ToString("0.0",CultureInfo.InvariantCulture)+" mm 套接重叠，按重叠中点拆边，仅计一次；需确认套接可穿缆"};
            return true;
        }
        static bool StraightFrame(CablePiece piece,out Vec axis,out Vec widthAxis,out Vec heightAxis,out double width,out double height)
        {
            axis=widthAxis=heightAxis=new Vec();width=height=0;var part=piece.Shape;if(part.Ports.Length!=2||part.InternalEdges.Length!=1)return false;
            string kind=(part.Kind??"").ToLowerInvariant();if(kind!="straight"&&kind!="slope"&&kind!="slopestraight")return false;
            var line=part.InternalEdges[0].Centerline;axis=line.Last()-line[0];double length=axis.Norm;if(length<1e-9)return false;axis=axis*(1/length);var direction=axis;
            if(line.Any(p=>(p-line[0]-direction*((p-line[0]).Dot(direction))).Norm>1e-6))return false;
            widthAxis=part.Ports[0].WidthAxis.Norm>1e-9?part.Ports[0].WidthAxis:piece.WidthAxis;heightAxis=part.Ports[0].HeightAxis.Norm>1e-9?part.Ports[0].HeightAxis:piece.HeightAxis;
            if(!widthAxis.Finite||!heightAxis.Finite||widthAxis.Norm<1e-9||heightAxis.Norm<1e-9)return false;widthAxis=widthAxis*(1/widthAxis.Norm);heightAxis=heightAxis*(1/heightAxis.Norm);
            if(Math.Abs(axis.Dot(widthAxis))>1e-5||Math.Abs(axis.Dot(heightAxis))>1e-5||Math.Abs(widthAxis.Dot(heightAxis))>1e-5)return false;
            width=part.Ports[0].Width;height=part.Ports[0].Height;return Math.Abs(width-part.Ports[1].Width)<1e-5&&Math.Abs(height-part.Ports[1].Height)<1e-5;
        }
        sealed class StationNode { public double Station;public int Node; }
        sealed class Graph { public List<CableGraphNode> Nodes=new List<CableGraphNode>();public List<CableGraphEdge> Edges=new List<CableGraphEdge>();public int Start=-1,Finish=-1; }
        Graph BuildGraph(CableLocation start,CableLocation finish)
        {
            var g=new Graph();var nodeIds=new Dictionary<string,int>();var stations=new Dictionary<string,List<StationNode>>();Func<int,string,string> key=(p,id)=>p+":"+id;
            Func<int,string,CableNodeKind,Vec,int> addNode=(p,id,kind,point)=>{string k=key(p,id);int index;if(nodeIds.TryGetValue(k,out index))return index;index=g.Nodes.Count;nodeIds.Add(k,index);g.Nodes.Add(new CableGraphNode{Id=index,Piece=p,LocalId=id,Kind=kind,Point=point});return index;};
            for(int p=0;p<Pieces.Count;p++)
            {
                var part=Pieces[p].Shape;foreach(var port in part.Ports)addNode(p,port.Id,CableNodeKind.Port,port.Point);foreach(var j in part.Junctions)addNode(p,j.Id,CableNodeKind.FittingInternalJunction,j.Point);
                for(int e=0;e<part.InternalEdges.Length;e++){var edge=part.InternalEdges[e];stations.Add(key(p,e.ToString(CultureInfo.InvariantCulture)),new List<StationNode>{new StationNode{Station=0,Node=nodeIds[key(p,edge.From)]},new StationNode{Station=edge.Length,Node=nodeIds[key(p,edge.To)]}});}
            }
            Func<CableLocation,int> stationNode=l=>{var list=stations[key(l.Piece,l.Edge.ToString(CultureInfo.InvariantCulture))];var existing=list.FirstOrDefault(s=>Math.Abs(s.Station-l.Station)<=1e-8);if(existing!=null)return existing.Node;string id="$station:"+l.Edge+":"+l.Station.ToString("R",CultureInfo.InvariantCulture);int n=addNode(l.Piece,id,CableNodeKind.VirtualJunction,l.Point);list.Add(new StationNode{Station=l.Station,Node=n});return n;};
            Action<CableGraphEdge> addEdge=e=>{e.Id=g.Edges.Count;g.Edges.Add(e);};
            for(int i=0;i<Joins.Count;i++)
            {
                var j=Joins[i];int a=stationNode(j.A),b=stationNode(j.B),connectionB=b;
                if(j.IsSideEntry){connectionB=addNode(j.B.Piece,"$side-port:"+i,CableNodeKind.Port,j.ConnectionPoint);addEdge(new CableGraphEdge{From=connectionB,To=b,Piece=j.B.Piece,InternalEdge=-1,EdgeId="$side-access:"+i,Kind=CableEdgeKind.InternalEdge,Length=j.AccessLength,FromStation=0,ToStation=j.AccessLength,Centerline=new[]{j.ConnectionPoint,j.B.Point},RequiresReview=true,ReviewReason=j.ReviewReason});}
                addEdge(new CableGraphEdge{From=a,To=connectionB,Piece=-1,InternalEdge=-1,EdgeId=(j.IsGapBridge?"$gap-bridge:":"$connection:")+i,Kind=j.IsGapBridge?CableEdgeKind.GapBridgeEdge:CableEdgeKind.ConnectionEdge,Length=j.ConnectionLength,Centerline=new[]{j.IsGapBridge?g.Nodes[a].Point:j.A.Point,g.Nodes[connectionB].Point},RequiresReview=j.RequiresReview,ReviewReason=j.ReviewReason,Join=j});
            }
            if(start!=null)g.Start=stationNode(start);if(finish!=null)g.Finish=stationNode(finish);
            for(int p=0;p<Pieces.Count;p++)for(int e=0;e<Pieces[p].Shape.InternalEdges.Length;e++)
            {
                var edge=Pieces[p].Shape.InternalEdges[e];var ordered=stations[key(p,e.ToString(CultureInfo.InvariantCulture))].OrderBy(s=>s.Station).ToArray();
                for(int i=1;i<ordered.Length;i++){var a=ordered[i-1];var b=ordered[i];addEdge(new CableGraphEdge{From=a.Node,To=b.Node,Piece=p,InternalEdge=e,EdgeId=edge.Id,Kind=CableEdgeKind.InternalEdge,Length=b.Station-a.Station,FromStation=a.Station,ToStation=b.Station,Centerline=Slice(edge.Centerline,a.Station,b.Station),RequiresReview=edge.RequiresReview,ReviewReason=edge.ReviewReason});}
            }
            return g;
        }
        public CableRoute Find(CableLocation start,CableLocation finish,bool allowCandidates=true)
        {
            if(start==null||finish==null)throw new ArgumentNullException(start==null?"start":"finish");
            // Resolve XYZ from the actual internal edge, never trust caller-supplied coordinates.
            start=At(start.Piece,start.Edge,start.Station);finish=At(finish.Piece,finish.Edge,finish.Station);var graph=BuildGraph(start,finish);int count=graph.Nodes.Count;
            var distances=Enumerable.Repeat(double.PositiveInfinity,count).ToArray();var gaps=Enumerable.Repeat(int.MaxValue,count).ToArray();
            var prior=new CableGraphEdge[count];var visited=new bool[count];var adjacency=Enumerable.Range(0,count).Select(i=>new List<CableGraphEdge>()).ToArray();
            foreach(var edge in graph.Edges){if(!allowCandidates&&edge.RequiresReview)continue;adjacency[edge.From].Add(edge);adjacency[edge.To].Add(edge);}distances[graph.Start]=0;gaps[graph.Start]=0;
            // Lexicographic Dijkstra: minimise gap count first, then real physical length.
            // Never add a fictitious length penalty to make a review route look longer.
            Func<int,double,int,double,bool> better=(gap,length,otherGap,otherLength)=>gap<otherGap||(gap==otherGap&&length<otherLength);
            for(int iteration=0;iteration<count;iteration++)
            {
                int a=-1;for(int i=0;i<count;i++)if(!visited[i]&&(a<0||better(gaps[i],distances[i],gaps[a],distances[a])))a=i;if(a<0||double.IsInfinity(distances[a])||a==graph.Finish)break;visited[a]=true;
                foreach(var edge in adjacency[a]){int b=edge.From==a?edge.To:edge.From;if(visited[b])continue;double distance=distances[a]+edge.Length;int gap=gaps[a]+(edge.Kind==CableEdgeKind.GapBridgeEdge?1:0);if(better(gap,distance,gaps[b],distances[b])){distances[b]=distance;gaps[b]=gap;prior[b]=edge;}}
            }
            if(double.IsInfinity(distances[graph.Finish]))throw new InvalidOperationException("未找到连续的 Port/Junction 路径；可能存在未识别配件、接入间隙或待复核连接");
            var steps=new List<CableStep>();int current=graph.Finish;
            while(current!=graph.Start)
            {
                var e=prior[current];bool forward=e.To==current;steps.Add(new CableStep{Piece=e.Piece,Edge=e.InternalEdge,EdgeId=e.EdgeId,Kind=e.Kind,From=forward?e.FromStation:e.ToStation,To=forward?e.ToStation:e.FromStation,A=graph.Nodes[forward?e.From:e.To].Point,B=graph.Nodes[forward?e.To:e.From].Point,Centerline=forward?e.Centerline.ToArray():e.Centerline.Reverse().ToArray(),Length=e.Length,Join=e.Join,RequiresReview=e.RequiresReview,ReviewReason=e.ReviewReason});current=forward?e.From:e.To;
            }
            steps.Reverse();var ids=steps.Where(s=>s.Piece>=0).Select(s=>s.Piece).Concat(new[]{start.Piece,finish.Piece}).Distinct().ToArray();var internals=steps.Where(s=>s.Kind==CableEdgeKind.InternalEdge).ToList();
            return new CableRoute{Length=distances[graph.Finish],GapBridgeCount=gaps[graph.Finish],GapBridgeLength=steps.Where(s=>s.Kind==CableEdgeKind.GapBridgeEdge).Sum(s=>s.Length),Steps=steps,Pieces=ids,RequiresReview=steps.Any(s=>s.RequiresReview),InternalEdges=internals,ReviewConnections=steps.Where(s=>s.Join!=null&&s.RequiresReview).Select(s=>s.Join).Distinct().ToList(),FittingContributions=internals.Where(s=>IsFitting(Pieces[s.Piece].Shape)).GroupBy(s=>s.Piece).Select(group=>new CableFittingContribution{Piece=group.Key,Name=Pieces[group.Key].Shape.Name,Kind=Pieces[group.Key].Shape.Kind,Length=group.Sum(s=>s.Length),EdgeIds=group.Select(s=>s.EdgeId).Distinct().ToArray()}).ToList()};
        }
        static bool IsFitting(Part p){string kind=(p.Kind??"").ToLowerInvariant();return kind!="straight"&&kind!="slope"&&kind!="slopestraight";}
    }
}
