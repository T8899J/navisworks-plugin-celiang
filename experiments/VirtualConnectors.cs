using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public sealed partial class CableNetwork
    {
        int[] ReadPhysicalComponents(Graph graph)
        {
            var adjacency=Enumerable.Range(0,graph.Nodes.Count).Select(i=>new List<int>()).ToArray();
            foreach(var edge in graph.Edges)
            {
                if(edge.Kind!=CableEdgeKind.InternalEdge&&edge.Kind!=CableEdgeKind.ConnectionEdge)continue;
                adjacency[edge.From].Add(edge.To);adjacency[edge.To].Add(edge.From);
            }
            var components=Enumerable.Repeat(-1,graph.Nodes.Count).ToArray();int next=0;
            for(int node=0;node<components.Length;node++)if(components[node]<0)
            {
                var pending=new Queue<int>();pending.Enqueue(node);components[node]=next;
                while(pending.Count>0){int current=pending.Dequeue();foreach(int target in adjacency[current])if(components[target]<0){components[target]=next;pending.Enqueue(target);}}
                next++;
            }
            var pieces=Enumerable.Repeat(-1,Pieces.Count).ToArray();
            foreach(var node in graph.Nodes)pieces[node.Piece]=components[node.Id];
            return pieces;
        }

        sealed class VirtualProposal
        {
            public CableLocation A,B;
            public VirtualConnectorCandidate Diagnostic;
        }

        CableLocation WorldPort(int piece,int port)
        {
            var location=AtPort(piece,port);location.Point=Pieces[piece].Shape.Ports[port].Point;return location;
        }

        static bool IsVirtualSegmentTarget(Part part,InternalEdge edge)
        {
            string kind=(part.Kind??"").ToLowerInvariant();
            if(kind!="straight"&&kind!="slope"&&kind!="slopestraight")return false;
            var direction=edge.Centerline.Last()-edge.Centerline[0];if(direction.Norm<1e-9)return false;direction=direction*(1/direction.Norm);
            // Classification and the centreline geometry determine eligibility. Cross-section
            // frames and size differences are diagnostics, not virtual-connector filters.
            var axis=direction;return edge.Centerline.All(p=>(p-edge.Centerline[0]-axis*((p-edge.Centerline[0]).Dot(axis))).Norm<=1e-6);
        }

        CableLocation ClosestOnInternalEdge(int piece,int edge,Vec point)
        {
            var line=Pieces[piece].Shape.InternalEdges[edge].Centerline;double total=0,best=double.PositiveInfinity,station=0;
            for(int i=1;i<line.Length;i++)
            {
                var delta=line[i]-line[i-1];double length=delta.Norm;
                double t=Math.Max(0,Math.Min(1,(point-line[i-1]).Dot(delta)/delta.Dot(delta)));
                var projected=line[i-1]+delta*t;double distance=(projected-point).Norm;
                if(distance<best){best=distance;station=total+t*length;}total+=length;
            }
            return At(piece,edge,station);
        }

        static double? DirectionAngle(Vec direction,Vec displacement)
        {
            double length=direction.Norm*displacement.Norm;if(length<1e-15)return null;
            return Math.Acos(Math.Max(-1,Math.Min(1,direction.Dot(displacement)/length)))*180/Math.PI;
        }

        VirtualProposal DescribeVirtual(CableLocation a,CableLocation b,VirtualConnectorKind kind)
        {
            var source=Pieces[a.Piece].Shape;var target=Pieces[b.Piece].Shape;var port=source.Ports[a.Port];
            var delta=b.Point-a.Point;var d=new VirtualConnectorCandidate{
                Kind=kind,SourcePiece=a.Piece,SourcePieceId=source.Id,SourceName=source.Name,SourceRunName=source.System,SourcePort=a.Port,SourcePortId=port.Id,
                TargetPiece=b.Piece,TargetPieceId=target.Id,TargetName=target.Name,TargetRunName=target.System,TargetPort=b.Port,TargetPortId=b.Port<0?null:target.Ports[b.Port].Id,
                TargetEdge=b.Edge,TargetEdgeId=target.InternalEdges[b.Edge].Id,TargetStation=b.Station,SourcePoint=a.Point,TargetPoint=b.Point,
                Distance3D=delta.Norm,DeltaX=delta.X,DeltaY=delta.Y,DeltaZ=delta.Z,SourceWidth=port.Width,SourceHeight=port.Height,
                SourceDirectionAngleDegrees=DirectionAngle(port.Outward,delta),SourcePhysicalComponent=PhysicalPieceComponents[a.Piece],TargetPhysicalComponent=PhysicalPieceComponents[b.Piece]};
            if(kind==VirtualConnectorKind.PortToPort3D)
            {
                var q=target.Ports[b.Port];d.TargetWidth=q.Width;d.TargetHeight=q.Height;
                d.TargetDirectionAngleDegrees=DirectionAngle(q.Outward,delta*(-1));d.PortFacingAngleDegrees=DirectionAngle(port.Outward,q.Outward*(-1));
            }
            else
            {
                var edge=target.InternalEdges[b.Edge];var p0=target.Ports.FirstOrDefault(p=>p.Id==edge.From)??target.Ports[0];
                var p1=target.Ports.FirstOrDefault(p=>p.Id==edge.To)??p0;double t=b.Station/edge.Length;
                d.TargetWidth=p0.Width+(p1.Width-p0.Width)*t;d.TargetHeight=p0.Height+(p1.Height-p0.Height)*t;
                d.TargetTangentAngleDegrees=DirectionAngle(edge.Centerline.Last()-edge.Centerline[0],delta);
            }
            d.WidthDifference=d.TargetWidth-d.SourceWidth;d.HeightDifference=d.TargetHeight-d.SourceHeight;
            return new VirtualProposal{A=a,B=b,Diagnostic=d};
        }

        void BuildVirtualConnectors(HashSet<string> occupied)
        {
            if(VirtualConnectorMaxDistance<=0)return;
            var free=new List<CableLocation>();
            for(int piece=0;piece<Pieces.Count;piece++)for(int port=0;port<Pieces[piece].Shape.Ports.Length;port++)
            {
                var location=WorldPort(piece,port);if(!occupied.Contains(Socket(location)))free.Add(location);
            }
            var proposals=new List<VirtualProposal>();
            Action<VirtualProposal> add=proposal=>{
                var d=proposal.Diagnostic;if(!Vec.IsFinite(d.Distance3D)||d.Distance3D>VirtualConnectorMaxDistance+1e-10)return;
                VirtualConnectorCandidates.Add(d);
                if(d.SourcePhysicalComponent==d.TargetPhysicalComponent)
                {d.Status="SkippedSamePhysicalComponent";d.Reason="已通过真实 InternalEdge / ConnectionEdge 连通，不创建虚拟捷径";return;}
                proposals.Add(proposal);
            };
            for(int i=0;i<free.Count;i++)for(int j=i+1;j<free.Count;j++)if(free[i].Piece!=free[j].Piece)
            {
                if((free[i].Point-free[j].Point).Norm>VirtualConnectorMaxDistance+1e-10)continue;
                add(DescribeVirtual(free[i],free[j],VirtualConnectorKind.PortToPort3D));
            }
            foreach(var source in free)for(int piece=0;piece<Pieces.Count;piece++)if(source.Piece!=piece)
            {
                var part=Pieces[piece].Shape;
                for(int edge=0;edge<part.InternalEdges.Length;edge++)
                {
                    var path=part.InternalEdges[edge];if(!IsVirtualSegmentTarget(part,path))continue;
                    var target=ClosestOnInternalEdge(piece,edge,source.Point);
                    // End stations belong to Port-to-Port candidates. Taking only the closest
                    // projection over all segments also avoids duplicate candidates at vertices.
                    if(target.Station<=PositionEpsilon||path.Length-target.Station<=PositionEpsilon)continue;
                    if((target.Point-source.Point).Norm>VirtualConnectorMaxDistance+1e-10)continue;
                    add(DescribeVirtual(source,target,VirtualConnectorKind.PortToSegment3D));
                }
            }
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var proposal in proposals)
            {
                var sockets=proposal.Diagnostic.Kind==VirtualConnectorKind.PortToPort3D?new[]{Socket(proposal.A),Socket(proposal.B)}:new[]{Socket(proposal.A)};
                foreach(string socket in sockets){int count;counts.TryGetValue(socket,out count);counts[socket]=count+1;}
            }
            foreach(var proposal in proposals)
            {
                var d=proposal.Diagnostic;d.CandidateCount=counts[Socket(proposal.A)];
                d.TargetPortCandidateCount=d.Kind==VirtualConnectorKind.PortToPort3D?counts[Socket(proposal.B)]:0;
                if(d.CandidateCount!=1||(d.Kind==VirtualConnectorKind.PortToPort3D&&d.TargetPortCandidateCount!=1))
                {
                    d.Status="Ambiguous";d.Reason="候选不唯一，禁止自动连接";
                    Ambiguities.Add("VirtualConnector Ambiguous: "+Label(d.SourcePiece)+" / "+d.SourcePortId+" ("+d.CandidateCount+") -> "+
                        Label(d.TargetPiece)+" / "+(d.TargetPortId??d.TargetEdgeId+"@"+d.TargetStation.ToString("R",CultureInfo.InvariantCulture))+" ("+d.TargetPortCandidateCount+")");
                    continue;
                }
                d.Status="Accepted";d.Reason="不同真实连通分量之间的唯一候选，需确认实际可穿缆";
                string review=string.Format(CultureInfo.InvariantCulture,"{0} 虚拟连接待复核: distance3D={1:F6} m; deltaX={2:F6} m; deltaY={3:F6} m; deltaZ={4:F6} m; candidates={5}/{6}",
                    d.Kind,d.Distance3D,d.DeltaX,d.DeltaY,d.DeltaZ,d.CandidateCount,d.TargetPortCandidateCount);
                Joins.Add(new CableJoin{A=proposal.A,B=proposal.B,Kind=d.Kind.ToString(),VirtualConnector=d,ConnectionPoint=proposal.B.Point,
                    SurfaceGap=d.Distance3D,RequiresReview=true,ReviewReason=review});
            }
        }

        struct PathCost
        {
            public int VirtualCount,GapCount;
            public double VirtualLength,TotalLength;
            public static PathCost Infinite { get { return new PathCost{VirtualCount=int.MaxValue,GapCount=int.MaxValue,VirtualLength=double.PositiveInfinity,TotalLength=double.PositiveInfinity}; } }
            public PathCost Add(CableGraphEdge edge)
            {
                bool virtualEdge=edge.Kind==CableEdgeKind.VirtualConnectorEdge;
                return new PathCost{VirtualCount=VirtualCount+(virtualEdge?1:0),VirtualLength=VirtualLength+(virtualEdge?edge.Length:0),
                    GapCount=GapCount+(edge.Kind==CableEdgeKind.GapBridgeEdge?1:0),TotalLength=TotalLength+edge.Length};
            }
            public int Compare(PathCost other,bool virtualMode)
            {
                if(virtualMode)
                {
                    int comparison=VirtualCount.CompareTo(other.VirtualCount);if(comparison!=0)return comparison;
                    comparison=VirtualLength.CompareTo(other.VirtualLength);if(comparison!=0)return comparison;
                }
                else
                {
                    // Keep the strict GapBridge-only API mode backward compatible when the
                    // independent VirtualConnector feature is disabled.
                    int comparison=GapCount.CompareTo(other.GapCount);if(comparison!=0)return comparison;
                }
                return TotalLength.CompareTo(other.TotalLength);
            }
        }
    }
}
