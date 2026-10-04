using System;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    public enum CableNodeKind { Port, VirtualJunction, FittingInternalJunction }
    public enum CableEdgeKind { InternalEdge, ConnectionEdge }

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
