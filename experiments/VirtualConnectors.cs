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

        static double? LocalVirtualOffset(Vec delta,Vec portAxis,Vec pieceAxis)
        {
            var axis=portAxis.Finite&&portAxis.Norm>1e-9?portAxis:pieceAxis;
            return axis.Finite&&axis.Norm>1e-9?(double?)(delta.Dot(axis)/axis.Norm):null;
        }

        VirtualProposal DescribeVirtual(CableLocation a,CableLocation b,VirtualConnectorKind kind)
        {
            var source=Pieces[a.Piece].Shape;var target=Pieces[b.Piece].Shape;var port=source.Ports[a.Port];
            var delta=b.Point-a.Point;var d=new VirtualConnectorCandidate{
                Kind=kind,SourcePiece=a.Piece,SourcePieceId=source.Id,SourceName=source.Name,SourceRunName=source.System,SourcePort=a.Port,SourcePortId=port.Id,
                TargetPiece=b.Piece,TargetPieceId=target.Id,TargetName=target.Name,TargetRunName=target.System,TargetPort=b.Port,TargetPortId=b.Port<0?null:target.Ports[b.Port].Id,
                TargetEdge=b.Edge,TargetEdgeId=target.InternalEdges[b.Edge].Id,TargetStation=b.Station,SourcePoint=a.Point,TargetPoint=b.Point,
                Distance3D=delta.Norm,DeltaX=delta.X,DeltaY=delta.Y,DeltaZ=delta.Z,SourceWidth=port.Width,SourceHeight=port.Height,
                ForwardOffset=delta.Dot(port.Outward),
                WidthOffset=LocalVirtualOffset(delta,port.WidthAxis,Pieces[a.Piece].WidthAxis),
                HeightOffset=LocalVirtualOffset(delta,port.HeightAxis,Pieces[a.Piece].HeightAxis),
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
            if(VirtualConnectorRejectParallelOffset)
            {
                var targetAxis=kind==VirtualConnectorKind.PortToPort3D?target.Ports[b.Port].Outward:
                    target.InternalEdges[b.Edge].Centerline.Last()-target.InternalEdges[b.Edge].Centerline[0];
                double? axisAngle=DirectionAngle(port.Outward,targetAxis);
                bool parallel=axisAngle.HasValue&&(axisAngle.Value<=ParallelAngleDegrees||axisAngle.Value>=180-ParallelAngleDegrees);
                bool sideRisk=parallel&&d.WidthOffset.HasValue&&Math.Abs(d.WidthOffset.Value)>Math.Max(d.SourceWidth,d.TargetWidth)/2;
                // A height discontinuity (including a perpendicular port-to-segment jump) is a
                // review risk in its own local dimension, never a lateral-distance rejection.
                bool heightRisk=d.HeightOffset.HasValue&&Math.Abs(d.HeightOffset.Value)>Math.Max(d.SourceHeight,d.TargetHeight)/2;
                d.ParallelOffsetRisk=sideRisk||heightRisk;
            }
            return new VirtualProposal{A=a,B=b,Diagnostic=d};
        }

        const double ParallelAngleDegrees=15;
        // Admission uses the open forward half-space, never a fixed angular cone.
        // Directions were normalized by ValidatePart, so the epsilon is in world metres.
        bool PassesVirtualFilters(VirtualProposal proposal)
        {
            var d=proposal.Diagnostic;
            if(d.ForwardOffset<=PositionEpsilon)
            {
                d.Status="BehindSourcePort";d.Reason="目标不在源 Port 前向半空间内；仅诊断";return false;
            }
            if(d.Kind==VirtualConnectorKind.PortToPort3D&&
                Pieces[d.TargetPiece].Shape.Ports[d.TargetPort].Outward.Dot(proposal.A.Point-proposal.B.Point)<=PositionEpsilon)
            {
                d.Status="BehindTargetPort";d.Reason="源点不在目标 Port 前向半空间内；仅诊断";return false;
            }
            d.Status="DiagnosticOnly";d.Reason="未选为自动连接；仅诊断";
            return true;
        }

        void BuildVirtualConnectors(HashSet<string> occupied)
        {
            if(VirtualConnectorMaxDistance<=0)return;
            BuildVirtualCandidates(occupied);
        }

        internal struct PathCost
        {
            public int VirtualCount,GapCount;
            public CableDistance TotalLength,VerticalTravel,VirtualLength;
            public bool IsInfinite;
            public static PathCost Infinite { get { return new PathCost{IsInfinite=true}; } }
            public PathCost Add(CableGraphEdge edge)
            {
                bool virtualEdge=edge.Kind==CableEdgeKind.VirtualConnectorEdge;
                var length=CableDistance.FromMetres(edge.Length);
                return new PathCost{TotalLength=TotalLength+length,VerticalTravel=VerticalTravel+CableDistance.VerticalTravel(edge.Centerline),
                    VirtualCount=VirtualCount+(virtualEdge?1:0),VirtualLength=VirtualLength+(virtualEdge?length:new CableDistance()),
                    GapCount=GapCount+(edge.Kind==CableEdgeKind.GapBridgeEdge?1:0),IsInfinite=IsInfinite};
            }
            public int Compare(PathCost other)
            {
                if(IsInfinite!=other.IsInfinite)return IsInfinite?1:-1;if(IsInfinite)return 0;
                int comparison=TotalLength.CompareTo(other.TotalLength);if(comparison!=0)return comparison;
                comparison=VerticalTravel.CompareTo(other.VerticalTravel);if(comparison!=0)return comparison;
                comparison=VirtualCount.CompareTo(other.VirtualCount);if(comparison!=0)return comparison;
                comparison=VirtualLength.CompareTo(other.VirtualLength);if(comparison!=0)return comparison;
                return GapCount.CompareTo(other.GapCount);
            }
        }
    }
}
