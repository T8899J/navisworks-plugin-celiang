using System;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public enum CableNodeKind { Port, VirtualJunction, FittingInternalJunction }
    public enum CableEdgeKind { InternalEdge, ConnectionEdge, GapBridgeEdge, VirtualConnectorEdge }
    public enum VirtualConnectorKind { PortToPort3D, PortToSegment3D }

    public sealed class CableNetworkOptions
    {
        // All distances are world metres. Zero disables gap bridging for existing callers.
        public double GapBridgeMaxDistance { get; set; }
        public double GapBridgeWidthAxisTolerance { get; set; } = .002;
        public double GapBridgeHeightAxisTolerance { get; set; } = .002;
        public double GapBridgeSizeTolerance { get; set; } = .003;
        // Independent search radius, not a physical tolerance. Zero preserves legacy callers.
        public double VirtualConnectorMaxDistance { get; set; }
        public bool VirtualConnectorExperimentalTopN { get; set; }
        public int VirtualConnectorTopN { get; set; } = 5;
        // Degrees between a port's outward direction and the connector (both ends for Port-to-Port). Zero disables.
        public double VirtualConnectorMaxAngle { get; set; }
        // Rejects connectors between parallel trays whose axes are offset sideways by more than half a cross-section.
        public bool VirtualConnectorRejectParallelOffset { get; set; }
        // Recognized components kept in the port graph; the rest are reported as rejected. Diagnostics use the same value.
        public int MaxPieces { get; set; } = 2000;
        // When true, a splice box holding more than two ports is still bridged if exactly one pair faces
        // each other. False restores the original rule that requires exactly two ports.
        public bool SpliceBridgeUniqueFacingPair { get; set; } = true;
    }

    public sealed class VirtualConnectorCandidate
    {
        public VirtualConnectorKind Kind;
        public string KindName { get { return Kind.ToString(); } }
        public bool RequiresReview { get { return true; } }
        public bool Confirmed { get { return false; } }
        public int SourcePiece, SourcePort, TargetPiece, TargetPort, TargetEdge;
        public string SourcePieceId, SourceName, SourceRunName, SourcePortId;
        public string TargetPieceId, TargetName, TargetRunName, TargetPortId, TargetEdgeId;
        public Vec SourcePoint, TargetPoint;
        public double TargetStation, Distance3D, DeltaX, DeltaY, DeltaZ;
        public double SourceWidth, SourceHeight, TargetWidth, TargetHeight, WidthDifference, HeightDifference;
        public double? SourceDirectionAngleDegrees, TargetDirectionAngleDegrees, PortFacingAngleDegrees, TargetTangentAngleDegrees;
        public int SourcePhysicalComponent, TargetPhysicalComponent, CandidateCount, TargetPortCandidateCount;
        public int CandidateRank, TargetPortCandidateRank;
        public bool WithinVirtualConnectorMaxDistance;
        public string Status, Reason;
    }

    public sealed class CableGraphNode
    {
        public int Id, Piece;
        public string LocalId;
        public CableNodeKind Kind;
        public Vec Point;
    }

    public sealed class CableGraphEdge
    {
        public int Id, From, To, Piece, InternalEdge;
        public string EdgeId;
        public CableEdgeKind Kind;
        public double Length, FromStation, ToStation;
        public Vec[] Centerline;
        public double VerticalTravel { get { return CableDistance.VerticalTravel(Centerline).Metres; } }
        public bool RequiresReview;
        public string ReviewReason;
        public CableJoin Join;
    }

    public sealed class CableFittingContribution
    {
        public int Piece;
        public string Name, Kind;
        public double Length;
        public string[] EdgeIds;
    }
}
