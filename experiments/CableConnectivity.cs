using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public sealed class CableBoundaryPort
    {
        public int Piece,Port,PhysicalComponent;
        public string PieceId,Name,RunName,PortId;
        public Vec Point,Outward;
        public double Width,Height;
        public bool ReservedPhysicalAmbiguity;
    }

    public sealed class CableConnectivityDiagnostics
    {
        public CableLocation Start,Finish;
        public int StartPhysicalComponent,FinishPhysicalComponent,PhysicalComponentCount;
        public List<CableBoundaryPort> BoundaryPorts;
        public List<VirtualConnectorCandidate> Candidates;
        public int TopNPerSource=5;
        public bool DiagnosticOnly=true;
        public string Note;
    }

    public sealed class CablePathNotFoundException:InvalidOperationException
    {
        public readonly CableConnectivityDiagnostics Diagnostics;
        public CablePathNotFoundException(CableConnectivityDiagnostics diagnostics)
            :base("未找到连续路径；起点真实分量 "+diagnostics.StartPhysicalComponent+"，终点真实分量 "+diagnostics.FinishPhysicalComponent+
                "；已记录 "+diagnostics.Candidates.Count+" 个断点候选（每个自由端口最近 "+diagnostics.TopNPerSource+" 个）") { Diagnostics=diagnostics; }
    }

    public sealed partial class CableNetwork
    {
        List<CableBoundaryPort> ReadPhysicalBoundaryPorts(HashSet<string> reserved)
        {
            var connected=new HashSet<string>(StringComparer.Ordinal);
            foreach(var join in Joins){connected.Add(Socket(join,true));connected.Add(Socket(join,false));}
            var result=new List<CableBoundaryPort>();
            for(int piece=0;piece<Pieces.Count;piece++)for(int port=0;port<Pieces[piece].Shape.Ports.Length;port++)
            {
                var location=WorldPort(piece,port);if(connected.Contains(Socket(location)))continue;
                var part=Pieces[piece].Shape;var p=part.Ports[port];
                result.Add(new CableBoundaryPort{Piece=piece,PieceId=part.Id,Name=part.Name,RunName=part.System,Port=port,PortId=p.Id,
                    PhysicalComponent=PhysicalPieceComponents[piece],Point=p.Point,Outward=p.Outward,Width=p.Width,Height=p.Height,
                    ReservedPhysicalAmbiguity=reserved.Contains(Socket(location))});
            }
            return result;
        }

        sealed class SegmentTarget { public int Piece,Edge;public Vec Min,Max; }
        SegmentTarget[] segmentTargets;
        SegmentTarget[] SegmentTargets()
        {
            if(segmentTargets!=null)return segmentTargets;
            var result=new List<SegmentTarget>();
            for(int piece=0;piece<Pieces.Count;piece++)for(int edge=0;edge<Pieces[piece].Shape.InternalEdges.Length;edge++)
            {
                var path=Pieces[piece].Shape.InternalEdges[edge];if(!IsVirtualSegmentTarget(Pieces[piece].Shape,path))continue;
                result.Add(new SegmentTarget{Piece=piece,Edge=edge,
                    Min=new Vec(path.Centerline.Min(p=>p.X),path.Centerline.Min(p=>p.Y),path.Centerline.Min(p=>p.Z)),
                    Max=new Vec(path.Centerline.Max(p=>p.X),path.Centerline.Max(p=>p.Y),path.Centerline.Max(p=>p.Z))});
            }
            return segmentTargets=result.ToArray();
        }

        static int CompareCandidate(VirtualProposal a,VirtualProposal b)
        {
            var x=a.Diagnostic;var y=b.Diagnostic;int compare=x.Distance3D.CompareTo(y.Distance3D);if(compare!=0)return compare;
            compare=string.Compare(x.TargetPieceId,y.TargetPieceId,StringComparison.Ordinal);if(compare!=0)return compare;
            compare=x.TargetPiece.CompareTo(y.TargetPiece);if(compare!=0)return compare;
            compare=x.Kind.CompareTo(y.Kind);if(compare!=0)return compare;
            compare=x.TargetPort.CompareTo(y.TargetPort);if(compare!=0)return compare;
            compare=x.TargetEdge.CompareTo(y.TargetEdge);return compare!=0?compare:x.TargetStation.CompareTo(y.TargetStation);
        }

        static string VirtualCandidateKey(VirtualConnectorCandidate d)
        {
            string source=d.SourcePiece+":"+d.SourcePort;
            if(d.Kind==VirtualConnectorKind.PortToPort3D)
            {
                string target=d.TargetPiece+":"+d.TargetPort;
                return string.Compare(source,target,StringComparison.Ordinal)<0?"port:"+source+"|"+target:"port:"+target+"|"+source;
            }
            return "segment:"+source+"|"+d.TargetPiece+":"+d.TargetEdge+":"+d.TargetStation.ToString("R",CultureInfo.InvariantCulture);
        }

        List<VirtualProposal> NearestVirtualCandidates(CableLocation source,IList<CableLocation> free,int topN,double radius,out int candidateCount,out VirtualProposal automatic)
        {
            var result=new List<VirtualProposal>();
            Action<VirtualProposal> retain=proposal=>{
                var d=proposal.Diagnostic;if(!Vec.IsFinite(d.Distance3D)||d.Distance3D>radius+1e-10)return;
                // Invalid directions stay in the diagnostic ranking; they never compete for routing.
                PassesVirtualFilters(proposal);result.Add(proposal);
            };
            int component=PhysicalPieceComponents[source.Piece];
            foreach(var target in free)
            {
                if(PhysicalPieceComponents[target.Piece]==component||(source.Point-target.Point).Norm>radius+1e-10)continue;
                retain(DescribeVirtual(source,target,VirtualConnectorKind.PortToPort3D));
            }
            foreach(var target in SegmentTargets())
            {
                if(PhysicalPieceComponents[target.Piece]==component)continue;
                double dx=Math.Max(0,Math.Max(target.Min.X-source.Point.X,source.Point.X-target.Max.X));
                double dy=Math.Max(0,Math.Max(target.Min.Y-source.Point.Y,source.Point.Y-target.Max.Y));
                double dz=Math.Max(0,Math.Max(target.Min.Z-source.Point.Z,source.Point.Z-target.Max.Z));
                if(Math.Sqrt(dx*dx+dy*dy+dz*dz)>radius+1e-10)continue;
                var location=ClosestOnInternalEdge(target.Piece,target.Edge,source.Point);var path=Pieces[target.Piece].Shape.InternalEdges[target.Edge];
                if(location.Station<=PositionEpsilon||path.Length-location.Station<=PositionEpsilon)continue;
                if((location.Point-source.Point).Norm>radius+1e-10)continue;
                retain(DescribeVirtual(source,location,VirtualConnectorKind.PortToSegment3D));
            }
            result.Sort(CompareCandidate);candidateCount=result.Count;
            for(int i=0;i<result.Count;i++)
            {
                var d=result[i].Diagnostic;d.CandidateRank=i+1;d.CandidateCount=candidateCount;d.WithinVirtualConnectorMaxDistance=d.Distance3D<=VirtualConnectorMaxDistance+1e-10;
            }
            // Search all radius-bounded candidates before truncating the diagnostic list. A legal
            // end-to-end connection must not disappear behind five closer segment projections.
            automatic=result.FirstOrDefault(p=>p.Diagnostic.Status=="DiagnosticOnly"&&p.Diagnostic.Kind==VirtualConnectorKind.PortToPort3D)
                ??result.FirstOrDefault(p=>p.Diagnostic.Status=="DiagnosticOnly"&&p.Diagnostic.Kind==VirtualConnectorKind.PortToSegment3D);
            return result.Take(topN).ToList();
        }

        void BuildVirtualCandidates(HashSet<string> occupied)
        {
            var free=PhysicalBoundaryPorts.Select(p=>WorldPort(p.Piece,p.Port)).Where(p=>!occupied.Contains(Socket(p))).ToArray();
            var nearest=new Dictionary<string,List<VirtualProposal>>(StringComparer.Ordinal);var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            var preferred=new Dictionary<string,VirtualProposal>(StringComparer.Ordinal);
            foreach(var source in free)
            {
                int total;VirtualProposal automatic;string socket=Socket(source);
                nearest.Add(socket,NearestVirtualCandidates(source,free,VirtualConnectorTopN,VirtualConnectorMaxDistance,out total,out automatic));
                counts.Add(socket,total);preferred.Add(socket,automatic);
            }
            foreach(var source in free)
            {
                string socket=Socket(source);var automatic=preferred[socket];
                foreach(var proposal in nearest[socket].Concat(automatic==null?Enumerable.Empty<VirtualProposal>():new[]{automatic}).Distinct())
                {
                    var d=proposal.Diagnostic;if(d.Kind!=VirtualConnectorKind.PortToPort3D)continue;
                    string target=Socket(proposal.B);d.TargetPortCandidateCount=counts[target];
                    var reverse=nearest[target].Concat(preferred[target]==null?Enumerable.Empty<VirtualProposal>():new[]{preferred[target]})
                        .FirstOrDefault(p=>p.Diagnostic.Kind==VirtualConnectorKind.PortToPort3D&&Socket(p.B)==socket);
                    d.TargetPortCandidateRank=reverse==null?0:reverse.Diagnostic.CandidateRank;
                }
                VirtualConnectorCandidates.AddRange(nearest[socket].Select(p=>p.Diagnostic));
            }
            var added=new HashSet<string>(StringComparer.Ordinal);
            foreach(var source in free)
            {
                string socket=Socket(source);var proposal=preferred[socket];if(proposal==null)continue;
                var d=proposal.Diagnostic;
                if(d.Kind==VirtualConnectorKind.PortToPort3D)
                {
                    string target=Socket(proposal.B);var reverse=preferred[target];
                    // An undirected connector consumes one slot at both ports. Conflicting
                    // nearest-port choices stay diagnostic; do not fall back to a segment.
                    if(reverse==null||reverse.Diagnostic.Kind!=VirtualConnectorKind.PortToPort3D||Socket(reverse.B)!=socket)
                    {d.Reason="目标 Port 的最近端口选择不一致；仅诊断，不回退到线段";continue;}
                }
                d.Status="Candidate";d.Reason="前向最近自动候选；未确认连通，必须复核";
                if(!added.Add(VirtualCandidateKey(d)))continue;
                Joins.Add(new CableJoin{A=proposal.A,B=proposal.B,Kind=d.Kind.ToString(),VirtualConnector=d,ConnectionPoint=proposal.B.Point,SurfaceGap=d.Distance3D,
                    RequiresReview=true,ReviewReason=string.Format(CultureInfo.InvariantCulture,"最近自动候选，需要复核: {0}; rank={1}/{2}; distance3D={3:F6} m; deltaX={4:F6} m; deltaY={5:F6} m; deltaZ={6:F6} m",
                        d.Kind,d.CandidateRank,d.CandidateCount,d.Distance3D,d.DeltaX,d.DeltaY,d.DeltaZ)});
            }
        }

        public CableConnectivityDiagnostics DiagnoseConnectivity(CableLocation start,CableLocation finish)
        {
            if(start==null||finish==null)throw new ArgumentNullException(start==null?"start":"finish");
            start=At(start.Piece,start.Edge,start.Station);finish=At(finish.Piece,finish.Edge,finish.Station);
            var result=new CableConnectivityDiagnostics{Start=start,Finish=finish,StartPhysicalComponent=PhysicalPieceComponents[start.Piece],
                FinishPhysicalComponent=PhysicalPieceComponents[finish.Piece],PhysicalComponentCount=PhysicalComponentCount,
                BoundaryPorts=PhysicalBoundaryPorts.ToList(),Candidates=new List<VirtualConnectorCandidate>(),
                TopNPerSource=VirtualConnectorTopN,Note="仅用于断点定位；跨所有其他真实分量搜索 Top-N，诊断不受虚拟连接候选半径限制，不会添加或确认任何边"};
            var free=PhysicalBoundaryPorts.Select(p=>WorldPort(p.Piece,p.Port)).ToArray();
            var routable=new HashSet<string>(Joins.Where(j=>j.IsVirtualConnector).Select(j=>VirtualCandidateKey(j.VirtualConnector)),StringComparer.Ordinal);
            foreach(var port in PhysicalBoundaryPorts.Where(p=>p.PhysicalComponent==result.StartPhysicalComponent))
            {
                int total;VirtualProposal unused;foreach(var proposal in NearestVirtualCandidates(WorldPort(port.Piece,port.Port),free,VirtualConnectorTopN,double.PositiveInfinity,out total,out unused))
                {
                    var d=proposal.Diagnostic;
                    if(routable.Contains(VirtualCandidateKey(d))){d.Status="Candidate";d.Reason="已入图的自动候选；仍需复核";}
                    else if(d.Status=="DiagnosticOnly")d.Reason="只展示断点；未自动入图";
                    result.Candidates.Add(d);
                }
            }
            result.Candidates=result.Candidates.OrderBy(p=>p.Distance3D).ThenBy(p=>p.SourcePieceId,StringComparer.Ordinal).ThenBy(p=>p.SourcePort).ThenBy(p=>p.CandidateRank).ToList();
            return result;
        }
    }
}
