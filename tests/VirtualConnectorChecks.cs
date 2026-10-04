using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static CableNetwork VirtualNet(params CablePiece[] pieces)
    {
        return new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{VirtualConnectorMaxDistance=.5});
    }
    static CableGraphEdge[] VirtualEdges(CableNetwork network)
    {
        return network.GraphEdges.Where(e=>e.Kind==CableEdgeKind.VirtualConnectorEdge).ToArray();
    }
    static void ReversibleVirtualRoute(CableNetwork network,CableLocation start,CableLocation finish,double length,string name)
    {
        var route=network.Find(start,finish);var reverse=network.Find(finish,start);
        Near(route.Length,length,name+" counts actual XYZ travel");Near(reverse.Length,length,name+" reverse length is identical");
        Near(route.Steps.Sum(s=>s.Length),route.Length,name+" physical total equals traversed edges");
        Check(reverse.TotalLength==route.TotalLength,name+" forward/reverse total length is exactly equal");
        Check(reverse.VirtualConnectorTotalLength==route.VirtualConnectorTotalLength,name+" reverse connector contribution is exactly equal");
        Check(reverse.VerticalTravel==route.VerticalTravel,name+" reverse vertical travel is exactly equal");Check(reverse.VirtualConnectorCount==route.VirtualConnectorCount,name+" reverse virtual count is identical");
        Check(route.RequiresReview&&route.VirtualConnectorCount>0,name+" is always a review route");
        Reject(()=>network.Find(start,finish,false),name+" is excluded from strict mode");
    }
    static CablePiece ShiftVirtualHub(string name,double x,double centreY,bool right,double z)
    {
        var piece=GapTestHub(name,x,centreY,right);var offset=new Vec(0,0,z);
        foreach(var port in piece.Shape.Ports)port.Point+=offset;
        foreach(var junction in piece.Shape.Junctions)junction.Point+=offset;
        foreach(var edge in piece.Shape.InternalEdges)edge.Centerline=edge.Centerline.Select(p=>p+offset).ToArray();
        return piece;
    }
    static void VirtualConnectorCases(string artifacts)
    {
        var a=Line("3D-source",new Vec(-2,0,0),new Vec());
        var b=Line("3D-target-other-size",new Vec(.3,0,.2),new Vec(2.3,0,.2),.6);
        Check(VirtualEdges(Net(a,b)).Length==0,"VirtualConnector is opt-in for legacy API callers");
        var n=VirtualNet(a,b);var edge=VirtualEdges(n).Single();var diagnostic=edge.Join.VirtualConnector;
        Check(diagnostic.Kind==VirtualConnectorKind.PortToPort3D,"different Z heights use PortToPort3D");
        Near(edge.Length,Math.Sqrt(.13),"3D connector uses sqrt(dx^2+dy^2+dz^2), not XY distance");
        Check(n.PhysicalComponentCount==2&&diagnostic.SourcePhysicalComponent!=diagnostic.TargetPhysicalComponent,"virtual connector spans different physical components");
        Check(edge.RequiresReview&&edge.Join.RequiresReview&&diagnostic.Status=="Accepted","accepted virtual connector still requires review");
        Near(diagnostic.DeltaX,.3,"candidate logs delta X");Near(diagnostic.DeltaY,0,"candidate logs delta Y");Near(diagnostic.DeltaZ,.2,"candidate logs delta Z");
        Near(diagnostic.SourceDirectionAngleDegrees.Value,Math.Atan2(.2,.3)*180/Math.PI,"candidate logs actual port-to-connector angle");
        Near(diagnostic.WidthDifference,.2,"section size difference is diagnostic rather than a hard condition");
        Check(diagnostic.CandidateCount==1&&diagnostic.TargetPortCandidateCount==1&&diagnostic.SourcePortId=="P1"&&diagnostic.TargetPortId=="P0","candidate records both port identities and mutual counts");
        Check(diagnostic.SourceRunName!=diagnostic.TargetRunName&&diagnostic.SourceName!=diagnostic.TargetName,"different names and RunNames do not sever a virtual connection");
        ReversibleVirtualRoute(n,n.AtPort(0,0),n.AtPort(1,1),4+Math.Sqrt(.13),"different-height Port-to-Port");
        Near(n.Find(n.AtPort(0,0),n.AtPort(1,1)).VirtualConnectorTotalLength,Math.Sqrt(.13),"route exposes actual virtual connector contribution");

        var origin=new Vec(50000,-20000,3000);var axis=Unit(new Vec(1,2,3));var delta=new Vec(.2,-.15,.25);
        a=Line("XYZ-A",origin-axis*2,origin);b=Line("XYZ-B",origin+delta,origin+delta+axis*2);
        n=VirtualNet(a,b);edge=VirtualEdges(n).Single();Near(edge.Length,Math.Sqrt(.125),"arbitrary XYZ connector uses all three delta components",1e-10);
        ReversibleVirtualRoute(n,n.AtPort(0,0),n.AtPort(1,1),4+Math.Sqrt(.125),"arbitrary XYZ Port-to-Port");
        b.Shape.Ports[0].Outward=axis; // Intentionally faces away. Angles are collected, not filtered in this phase.
        n=VirtualNet(a,b);diagnostic=VirtualEdges(n).Single().Join.VirtualConnector;
        Near(diagnostic.PortFacingAngleDegrees.Value,180,"wrong-facing geometry remains reviewable with an explicit 180-degree angle");

        var perturbed=Line("actual-port",new Vec(-2,0,0),new Vec());perturbed.Shape.Ports[1].Point=new Vec(8e-8,0,0);
        n=VirtualNet(perturbed,Line("exact-target",new Vec(.3,0,.2),new Vec(2.3,0,.2)));
        edge=VirtualEdges(n).Single();Near(edge.Length,Math.Sqrt((.3-8e-8)*(.3-8e-8)+.04),"virtual length uses actual world Port, not a nearby station approximation",1e-12);
        Near((edge.Centerline[1]-edge.Centerline[0]).Norm,edge.Length,"virtual overlay endpoints match its physical length",1e-12);

        var main=Line("horizontal-main",new Vec(),new Vec(10,0,0),.4);
        var branch=Line("horizontal-branch",new Vec(3,-2,0),new Vec(3,-.3,0),.1);
        n=VirtualNet(main,branch);edge=VirtualEdges(n).Single();diagnostic=edge.Join.VirtualConnector;
        Check(diagnostic.Kind==VirtualConnectorKind.PortToSegment3D&&diagnostic.TargetPort==-1&&diagnostic.TargetEdgeId=="E0","horizontal Port-to-Segment identifies target internal edge");
        Near(diagnostic.TargetStation,3,"horizontal projection station is on the target centreline");
        Near(diagnostic.TargetPoint.X,3,"horizontal projection world X");Near(diagnostic.TargetPoint.Y,0,"horizontal projection world Y");Near(edge.Length,.3,"horizontal side gap contributes P-to-Q only");
        Check(n.GraphNodes.Count(v=>v.Kind==CableNodeKind.VirtualJunction)==1,"Port-to-Segment creates a single VirtualJunction");
        var mainEdges=n.GraphEdges.Where(e=>e.Kind==CableEdgeKind.InternalEdge&&e.Piece==0).ToArray();
        Check(mainEdges.Length==2,"target InternalEdge is logically split at the projected station");Near(mainEdges.Sum(e=>e.Length),10,"station split preserves full main centreline length");
        Near(main.Shape.InternalEdges.Single().Length,10,"virtual station splitting does not mutate the source internal edge");
        ReversibleVirtualRoute(n,n.AtPort(1,0),n.AtPort(0,1),9,"horizontal Port-to-Segment");
        int nodes=n.GraphNodes.Count,edges=n.GraphEdges.Count;
        ReversibleVirtualRoute(n,n.At(1,.5),n.At(0,8),6.5,"partial branch-to-main station query");
        Check(n.GraphNodes.Count==nodes&&n.GraphEdges.Count==edges,"query-specific splits preserve the base virtual graph");

        branch=Line("raised-branch",new Vec(3,-2,.25),new Vec(3,-.3,.25),.1);
        n=VirtualNet(main,branch);diagnostic=VirtualEdges(n).Single().Join.VirtualConnector;
        Near(diagnostic.Distance3D,Math.Sqrt(.1525),"Port-to-Segment with height gap uses 3D distance");
        Near(diagnostic.DeltaZ,-.25,"Port-to-Segment logs the signed height change");
        ReversibleVirtualRoute(n,n.AtPort(1,0),n.At(0,8),6.7+Math.Sqrt(.1525),"raised Port-to-Segment");

        origin=new Vec(10,20,30);axis=Unit(new Vec(1,2,3));var q=origin+axis*.7;delta=Unit(axis.Cross(new Vec(.6,-.3,.1)))*.35;
        main=Line("sloping-main",origin-axis*3,origin+axis*5);main.Shape.Kind="Slope";
        main.Shape.InternalEdges[0].Centerline=new[]{origin-axis*3,origin,q,origin+axis*5};
        branch=Line("XYZ-branch",q-delta-Unit(delta)*2,q-delta,.2);branch.Shape.Kind="Slope";
        n=VirtualNet(main,branch);diagnostic=VirtualEdges(n).Single().Join.VirtualConnector;
        Near((diagnostic.TargetPoint-q).Norm,0,"XYZ projection reaches the independently constructed perpendicular foot");
        Near(diagnostic.TargetStation,3.7,"XYZ polyline projection accumulates 3D stations");Near(diagnostic.Distance3D,.35,"XYZ Port-to-Segment uses full perpendicular distance");
        Check(n.VirtualConnectorCandidates.Count(d=>d.Status=="Accepted")==1,"projection at a shared polyline vertex is deduplicated");
        ReversibleVirtualRoute(n,n.AtPort(1,0),n.AtPort(0,1),6.65,"XYZ slope Port-to-Segment");

        main=Line("reversed-main",new Vec(10,0,0),new Vec());branch=Line("reversed-branch",new Vec(3,-2,.25),new Vec(3,-.3,.25));
        n=VirtualNet(main,branch);Near(VirtualEdges(n).Single().Join.VirtualConnector.TargetStation,7,"reversing main centreline reverses station rather than XYZ projection");
        Near(n.Find(n.AtPort(1,0),n.AtPort(0,0)).Length,8.7+Math.Sqrt(.1525),"reversed main orientation preserves physical travel");
        main=Line("endpoint-target",new Vec(),new Vec(10,0,0));branch=Line("endpoint-source",new Vec(-2,-.2,.1),new Vec(-.2,-.2,.1));
        n=VirtualNet(main,branch);Check(VirtualEdges(n).Single().Join.VirtualConnector.Kind==VirtualConnectorKind.PortToPort3D,"clamped endpoint projection is a Port-to-Port candidate, not a middle junction");
        Check(!n.GraphNodes.Any(v=>v.Kind==CableNodeKind.VirtualJunction),"endpoint projection does not create a spurious internal junction");

        a=Line("connected-detour-A",new Vec(),new Vec(1,0,0));b=Line("connected-detour-B",new Vec(1.03,0,0),new Vec(2.03,0,0));
        n=VirtualNet(ConfirmedDetour(),a,b);
        Check(n.PhysicalComponentCount==1&&n.Joins.Count==2,"all real physical connections precede component detection");
        Check(VirtualEdges(n).Length==0&&n.VirtualConnectorCandidates.Any(d=>d.Status=="SkippedSamePhysicalComponent"),"existing real path prevents creating a 30mm virtual shortcut");
        var route=n.Find(n.AtPort(0,0),n.AtPort(0,1));Near(route.Length,14.03,"connected endpoints keep their full real detour");Check(route.VirtualConnectorCount==0,"confirmed route contributes no virtual connector");

        var detour=ConfirmedDetour();var internalJunction=new Vec(1.5,-5,0);
        detour.Shape.Ports[1].Point=new Vec(3,0,0);detour.Shape.Ports[2].Point=new Vec(1.5,-6,0);detour.Shape.Junctions[0].Point=internalJunction;
        detour.Shape.InternalEdges[0].Centerline=new[]{new Vec(),new Vec(-.5,0,0),new Vec(-.5,-5,0),internalJunction};
        detour.Shape.InternalEdges[1].Centerline=new[]{new Vec(3,0,0),new Vec(3.5,0,0),new Vec(3.5,-5,0),internalJunction};
        detour.Shape.InternalEdges[2].Centerline=new[]{detour.Shape.Ports[2].Point,internalJunction};
        n=VirtualNet(detour,Line("real-A",new Vec(),new Vec(1,0,0)),Line("real-B",new Vec(2,0,0),new Vec(3,0,0)),
            Line("outside-component",new Vec(1,.3,.2),new Vec(2,.3,.2)));
        Check(VirtualEdges(n).Length==2,"different-component connectors can coexist with a real detour without linking within the real component");
        route=n.Find(n.AtPort(1,1),n.AtPort(2,0));Near(route.Length,1+2*Math.Sqrt(.13),"shorter total route wins over a long confirmed physical detour");
        Check(route.VirtualConnectorCount==2&&route.RequiresReview,"shorter external bypass remains explicitly reviewable with both virtual connectors");
        CostReverse(n,n.AtPort(1,1),n.AtPort(2,0),route,"shorter reviewed bypass");

        a=Line("ambiguous-source",new Vec(-2,0,0),new Vec());
        b=Line("near-candidate",new Vec(.3,0,.2),new Vec(2.3,0,.2));var c=Line("far-candidate",new Vec(.4,0,-.2),new Vec(2.4,0,-.2));
        n=VirtualNet(a,b,c);Check(VirtualEdges(n).Length==0&&n.Ambiguities.Any(s=>s.Contains("VirtualConnector Ambiguous")),"multiple Port-to-Port candidates reject all rather than choosing nearest");
        Check(n.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0).All(d=>d.Status=="Ambiguous"&&d.CandidateCount==2),"ambiguous diagnostics retain full candidate counts");
        Check(VirtualEdges(VirtualNet(c,b,a)).Length==0,"mutual uniqueness is independent of enumeration order");
        main=Line("mixed-main",new Vec(-2,.3,0),new Vec(2,.3,0));b=Line("mixed-port",new Vec(.3,0,-.2),new Vec(2.3,0,-.2));
        n=VirtualNet(a,main,b,Line("mixed-source-extension",new Vec(-3,0,0),new Vec(-2,0,0)));Check(VirtualEdges(n).Length==0,"Port-to-Port and Port-to-Segment candidates share one ambiguity count");
        Check(n.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0).Select(d=>d.Kind).Distinct().Count()==2,"mixed candidate diagnostics identify both virtual types");
        n=VirtualNet(Line("branch-for-two-mains",new Vec(0,-2,0),new Vec(0,-.3,0)),Line("main-one",new Vec(-2,0,.1),new Vec(2,0,.1)),Line("main-two",new Vec(-2,0,-.1),new Vec(2,0,-.1)));
        Check(VirtualEdges(n).All(e=>e.Join.A.Piece!=0&&e.Join.B.Piece!=0)&&n.VirtualConnectorCandidates.Count(d=>d.SourcePiece==0&&d.Status=="Ambiguous")==2,"two possible main centreline targets are ambiguous at the branch port");

        a=Line("bounded-source",new Vec(-2,0,0),new Vec());b=Line("over-radius",new Vec(.4,0,.31),new Vec(2.4,0,.31));
        Check(VirtualEdges(VirtualNet(a,b)).Length==0,"independent 500mm radius rejects larger XYZ distance");
        b=Line("exact-radius",new Vec(.3,0,.4),new Vec(2.3,0,.4));Check(VirtualEdges(VirtualNet(a,b)).Length==1,"500mm 3D boundary is included");
        n=new CableNetwork(new List<CablePiece>{a,b},options:new CableNetworkOptions{VirtualConnectorMaxDistance=.6,GapBridgeMaxDistance=.05});
        Near(n.PhysicalTolerance,.002,"VirtualConnector configuration preserves 2mm physical tolerance");Near(n.GapBridgeMaxDistance,.05,"VirtualConnector radius does not widen strict GapBridge distance");
        Near(n.GapBridgeWidthAxisTolerance,.002,"VirtualConnector does not widen GapBridge width tolerance");Near(n.GapBridgeHeightAxisTolerance,.002,"VirtualConnector does not widen GapBridge height tolerance");
        a=Line("strict-gap-A",new Vec(),new Vec(1,0,0));b=Line("strict-gap-B",new Vec(1.03,0,0),new Vec(2.03,0,0));
        n=new CableNetwork(new List<CablePiece>{a,b},options:new CableNetworkOptions{GapBridgeMaxDistance=.05,VirtualConnectorMaxDistance=.5});
        Check(HasGap(n)&&VirtualEdges(n).Length==0,"accepted strict GapBridge is not duplicated by VirtualConnector");
        Check(n.PhysicalComponentCount==2,"physical components exclude strict GapBridge edges");
        n=VirtualNet(a,Line("physical-socket",new Vec(1,0,0),new Vec(2,0,0)),Line("gap-beyond-occupied",new Vec(1.3,0,.2),new Vec(3.3,0,.2)));
        Check(n.VirtualConnectorCandidates.All(d=>!(d.SourcePiece==0&&d.SourcePort==1)&&!(d.TargetPiece==0&&d.TargetPort==1)),"physically occupied source ports never enter virtual candidate search");
        n=VirtualNet(a,Line("physical-duplicate-1",new Vec(1,0,0),new Vec(2,0,0)),Line("physical-duplicate-2",new Vec(1,0,0),new Vec(3,0,0)),Line("near-ambiguous-socket",new Vec(1.3,0,.2),new Vec(3.3,0,.2)));
        Check(n.VirtualConnectorCandidates.All(d=>!(d.SourcePiece==0&&d.SourcePort==1)&&!(d.TargetPiece==0&&d.TargetPort==1)),"ambiguous physical sockets cannot be bypassed by virtual candidates");
        n=VirtualNet(Junction(3),Line("tee-port-target",new Vec(0,-2.3,.2),new Vec(0,-4.3,.2),.6));
        Check(VirtualEdges(n).Single().Join.VirtualConnector.Kind==VirtualConnectorKind.PortToPort3D,"existing fitting ports may participate without assuming two ports per part");

        VirtualCostCases();
        VirtualFilterCases();
        if(artifacts!=null)
        {
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts,"virtual-connector-diagnostics.json"),JsonSerializer.Serialize(new{
                synthetic=true,physicalComponents=n.PhysicalPieceComponents,candidates=n.VirtualConnectorCandidates,graphNodes=n.GraphNodes,graphEdges=n.GraphEdges,
                result=n.Find(n.AtPort(0,0),n.AtPort(1,1))},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        }
    }

    static void VirtualFilterCases()
    {
        CableNetwork Filtered(double angle,bool parallel,params CablePiece[] pieces)=>new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{
            VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true,VirtualConnectorMaxAngle=angle,VirtualConnectorRejectParallelOffset=parallel});
        var a=Line("filter-a",new Vec(),new Vec(2,0,0),.2);
        Check(VirtualEdges(Filtered(60,true,a,Line("aligned",new Vec(2.3,0,0),new Vec(4,0,0),.2))).Length==1,"aligned gap passes direction and parallel filters");

        var side=Line("side-by-side",new Vec(2,.3,0),new Vec(4,.3,0),.2);
        Check(VirtualEdges(Filtered(0,false,a,side)).Length==1,"filters are disabled by default");
        var n=Filtered(60,false,a,side);Check(VirtualEdges(n).Length==0&&n.VirtualConnectorDirectionRejected>0,"90 degree side jump is rejected by the direction cone");
        n=Filtered(0,true,a,side);Check(VirtualEdges(n).Length==0&&n.VirtualConnectorParallelRejected>0,"side jump between parallel trays is rejected by the parallel offset rule");
        var diagnostics=n.DiagnoseConnectivity(n.AtPort(0,0),n.AtPort(1,1));
        Check(diagnostics.Candidates.Any(d=>d.TargetPiece==1),"failure diagnostics still list candidates removed by graph filters");

        var offset=Line("parallel-offset",new Vec(2.35,.2,0),new Vec(4,.2,0),.2);
        Check(VirtualEdges(Filtered(60,false,a,offset)).Length==1,"35 degree parallel offset passes the 60 degree cone alone");
        n=Filtered(60,true,a,offset);Check(VirtualEdges(n).Length==0&&n.VirtualConnectorParallelRejected>0,"parallel trays offset beyond half a width are rejected");

        var elbow=Line("missing-45-elbow",new Vec(2.15,.15,0),new Vec(2.85,.85,0),.2);
        n=Filtered(60,true,a,elbow);Check(VirtualEdges(n).Length==1,"gap of a missing 45 degree elbow remains a candidate");
        var route=n.Find(n.AtPort(0,0),n.AtPort(1,1));Check(route.VirtualConnectorCount==1&&route.RequiresReview,"filtered candidate still yields a review route");

        var stacked=Line("stacked-main",new Vec(1,-.5,.225),new Vec(3,-.5,.225),.2);var branch=Line("stacked-branch",new Vec(2,-1.5,0),new Vec(2,-.5,0),.2);
        Check(VirtualEdges(Filtered(0,false,branch,stacked)).Any(e=>e.Join.VirtualConnector.Kind==VirtualConnectorKind.PortToSegment3D),"unfiltered port jumps vertically onto a stacked tray");
        Check(!VirtualEdges(Filtered(60,true,branch,stacked)).Any(e=>e.Join.VirtualConnector.Kind==VirtualConnectorKind.PortToSegment3D),"vertical jump perpendicular to the port is rejected");
    }

    static void VirtualCostCases(bool experimental=false)
    {
        CableNetwork CostNetwork(params CablePiece[] pieces) => experimental?new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true}):VirtualNet(pieces);
        // Existing multiport fixtures only provide alternative routes. No new fitting rules.
        Func<double,bool,CableNetwork> alternatives=(centreY,equalLength)=>CostNetwork(
            ShiftVirtualHub("cost-left",0,centreY,false,0),ShiftVirtualHub("cost-right",3,centreY,true,.2),
            Line("lower-A",new Vec(),new Vec(1,0,0)),Line("lower-B",new Vec(1.4,0,.2),new Vec(3,0,.2)),
            Line("upper-A",new Vec(0,4,0),new Vec(1,4,0)),Line("upper-B",new Vec(equalLength?1.4:1.3,4,.2),new Vec(3,4,.2)));
        var n=alternatives(.5,false);Check(VirtualEdges(n).Length==2,"virtual cost fixture has two unique alternative connectors");
        var route=n.Find(n.AtPort(0,2),n.AtPort(1,2));Near(route.VirtualConnectorTotalLength,Math.Sqrt(.2),"total length wins even when the selected connector is longer");
        Check(route.Pieces.Contains(2)&&!route.Pieces.Contains(4),"shorter actual lower route wins over a longer route with a shorter virtual connector");Near(route.Length,7.6+Math.Sqrt(.2),"selected route reports its actual shorter total");
        CostReverse(n,n.AtPort(0,2),n.AtPort(1,2),route,"total route length preference");
        n=alternatives(.5,true);route=n.Find(n.AtPort(0,2),n.AtPort(1,2));
        Check(route.Pieces.Contains(2)&&!route.Pieces.Contains(4),"total route length chooses the shorter physical path when connector contributions are equal");
        Near(route.Length,7.6+Math.Sqrt(.2),"first cost term reports actual internal plus connector travel");

        n=CostNetwork(ShiftVirtualHub("count-left",0,3.5,false,0),ShiftVirtualHub("count-right",3,3.5,true,.2),
            Line("one-A",new Vec(),new Vec(1,0,0)),Line("one-B",new Vec(1.4,0,.2),new Vec(3,0,.2)),
            Line("two-A",new Vec(0,4,0),new Vec(.6,4,0)),Line("two-middle",new Vec(.7,4,.1),new Vec(2,4,.1)),Line("two-B",new Vec(2.1,4,.2),new Vec(3,4,.2)));
        Check(VirtualEdges(n).Length==3,"connector count fixture offers one connector and two-connector alternatives");
        route=n.Find(n.AtPort(0,2),n.AtPort(1,2));Check(route.VirtualConnectorCount==2&&route.Pieces.Contains(4),"shorter total route wins despite using two virtual connectors");
        Near(route.VirtualConnectorTotalLength,2*Math.Sqrt(.02),"both selected virtual connectors contribute their real distance");
        Near(route.Length,7.8+2*Math.Sqrt(.02),"total length priority adds no artificial distance penalty");
        Near(route.Steps.Sum(s=>s.Length),route.Length,"lexicographic route still sums real physical lengths");
        CostReverse(n,n.AtPort(0,2),n.AtPort(1,2),route,"shorter two-connector route");
    }
}
