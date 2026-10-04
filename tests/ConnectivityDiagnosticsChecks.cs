using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static CableNetwork DiagnosticNet(params CablePiece[] pieces)
    {
        return new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{
            GapBridgeMaxDistance=.05,VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true,VirtualConnectorTopN=5});
    }

    static void ConnectivityDiagnosticCases()
    {
        var source=Line("diagnostic-branch",new Vec(-2,0,0),new Vec(),.2);
        var near=Line("diagnostic-near-main",new Vec(.3,0,.2),new Vec(2.3,0,.2),.6);
        var other=Line("diagnostic-other-main",new Vec(.4,0,-.2),new Vec(2.4,0,-.2));
        var n=DiagnosticNet(source,near,other);
        var choices=n.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0&&d.SourcePort==1).ToArray();
        Check(choices.Length==2&&choices.All(d=>d.CandidateCount==2),"V12 retains multiple candidates instead of rejecting the free source port");
        Check(choices.Select(d=>d.CandidateRank).SequenceEqual(new[]{1,2})&&choices[0].Distance3D<choices[1].Distance3D,"V12 ranks candidates by actual 3D distance");
        Check(choices[0].Status=="Candidate"&&choices[1].Status=="DiagnosticOnly"&&choices.All(d=>d.RequiresReview&&!d.Confirmed),"only the nearest facing port candidate is routable; both remain unconfirmed diagnostics");
        var edges=VirtualEdges(n);Check(edges.All(e=>e.RequiresReview&&e.Join.RequiresReview&&e.Join.ReviewReason.Contains("rank=")),"every experimental graph connector carries review and ranking diagnostics");
        Check(edges.Select(e=>string.Join(":",new[]{e.From,e.To}.OrderBy(v=>v))).Distinct().Count()==edges.Length,"opposite source searches do not duplicate undirected port connectors");
        ReversibleVirtualRoute(n,n.AtPort(0,0),n.AtPort(1,1),4+Math.Sqrt(.13),"V12 ambiguous XYZ Port-to-Port candidate route");
        Check(!n.Ambiguities.Any(a=>a.Contains("VirtualConnector Ambiguous")),"experimental multiplicity does not mark all candidates unusable");
        Check(choices[0].SourceName!=choices[0].TargetName&&choices[0].SourceRunName!=choices[0].TargetRunName&&choices[0].WidthDifference>.3,"V12 uses names runs and sizes only as diagnostics");

        var main=Line("horizontal-main-V12",new Vec(),new Vec(10,0,0));
        var branch=Line("horizontal-branch-V12",new Vec(3,-2,0),new Vec(3,-.3,0),.1);
        n=DiagnosticNet(main,branch);var candidate=VirtualEdges(n).Single().Join.VirtualConnector;
        Check(candidate.Kind==VirtualConnectorKind.PortToSegment3D&&candidate.TargetPortId==null&&candidate.TargetEdgeId=="E0","V12 horizontal Port-to-Segment identifies target edge rather than a port");
        Near(candidate.TargetStation,3,"V12 horizontal centreline station");
        ReversibleVirtualRoute(n,n.AtPort(1,0),n.AtPort(0,1),9,"V12 horizontal branch candidate path");
        branch=Line("raised-branch-V12",new Vec(3,-2,.25),new Vec(3,-.3,.25),.1);
        n=DiagnosticNet(main,branch);candidate=VirtualEdges(n).Single().Join.VirtualConnector;
        Near(candidate.DeltaZ,-.25,"V12 raised Port-to-Segment retains signed Z difference");
        ReversibleVirtualRoute(n,n.AtPort(1,0),n.At(0,8),6.7+Math.Sqrt(.1525),"V12 different-height Port-to-Segment candidate path");
        var axis=Unit(new Vec(1,2,3));var origin=new Vec(110,220,33);var q=origin+axis*.7;var delta=Unit(axis.Cross(new Vec(.6,-.3,.1)))*.35;
        main=Line("XYZ-main-V12",origin-axis*3,origin+axis*5);main.Shape.Kind="Slope";
        main.Shape.InternalEdges[0].Centerline=new[]{origin-axis*3,origin,q,origin+axis*5};
        branch=Line("XYZ-branch-V12",q-delta-Unit(delta)*2,q-delta,.2);branch.Shape.Kind="Slope";
        n=DiagnosticNet(main,branch);candidate=VirtualEdges(n).Single().Join.VirtualConnector;
        Near((candidate.TargetPoint-q).Norm,0,"V12 arbitrary XYZ closest projection");Near(candidate.TargetStation,3.7,"V12 3D polyline station");
        ReversibleVirtualRoute(n,n.AtPort(1,0),n.AtPort(0,1),6.65,"V12 arbitrary XYZ Port-to-Segment candidate path");

        source=Line("combined-source",new Vec(-2,0,0),new Vec());
        main=Line("combined-segment",new Vec(-2,.3,0),new Vec(2,.3,0));
        near=Line("combined-port",new Vec(.2,0,.1),new Vec(2.2,0,.1));
        n=DiagnosticNet(source,main,near);choices=n.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0&&d.SourcePort==1).ToArray();
        Check(choices.Length==2&&choices.Select(d=>d.Kind).SequenceEqual(new[]{VirtualConnectorKind.PortToPort3D,VirtualConnectorKind.PortToSegment3D}),"Port and segment candidates compete in the same nearest-neighbour ranking");
        Check(choices.All(d=>d.CandidateCount==2),"mixed candidate count includes both virtual connector types");

        var pieces=new List<CablePiece>{Line("top-five-source",new Vec(0,0,-2),new Vec(),.1)};
        for(int i=1;i<=6;i++)pieces.Add(Line("top-five-main-"+i,new Vec(-2,0,i*.08),new Vec(2,0,i*.08)));
        n=DiagnosticNet(pieces.ToArray());choices=n.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0&&d.SourcePort==1).ToArray();
        Check(choices.Length==5&&choices.All(d=>d.CandidateCount==6),"V12 limits stored source candidates to Top 5 but records full eligible count");
        Check(choices.Select(d=>d.CandidateRank).SequenceEqual(Enumerable.Range(1,5))&&choices.Select(d=>d.TargetPiece).SequenceEqual(Enumerable.Range(1,5)),"Top 5 excludes the sixth farther target without a uniqueness requirement");
        Check(n.GraphEdges.Where(e=>e.Kind==CableEdgeKind.VirtualConnectorEdge).All(e=>e.Join.VirtualConnector.SourcePhysicalComponent!=e.Join.VirtualConnector.TargetPhysicalComponent),"all experimental connectors span distinct physical components");

        var connected=DiagnosticNet(Line("visible-path-A",new Vec(),new Vec(1,0,0)),Line("visible-path-B",new Vec(1,0,0),new Vec(2,0,0)));
        Check(connected.PhysicalBoundaryPorts.Count==2&&connected.PhysicalBoundaryPorts.All(p=>!(p.Piece==0&&p.Port==1)&&!(p.Piece==1&&p.Port==0)),"boundary-port diagnostics exclude physically connected sockets");
        n=new CableNetwork(new List<CablePiece>{ConfirmedDetour(),Line("real-detour-A",new Vec(),new Vec(1,0,0)),Line("real-detour-B",new Vec(1.03,0,0),new Vec(2.03,0,0))},options:new CableNetworkOptions{VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true});
        Check(n.PhysicalComponentCount==1&&VirtualEdges(n).Length==0,"real connected component cannot gain an experimental shortcut");
        Near(n.Find(n.AtPort(0,0),n.AtPort(0,1)).Length,14.03,"existing real detour is retained in Top-N mode");
        n=DiagnosticNet(Line("strict-gap-V12-A",new Vec(),new Vec(1,0,0)),Line("strict-gap-V12-B",new Vec(1.03,0,0),new Vec(2.03,0,0)));
        Check(HasGap(n)&&VirtualEdges(n).Length==0,"Top-N does not duplicate ports occupied by an accepted strict GapBridge");
        Near(n.PhysicalTolerance,.002,"V12 preserves 2mm physical tolerance");Near(n.GapBridgeMaxDistance,.05,"V12 preserves 50mm strict GapBridge limit");
        Near(n.GapBridgeWidthAxisTolerance,.002,"V12 preserves strict width deviation tolerance");Near(n.GapBridgeHeightAxisTolerance,.002,"V12 preserves strict height deviation tolerance");

        ConnectivityFailureCases();VirtualCostCases(true);
    }

    static void ConnectivityFailureCases()
    {
        var pieces=new List<CablePiece>{Line("failure-source",new Vec(-2,0,0),new Vec(),.2)};
        for(int i=0;i<7;i++)pieces.Add(Line("failure-main-"+i,new Vec(-3,1+i*.4,.2+i*.1),new Vec(3,1+i*.4,.2+i*.1),.4+i*.02));
        var n=DiagnosticNet(pieces.ToArray());int nodes=n.GraphNodes.Count,edges=n.GraphEdges.Count,joins=n.Joins.Count;
        CablePathNotFoundException failure=null;
        try{n.Find(n.AtPort(0,0),n.AtPort(7,1));}catch(CablePathNotFoundException e){failure=e;}
        Check(failure!=null,"failed Find returns typed graph-connectivity diagnostics");var d=failure.Diagnostics;
        Check(d.StartPhysicalComponent==n.PhysicalPieceComponents[0]&&d.FinishPhysicalComponent==n.PhysicalPieceComponents[7]&&d.PhysicalComponentCount==8,"failure identifies endpoint physical components");
        Check(d.BoundaryPorts.Count==16&&d.BoundaryPorts.Select(p=>p.PhysicalComponent).Distinct().Count()==8,"failure reports free boundary ports of all components");
        Check(d.Candidates.Count==10&&d.Candidates.GroupBy(c=>c.SourcePort).All(g=>g.Count()==5&&g.Select(c=>c.CandidateRank).OrderBy(r=>r).SequenceEqual(Enumerable.Range(1,5))),"failed start component gets Top 5 per free port");
        Check(d.Candidates.All(c=>c.Distance3D>.5&&!c.WithinVirtualConnectorMaxDistance&&
            (c.ForwardOffset<=1e-7?c.Status=="BehindSourcePort":c.Kind==VirtualConnectorKind.PortToPort3D&&pieces[c.TargetPiece].Shape.Ports[c.TargetPort].Outward.Dot(c.SourcePoint-c.TargetPoint)<=1e-7?c.Status=="BehindTargetPort":c.Status=="DiagnosticOnly")),
            "breakpoint diagnostics retain exact direction statuses beyond the graph candidate radius");
        Check(d.Candidates.Any(c=>c.Kind==VirtualConnectorKind.PortToSegment3D)&&d.Candidates.Any(c=>c.Kind==VirtualConnectorKind.PortToPort3D),"failure ranks port and segment targets together");
        foreach(var c in d.Candidates)
        {
            Near(c.Distance3D,Math.Sqrt(c.DeltaX*c.DeltaX+c.DeltaY*c.DeltaY+c.DeltaZ*c.DeltaZ),"failure candidate actual XYZ distance");
            Near((c.TargetPoint-c.SourcePoint-new Vec(c.DeltaX,c.DeltaY,c.DeltaZ)).Norm,0,"failure candidate signed XYZ deltas");
        }
        Check(d.Candidates.All(c=>!string.IsNullOrEmpty(c.SourcePieceId)&&!string.IsNullOrEmpty(c.TargetPieceId)&&!string.IsNullOrEmpty(c.SourceRunName)&&c.SourceDirectionAngleDegrees.HasValue&&
            (c.TargetDirectionAngleDegrees.HasValue||c.TargetTangentAngleDegrees.HasValue)&&c.SourceWidth>0&&c.SourceHeight>0&&c.TargetWidth>0&&c.TargetHeight>0),"failure diagnostic includes identities runs component direction angles and actual section sizes");
        Check(d.DiagnosticOnly&&n.GraphNodes.Count==nodes&&n.GraphEdges.Count==edges&&n.Joins.Count==joins,"read-only failed-route diagnostics do not insert or confirm graph edges");
        var reverse=n.DiagnoseConnectivity(n.AtPort(7,1),n.AtPort(0,0));
        Check(reverse.StartPhysicalComponent==d.FinishPhysicalComponent&&reverse.FinishPhysicalComponent==d.StartPhysicalComponent,"reversed failed query reports the reversed physical components");
        Check(d.Candidates.SequenceEqual(d.Candidates.OrderBy(c=>c.Distance3D).ThenBy(c=>c.SourcePieceId,StringComparer.Ordinal).ThenBy(c=>c.SourcePort).ThenBy(c=>c.CandidateRank)),"first displayed breakpoint is the nearest recorded candidate");
        var overlap=DiagnosticNet(Line("reserved-source",new Vec(),new Vec(1,0,0)),Line("reserved-target-1",new Vec(1,0,0),new Vec(2,0,0)),
            Line("reserved-target-2",new Vec(1,0,0),new Vec(3,0,0)),Line("near-reserved",new Vec(1.3,0,.2),new Vec(3.3,0,.2)));
        Check(overlap.PhysicalBoundaryPorts.Any(p=>p.Piece==0&&p.Port==1&&p.ReservedPhysicalAmbiguity),"unresolved physical ambiguity remains visible in boundary diagnostics");
        Check(overlap.VirtualConnectorCandidates.All(c=>!(c.SourcePiece==0&&c.SourcePort==1)&&!(c.TargetPiece==0&&c.TargetPort==1)),"experimental virtual edges do not bypass existing physical ambiguity protections");
    }
}
