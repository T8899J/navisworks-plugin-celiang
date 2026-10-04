using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static CableNetwork GapNet(params CablePiece[] pieces)
    {
        return new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{GapBridgeMaxDistance=.05});
    }
    static bool HasGap(CableNetwork network) { return network.GraphEdges.Any(e=>e.Kind==CableEdgeKind.GapBridgeEdge); }
    static Port TestPort(string id,Vec point,Vec outward)
    {
        var width=Unit(new Vec(0,0,1).Cross(outward));
        return new Port{Id=id,Point=point,Outward=outward,Width=.4,Height=.1,WidthAxis=width,HeightAxis=outward.Cross(width)};
    }
    // Existing multiport graph fixtures provide alternative routes only. This stage adds
    // no fitting geometry, side-entry gap inference or main-route preference rules.
    static CablePiece GapTestHub(string name,double x,double centreY,bool right)
    {
        double inward=right?1:-1;var centre=new Vec(x+inward,centreY,0);
        var points=new[]{new Vec(x,0,0),new Vec(x,4,0),centre+new Vec(inward,0,0)};
        var ports=points.Select((p,i)=>TestPort("P"+i,p,new Vec(i==2?inward:-inward,0,0))).ToArray();
        return new CablePiece{Shape=new Part{Id=name,Name=name,System="Run-"+name,Kind="Tee",Ports=ports,
            Junctions=new[]{new PartJunction{Id="J",Point=centre}},InternalEdges=points.Select((p,i)=>new InternalEdge{
                Id="Arm"+i,From="P"+i,To="J",Centerline=i==2?new[]{p,centre}:new[]{p,new Vec(centre.X,p.Y,0),centre}}).ToArray()}};
    }
    static CablePiece ConfirmedDetour()
    {
        var junction=new Vec(1.015,-5,0);var points=new[]{new Vec(),new Vec(2.03,0,0),new Vec(1.015,-6,0)};
        var outward=new[]{new Vec(1,0,0),new Vec(-1,0,0),new Vec(0,-1,0)};
        var paths=new[]{new[]{points[0],new Vec(-.5,0,0),new Vec(-.5,-5,0),junction},
            new[]{points[1],new Vec(2.53,0,0),new Vec(2.53,-5,0),junction},new[]{points[2],junction}};
        return new CablePiece{Shape=new Part{Id="confirmed-detour",Name="confirmed-detour",Kind="Tee",
            Ports=points.Select((p,i)=>TestPort("P"+i,p,outward[i])).ToArray(),Junctions=new[]{new PartJunction{Id="J",Point=junction}},
            InternalEdges=paths.Select((line,i)=>new InternalEdge{Id="Arm"+i,From="P"+i,To="J",Centerline=line}).ToArray()}};
    }
    static void GapBridgeCases()
    {
        var a=Line("gap-A",new Vec(),new Vec(1,0,0));var b=Line("gap-B",new Vec(1.03,0,0),new Vec(2.03,0,0));
        Check(!HasGap(Net(a,b)),"gap bridging is opt-in for existing network callers");
        var n=GapNet(a,b);Check(n.Joins.Count==1&&HasGap(n),"straight 30mm gap accepted");
        Near(n.PhysicalTolerance,.002,"gap configuration retains 2mm PhysicalTolerance");
        var edge=n.GraphEdges.Single(e=>e.Kind==CableEdgeKind.GapBridgeEdge);Near(edge.Length,.03,"gap edge length is world-coordinate port distance");
        Check(edge.RequiresReview&&edge.Join.IsGapBridge,"gap bridge is always an explicit review edge");
        Check(edge.ReviewReason.Contains("gap=30.000 mm")&&edge.ReviewReason.Contains("横向偏差=0.000 mm")&&edge.ReviewReason.Contains("竖向偏差=0.000 mm"),"gap review reason includes gap lateral and vertical deviations");
        var route=n.Find(n.AtPort(0,0),n.AtPort(1,1));Near(route.Length,2.03,"gap physical length contributes to the route exactly once");
        Check(route.GapBridgeCount==1&&route.RequiresReview&&route.ReviewConnections.Count==1,"route reports gap count and review connection");
        Near(route.GapBridgeLength,.03,"route reports actual total gap length");
        Near(route.Steps.Sum(s=>s.Length),route.Length,"gap route total remains the sum of physical edge lengths");
        Near(n.Find(n.AtPort(1,1),n.AtPort(0,0)).Length,route.Length,"gap route is reversible");
        Reject(()=>n.Find(n.AtPort(0,0),n.AtPort(1,1),false),"strict routing excludes gap review edges");

        var perturbed=Line("port-vs-station",new Vec(),new Vec(1,0,0));perturbed.Shape.Ports[1].Point+=new Vec(8e-8,0,0);
        n=GapNet(perturbed,b);edge=n.GraphEdges.Single(e=>e.Kind==CableEdgeKind.GapBridgeEdge);
        Near(edge.Length,(perturbed.Shape.Ports[1].Point-b.Shape.Ports[0].Point).Norm,"gap length uses actual Port coordinates rather than internal station approximations",1e-12);
        Near((edge.Centerline[1]-edge.Centerline[0]).Norm,edge.Length,"gap edge centreline agrees with actual Port distance",1e-12);

        var direction=Unit(new Vec(1,2,3));var origin=new Vec(10000,-90000,3000);
        a=Line("slope-gap-A",origin,origin+direction);b=Line("slope-gap-B",origin+direction*1.03,origin+direction*2.03);
        a.Shape.Kind=b.Shape.Kind="Slope";n=GapNet(a,b);
        Check(HasGap(n),"arbitrary 3D slope gap accepted");Near(n.Find(n.AtPort(0,0),n.AtPort(1,1)).Length,2.03,"3D slope gap uses real world distance rather than a horizontal projection");
        n=GapNet(Line("vertical-A",new Vec(),new Vec(0,0,1)),Line("vertical-B",new Vec(0,0,1.03),new Vec(0,0,2.03)));
        Check(HasGap(n),"vertical straight port-to-port gap accepted");

        a=Line("offset-reference",new Vec(),new Vec(1,0,0));
        Check(!HasGap(GapNet(a,Line("lateral-offset",new Vec(1.049,.0021,0),new Vec(2.049,.0021,0)))),"lateral offset rejected even inside the longitudinal angle and maximum distance");
        Check(!HasGap(GapNet(a,Line("vertical-parallel",new Vec(1.049,0,.0021),new Vec(2.049,0,.0021)))),"vertical parallel tray rejected even inside the longitudinal angle and maximum distance");
        b=Line("wrong-facing",new Vec(1.03,0,0),new Vec(2.03,0,0));b.Shape.Ports[0].Outward=new Vec(1,0,0);
        Check(!HasGap(GapNet(a,b)),"wrong-facing port rejected");
        Check(!HasGap(GapNet(a,Line("over-max",new Vec(1.050001,0,0),new Vec(2.050001,0,0)))),"over max gap rejected");
        Check(HasGap(GapNet(a,Line("at-max",new Vec(1.05,0,0),new Vec(2.05,0,0)))),"gap exactly at configured maximum accepted");
        Check(!HasGap(GapNet(a,Line("different-width",new Vec(1.03,0,0),new Vec(2.03,0,0),.6))),"incompatible width rejected for first-stage gap bridges");
        b=Line("different-height",new Vec(1.03,0,0),new Vec(2.03,0,0));foreach(var port in b.Shape.Ports)port.Height=.12;
        Check(!HasGap(GapNet(a,b)),"incompatible height rejected for first-stage gap bridges");
        b=Line("rolled-gap",new Vec(1.03,0,0),new Vec(2.03,0,0));foreach(var port in b.Shape.Ports){port.WidthAxis=new Vec(0,0,1);port.HeightAxis=new Vec(0,-1,0);}
        Check(!HasGap(GapNet(a,b)),"incompatible cross-section roll cannot become a gap bridge");
        b=Line("small-offset",new Vec(1.03,.001,.0005),new Vec(2.03,.001,.0005));n=GapNet(a,b);
        Check(HasGap(n),"small lateral and vertical deviations inside both tolerances accepted");
        Near(n.Joins.Single().LateralOffset,.001,"reported lateral deviation uses WidthAxis");Near(n.Joins.Single().VerticalOffset,.0005,"reported vertical deviation uses HeightAxis");
        Near(n.GraphEdges.Single(e=>e.Kind==CableEdgeKind.GapBridgeEdge).Length,Math.Sqrt(.03*.03+.001*.001+.0005*.0005),"offset bridge stores Euclidean physical length");
        var options=new CableNetworkOptions{GapBridgeMaxDistance=.05,GapBridgeWidthAxisTolerance=.0001};
        Check(!HasGap(new CableNetwork(new List<CablePiece>{a,b},options:options)),"WidthAxis allowance can be configured independently");
        options=new CableNetworkOptions{GapBridgeMaxDistance=.05,GapBridgeHeightAxisTolerance=.0001};
        Check(!HasGap(new CableNetwork(new List<CablePiece>{a,b},options:options)),"HeightAxis allowance can be configured independently");
        b=Line("non-longitudinal",new Vec(1.03,.02,0),new Vec(2.03,.02,0));
        options=new CableNetworkOptions{GapBridgeMaxDistance=.05,GapBridgeWidthAxisTolerance=.1};
        Check(!HasGap(new CableNetwork(new List<CablePiece>{a,b},options:options)),"non-longitudinal displacement rejected even when transverse tolerance is wide");

        b=Line("candidate-near",new Vec(1.03,0,0),new Vec(2.03,0,0));var c=Line("candidate-far",new Vec(1.04,0,0),new Vec(3.04,0,0));n=GapNet(a,b,c);
        Check(!HasGap(n)&&n.Ambiguities.Any(s=>s.Contains("断截端口")),"multiple candidates ambiguous/rejected rather than choosing the nearest");
        Check(!HasGap(GapNet(c,b,a)),"mutual candidate uniqueness is independent of piece enumeration order");
        var physical=Line("physically-connected",new Vec(1,0,0),new Vec(2,0,0));n=GapNet(a,physical,b);
        Check(!HasGap(n)&&n.Joins.Count==1,"a physically connected port is never offered a gap candidate");
        n=GapNet(a,physical,Line("duplicate-physical",new Vec(1,0,0),new Vec(3,0,0)),b);
        Check(!HasGap(n)&&n.Ambiguities.Count>0,"ambiguous physical sockets cannot be bypassed with a gap bridge");
        n=GapNet(a,Line("physical-1mm",new Vec(1.001,0,0),new Vec(2.001,0,0)));
        Check(!HasGap(n)&&n.GraphEdges.Count(e=>e.Kind==CableEdgeKind.ConnectionEdge)==1,"a 1mm physical connection keeps its original edge kind");
        Check(!HasGap(GapNet(Line("overlap-A",new Vec(),new Vec(1,0,0)),Line("overlap-B",new Vec(.97,0,0),new Vec(2,0,0)))),"inward socket overlap is not reclassified as an empty gap");

        var elbow=FittingGeometry.Build(PortGraphFixtures.Bend(),"Elbow90");elbow.Id="existing-elbow";
        n=GapNet(Line("before-missing-elbow",new Vec(.97,0,0),new Vec(1.97,0,0)),new CablePiece{Shape=elbow});
        Check(!HasGap(n),"elbow ports are excluded from first-stage gap bridging");
        n=GapNet(Line("before-tee",new Vec(-2.03,0,0),new Vec(-1.03,0,0)),Junction(3));
        Check(!HasGap(n),"tee ports are excluded from first-stage gap bridging");
        n=GapNet(Line("main-no-gap-extension",new Vec(),new Vec(10,0,0)),Line("branch-30mm-side-gap",new Vec(3,-2,0),new Vec(3,-.23,0),.1));
        Check(n.Joins.Count==0,"branch-to-middle 30mm gap remains disconnected");

        n=GapNet(ConfirmedDetour(),Line("short-cut-A",new Vec(),new Vec(1,0,0)),Line("short-cut-B",new Vec(1.03,0,0),new Vec(2.03,0,0)));
        Check(HasGap(n),"routing fixture contains a shorter gap shortcut and a confirmed detour");
        route=n.Find(n.AtPort(0,0),n.AtPort(0,1));Near(route.Length,2.03,"shorter gap route wins over a much longer confirmed detour");
        Check(route.GapBridgeCount==1&&route.RequiresReview,"selected shorter gap route retains its review status");CostReverse(n,n.AtPort(0,0),n.AtPort(0,1),route,"shorter gap shortcut");

        var lowA=Line("lower-A",new Vec(),new Vec(1,0,0));var lowB=Line("lower-B",new Vec(1.03,0,0),new Vec(2.03,0,0));
        n=GapNet(GapTestHub("left-equal-count",0,.5,false),GapTestHub("right-equal-count",2.03,.5,true),lowA,lowB,
            Line("upper-A",new Vec(0,4,0),new Vec(.8,4,0)),Line("upper-B",new Vec(.84,4,0),new Vec(2.03,4,0)));
        Check(n.GraphEdges.Count(e=>e.Kind==CableEdgeKind.GapBridgeEdge)==2,"equal-gap-count fixture has two alternative bridges");
        route=n.Find(n.AtPort(0,2),n.AtPort(1,2));Near(route.Length,7.03,"equal gap counts choose shorter physical length");
        Check(route.GapBridgeCount==1&&route.Pieces.Contains(2)&&route.Pieces.Contains(3),"equal gap count choice uses the shorter lower route");

        n=GapNet(GapTestHub("left-fewer-gaps",0,3.5,false),GapTestHub("right-fewer-gaps",2.03,3.5,true),lowA,lowB,
            Line("upper-two-gaps-A",new Vec(0,4,0),new Vec(.6,4,0)),Line("upper-two-gaps-middle",new Vec(.63,4,0),new Vec(1.2,4,0)),
            Line("upper-two-gaps-B",new Vec(1.23,4,0),new Vec(2.03,4,0)));
        Check(n.GraphEdges.Count(e=>e.Kind==CableEdgeKind.GapBridgeEdge)==3,"fewer-gap fixture contains a one-gap route and a shorter two-gap route");
        route=n.Find(n.AtPort(0,2),n.AtPort(1,2));Near(route.Length,7.03,"shorter two-gap route wins before comparing gap count");
        Check(route.GapBridgeCount==2,"total length priority reports both selected gap bridges");
        Near(route.Steps.Sum(s=>s.Length),route.Length,"lexicographic preference never adds a fictitious distance penalty");
        CostReverse(n,n.AtPort(0,2),n.AtPort(1,2),route,"shorter two-gap route");
    }
}
