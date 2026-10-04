using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static double CostVertical(Vec[] line)
    {
        return line.Zip(line.Skip(1),(a,b)=>Math.Abs(b.Z-a.Z)).Sum();
    }

    static void CostReverse(CableNetwork network,CableLocation start,CableLocation finish,CableRoute forward,string name)
    {
        var reverse=network.Find(finish,start);
        Check(reverse.TotalLength==forward.TotalLength,name+" forward/reverse TotalLength is exactly equal ("+forward.TotalLength.ToString("R")+" / "+reverse.TotalLength.ToString("R")+")");
        Check(reverse.VerticalTravel==forward.VerticalTravel,name+" forward/reverse VerticalTravel is exactly equal ("+forward.VerticalTravel.ToString("R")+" / "+reverse.VerticalTravel.ToString("R")+")");
        Check(reverse.VirtualConnectorCount==forward.VirtualConnectorCount,name+" forward/reverse VirtualConnectorCount");
        Check(reverse.VirtualConnectorTotalLength==forward.VirtualConnectorTotalLength,name+" forward/reverse virtual distance is exactly equal ("+forward.VirtualConnectorTotalLength.ToString("R")+" / "+reverse.VirtualConnectorTotalLength.ToString("R")+")");
        Check(reverse.GapBridgeCount==forward.GapBridgeCount,name+" forward/reverse GapBridgeCount");
        Near(forward.Steps.Sum(s=>s.VerticalTravel),forward.VerticalTravel,name+" step vertical contributions sum to route cost");
        Near(reverse.Steps.Sum(s=>s.VerticalTravel),reverse.VerticalTravel,name+" reverse step vertical contributions sum to route cost");
    }

    static CableNetwork CostNet(bool experimental,params CablePiece[] pieces)
    {
        return new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{
            VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=experimental});
    }

    // Fixed alternative centreline geometry exercises the existing Port/Junction graph.
    // This test fixture does not define new fitting reconstruction or connectivity rules.
    static CablePiece CostAlternatives(string name,Vec start,Vec finish,params Vec[][] routes)
    {
        var p0=start-new Vec(1,0,0);var p1=finish+new Vec(1,0,0);
        var edges=new List<InternalEdge>{
            new InternalEdge{Id="Start",From="P0",To="J0",Centerline=new[]{p0,start}},
            new InternalEdge{Id="Finish",From="P1",To="J1",Centerline=new[]{p1,finish}}};
        edges.AddRange(routes.Select((line,i)=>new InternalEdge{Id="Route"+i,From="J0",To="J1",Centerline=line}));
        return new CablePiece{Shape=new Part{Id=name,Name=name,Kind="Tee",System="CostFixture",
            Ports=new[]{TestPort("P0",p0,new Vec(-1,0,0)),TestPort("P1",p1,new Vec(1,0,0))},
            Junctions=new[]{new PartJunction{Id="J0",Point=start},new PartJunction{Id="J1",Point=finish}},InternalEdges=edges.ToArray()}};
    }

    static void AddHubExcursion(CablePiece hub,int arm,double extra)
    {
        var line=hub.Shape.InternalEdges[arm].Centerline;
        hub.Shape.InternalEdges[arm].Centerline=new[]{line[0],line[0]+new Vec(0,-extra/2,0),line[0]}.Concat(line.Skip(1)).ToArray();
    }

    static void PathCostCases()
    {
        foreach(bool experimental in new[]{false,true})
        {
            string mode=experimental?"Top-N":"unique candidate";
            var left=GapTestHub("twelve-eighty-left-"+mode,0,2,false);
            var right=GapTestHub("twelve-eighty-right-"+mode,3,2,true);
            AddHubExcursion(left,0,1);AddHubExcursion(left,1,69);
            var n=CostNet(experimental,left,right,
                Line("twelve-left",new Vec(),new Vec(1,0,0)),Line("twelve-right",new Vec(1.3,0,0),new Vec(3,0,0)),
                Line("eighty-left",new Vec(0,4,0),new Vec(1,4,0)),Line("eighty-right",new Vec(1.05,4,0),new Vec(3,4,0)));
            var virtualEdges=VirtualEdges(n);Check(virtualEdges.Length==2,mode+" Case 1 has both actual connector choices");
            var lower=virtualEdges.Single(e=>e.Join.A.Piece==2||e.Join.B.Piece==2);
            var upper=virtualEdges.Single(e=>e.Join.A.Piece==4||e.Join.B.Piece==4);
            Near(lower.Length,.30,mode+" Case 1 near route virtual distance");Near(upper.Length,.05,mode+" Case 1 far route virtual distance");
            Near(left.Shape.InternalEdges[2].Length+left.Shape.InternalEdges[0].Length+right.Shape.InternalEdges[0].Length+right.Shape.InternalEdges[2].Length+
                n.Pieces[2].Shape.Length+n.Pieces[3].Shape.Length+lower.Length,12,mode+" Case 1 near alternative geometry is 12m");
            Near(left.Shape.InternalEdges[2].Length+left.Shape.InternalEdges[1].Length+right.Shape.InternalEdges[1].Length+right.Shape.InternalEdges[2].Length+
                n.Pieces[4].Shape.Length+n.Pieces[5].Shape.Length+upper.Length,80,mode+" Case 1 far alternative geometry is 80m");
            var start=n.AtPort(0,2);var finish=n.AtPort(1,2);var route=n.Find(start,finish);
            Near(route.Length,12,mode+" Case 1 chooses 12m rather than 80m");Near(route.VirtualConnectorTotalLength,.30,mode+" Case 1 accepts the longer virtual edge on the shorter route");
            Check(route.VirtualConnectorCount==1&&route.Pieces.Contains(2)&&!route.Pieces.Contains(4),mode+" Case 1 uses the near branch and keeps one review connector");
            CostReverse(n,start,finish,route,mode+" Case 1");

            n=CostNet(experimental,GapTestHub("equal-count-left-"+mode,0,2,false),GapTestHub("equal-count-right-"+mode,3,2,true),
                Line("one-half-A",new Vec(),new Vec(1,0,0)),Line("one-half-B",new Vec(1.5,0,0),new Vec(3,0,0)),
                Line("two-eighth-A",new Vec(0,4,0),new Vec(.5,4,0)),Line("two-eighth-middle",new Vec(.625,4,0),new Vec(2,4,0)),
                Line("two-eighth-B",new Vec(2.125,4,0),new Vec(3,4,0)));
            Check(VirtualEdges(n).Length==3,mode+" count tie-break has one-versus-two virtual alternatives");
            start=n.AtPort(0,2);finish=n.AtPort(1,2);route=n.Find(start,finish);
            Near(route.Length,11,mode+" virtual-count alternatives have equal total length");Near(route.VerticalTravel,0,mode+" virtual-count alternatives are horizontal");
            Check(route.VirtualConnectorCount==1&&route.Pieces.Contains(2),mode+" third term prefers one virtual connector after length and vertical ties");
            Near(route.VirtualConnectorTotalLength,.5,mode+" count precedes the competing smaller .25m virtual sum");CostReverse(n,start,finish,route,mode+" virtual-count tie-break");

            n=CostNet(experimental,GapTestHub("equal-virtual-left-"+mode,0,2,false),GapTestHub("equal-virtual-right-"+mode,3,2,true),
                Line("quarter-A",new Vec(),new Vec(1,0,0)),Line("quarter-B",new Vec(1.25,0,0),new Vec(3,0,0)),
                Line("half-A",new Vec(0,4,0),new Vec(1,4,0)),Line("half-B",new Vec(1.5,4,0),new Vec(3,4,0)));
            Check(VirtualEdges(n).Length==2,mode+" virtual-distance tie-break keeps both alternatives");
            start=n.AtPort(0,2);finish=n.AtPort(1,2);route=n.Find(start,finish);
            Near(route.Length,11,mode+" virtual-distance alternatives have equal total length");Check(route.VirtualConnectorCount==1,mode+" virtual-distance alternatives have equal connector count");
            Near(route.VirtualConnectorTotalLength,.25,mode+" fourth term prefers the shorter virtual contribution");CostReverse(n,start,finish,route,mode+" virtual-distance tie-break");
        }

        VerticalCostCases();ConnectionVerticalCases();GapCostTieCases();PathCostNumericCases();
    }

    static void VerticalCostCases()
    {
        var start=new Vec();var finish=new Vec(4,0,0);
        var horizontal=new[]{start,new Vec(0,2,0),new Vec(4,2,0),finish};
        var upDown=new[]{start,new Vec(0,0,2),new Vec(4,0,2),finish};
        // Vertical route appears first: the decision must come from cost, not edge order.
        var piece=CostAlternatives("exact-equal-vertical",start,finish,upDown,horizontal);var n=Net(piece);
        Check(piece.Shape.InternalEdges[2].Length==piece.Shape.InternalEdges[3].Length,"Case 2 alternatives are exactly equal length without an epsilon tie");
        var a=n.AtPort(0,0);var b=n.AtPort(0,1);var route=n.Find(a,b);
        Near(route.Length,10,"Case 2 total travel includes the two port stems");Near(route.VerticalTravel,0,"Case 2 horizontal route wins over same-height up/down travel");
        Check(route.Steps.Any(s=>s.EdgeId=="Route1")&&!route.Steps.Any(s=>s.EdgeId=="Route0"),"Case 2 selects the actual horizontal centreline");
        var verticalEdge=n.GraphEdges.Single(e=>e.EdgeId=="Route0");Near(verticalEdge.VerticalTravel,4,"complete edge polyline counts ascent and descent despite zero endpoint Z difference");
        CostReverse(n,a,b,route,"Case 2");

        piece=CostAlternatives("length-before-vertical",start,finish,new[]{start,new Vec(0,0,1),new Vec(4,0,1),finish},horizontal);n=Net(piece);
        a=n.AtPort(0,0);b=n.AtPort(0,1);route=n.Find(a,b);
        Near(route.Length,8,"TotalLength precedes vertical travel when routes are unequal");Near(route.VerticalTravel,2,"shorter route may legitimately rise and fall");CostReverse(n,a,b,route,"length before vertical");

        finish=new Vec(6,0,4);
        var monotonic=new[]{start,new Vec(0,3,0),new Vec(3,3,4),new Vec(6,3,4),finish};
        var repeated=new[]{start,new Vec(0,0,6),new Vec(6,0,6),finish};
        piece=CostAlternatives("sloping-height-change",start,finish,repeated,monotonic);n=Net(piece);
        Check(piece.Shape.InternalEdges[2].Length==piece.Shape.InternalEdges[3].Length,"Case 3 equal-length fixture uses exact 3-4-5 slope and integer legs");
        a=n.AtPort(0,0);b=n.AtPort(0,1);route=n.Find(a,b);
        Near(route.Length,16,"Case 3 equal total length includes real monotonic slope");Near(route.VerticalTravel,4,"Case 3 monotonic height change wins over an 8m vertical detour");
        Check(route.Steps.Any(s=>s.EdgeId=="Route1"),"Case 3 uses the monotonic 3D slope route");CostReverse(n,a,b,route,"Case 3");

        var direct=Line("direct-monotonic-slope",new Vec(),new Vec(3,0,4));n=Net(direct);a=n.AtPort(0,0);b=n.AtPort(0,1);route=n.Find(a,b);
        Near(route.Length,5,"normal direct 3D slope remains available");Near(route.VerticalTravel,4,"normal slope accumulates actual endpoint height travel");CostReverse(n,a,b,route,"direct monotonic slope");

        var polyline=Line("up-down-station-slice",new Vec(),new Vec(4,0,0));polyline.Shape.Kind="Tee";
        polyline.Shape.InternalEdges[0].Centerline=new[]{new Vec(),new Vec(1,0,2),new Vec(2,0,0),new Vec(3,0,-1),new Vec(4,0,0)};
        n=Net(polyline);Near(n.GraphEdges.Single().VerticalTravel,6,"full polyline includes both intermediate high and low points");
        a=n.At(0,Math.Sqrt(5)/2);b=n.At(0,2*Math.Sqrt(5)+Math.Sqrt(2)/2);route=n.Find(a,b);
        Near(route.VerticalTravel,3.5,"query-specific station slices accumulate only traversed vertical travel");
        Near(route.Steps.Single().VerticalTravel,3.5,"sliced step reports the same complete polyline vertical travel");
        Near(n.GraphEdges.Single().VerticalTravel,6,"query slicing does not mutate base edge vertical cost");CostReverse(n,a,b,route,"station-sliced vertical polyline");
    }

    static void ConnectionVerticalCases()
    {
        var axis=new Vec(.6,0,.8);var n=Net(Line("physical-slope-A",new Vec(),axis),Line("physical-slope-B",axis*1.001,axis*2.001));
        var edge=n.GraphEdges.Single(e=>e.Kind==CableEdgeKind.ConnectionEdge);Near(edge.VerticalTravel,.0008,"1mm physical connection counts its real 3D vertical change");
        var start=n.AtPort(0,0);var finish=n.AtPort(1,1);var route=n.Find(start,finish);Near(route.VerticalTravel,2.001*.8,"physical connection and both slope internals accumulate vertical travel");CostReverse(n,start,finish,route,"physical slope connection");

        n=GapNet(Line("gap-slope-A",new Vec(),axis),Line("gap-slope-B",axis*1.03125,axis*2.03125));
        edge=n.GraphEdges.Single(e=>e.Kind==CableEdgeKind.GapBridgeEdge);Near(edge.VerticalTravel,.025,"strict slope GapBridge includes its full vertical component");
        start=n.AtPort(0,0);finish=n.AtPort(1,1);route=n.Find(start,finish);Near(route.VerticalTravel,2.03125*.8,"GapBridge vertical travel contributes exactly once");CostReverse(n,start,finish,route,"strict slope gap bridge");

        n=CostNet(true,Line("virtual-slope-A",new Vec(-2,0,-1),new Vec()),Line("virtual-slope-B",new Vec(.3,.1,.2),new Vec(2.3,.1,1.2)));
        edge=VirtualEdges(n).Single();Near(edge.VerticalTravel,.2,"3D VirtualConnector includes its world Z change");
        start=n.AtPort(0,0);finish=n.AtPort(1,1);route=n.Find(start,finish);Near(route.VerticalTravel,2.2,"virtual connector and slope internals accumulate real vertical movement");CostReverse(n,start,finish,route,"virtual slope bridge");
        foreach(var e in n.GraphEdges)Near(e.VerticalTravel,CostVertical(e.Centerline),"graph edge vertical cost agrees with every segment in its centreline");
    }

    static void GapCostTieCases()
    {
        var n=new CableNetwork(new List<CablePiece>{GapTestHub("gap-tie-left",0,2,false),GapTestHub("gap-tie-right",3,2,true),
            Line("one-gap-A",new Vec(),new Vec(1,0,0)),Line("one-gap-B",new Vec(1.03125,0,0),new Vec(3,0,0)),
            Line("two-gap-A",new Vec(0,4,0),new Vec(.5,4,0)),Line("two-gap-middle",new Vec(.53125,4,0),new Vec(2,4,0)),
            Line("two-gap-B",new Vec(2.03125,4,0),new Vec(3,4,0))},options:new CableNetworkOptions{GapBridgeMaxDistance=.05,VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true});
        Check(n.GraphEdges.Count(e=>e.Kind==CableEdgeKind.GapBridgeEdge)==3,"fifth-term fixture preserves all strict GapBridge choices");
        var start=n.AtPort(0,2);var finish=n.AtPort(1,2);var route=n.Find(start,finish);
        Near(route.Length,11,"gap-count alternatives tie in TotalLength");Near(route.VerticalTravel,0,"gap-count alternatives tie in VerticalTravel");
        Check(route.VirtualConnectorCount==0&&route.VirtualConnectorTotalLength==0,"gap-count alternatives tie in both virtual costs");
        Check(route.GapBridgeCount==1&&route.Pieces.Contains(2),"fifth term chooses fewer gap bridges after all preceding terms tie");CostReverse(n,start,finish,route,"gap-count fifth tie-break");

        n=new CableNetwork(new List<CablePiece>{GapTestHub("mixed-tie-left",0,2,false),GapTestHub("mixed-tie-right",3,2,true),
            Line("small-virtual-gap-A",new Vec(),new Vec(.5,0,0)),Line("small-virtual-gap-middle",new Vec(.53125,0,0),new Vec(1,0,0)),
            Line("small-virtual-gap-B",new Vec(1.25,0,0),new Vec(3,0,0)),
            Line("large-virtual-no-gap-A",new Vec(0,4,0),new Vec(1,4,0)),Line("large-virtual-no-gap-B",new Vec(1.5,4,0),new Vec(3,4,0))},
            options:new CableNetworkOptions{GapBridgeMaxDistance=.05,VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true});
        Check(VirtualEdges(n).Length==2&&n.GraphEdges.Count(e=>e.Kind==CableEdgeKind.GapBridgeEdge)==1,"fourth-before-fifth fixture contains both virtual alternatives and one strict gap");
        start=n.AtPort(0,2);finish=n.AtPort(1,2);route=n.Find(start,finish);
        Near(route.Length,11,"mixed virtual/gap alternatives have equal actual total length");Near(route.VirtualConnectorTotalLength,.25,"fourth term selects smaller virtual distance before gap count");
        Check(route.VirtualConnectorCount==1&&route.GapBridgeCount==1&&route.Pieces.Contains(2),"one additional reviewed gap cannot override the preceding virtual-distance tie-break");CostReverse(n,start,finish,route,"virtual-distance before gap count");
    }

    static void PathCostNumericCases()
    {
        var values=new[]{0d,double.Epsilon,BitConverter.Int64BitsToDouble(0x000fffffffffffff),BitConverter.Int64BitsToDouble(0x0010000000000000),
            .1,1d,1.3,Math.PI,1e100,double.MaxValue};
        foreach(double value in values)Check(BitConverter.DoubleToInt64Bits(CableDistance.FromMetres(value).Metres)==BitConverter.DoubleToInt64Bits(value),
            "exact distance round-trips " +value.ToString("R"));
        Check((CableDistance.FromMetres(double.Epsilon)+CableDistance.FromMetres(double.Epsilon)).Metres==2*double.Epsilon,"exact distance adds subnormal physical travel");
        double halfUlp=Math.Pow(2,-53);
        Check((CableDistance.FromMetres(1)+CableDistance.FromMetres(halfUlp)).Metres==1,"distance output rounds a halfway result to the even double");
        Check((CableDistance.FromMetres(1)+CableDistance.FromMetres(halfUlp)+CableDistance.FromMetres(halfUlp)).Metres==BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1)+1),
            "small accumulated travel is retained before final output rounding");

        Func<double,CableEdgeKind,CableGraphEdge> make=(length,kind)=>new CableGraphEdge{Kind=kind,Length=length,Centerline=new[]{new Vec(),new Vec(length,0,0)}};
        var near=new[]{make(.1,CableEdgeKind.VirtualConnectorEdge),make(.1,CableEdgeKind.InternalEdge),make(.1,CableEdgeKind.InternalEdge),make(1,CableEdgeKind.InternalEdge)};
        var far=new[]{make(1,CableEdgeKind.InternalEdge),make(.1,CableEdgeKind.VirtualConnectorEdge),make(.1,CableEdgeKind.InternalEdge),make(.1,CableEdgeKind.VirtualConnectorEdge)};
        Func<IEnumerable<CableGraphEdge>,CableNetwork.PathCost> cost=edges=>{var result=new CableNetwork.PathCost();foreach(var edge in edges)result=result.Add(edge);return result;};
        double naiveNear=0,naiveFar=0;foreach(var edge in near)naiveNear+=edge.Length;foreach(var edge in far)naiveFar+=edge.Length;
        Check(naiveNear!=naiveFar,"floating-order regression really differs under naive double addition");
        var nearForward=cost(near);var nearReverse=cost(near.Reverse());var farForward=cost(far);var farReverse=cost(far.Reverse());
        Check(nearForward.TotalLength.CompareTo(farForward.TotalLength)==0,"equal edge-weight multisets have exactly equal total cost");
        Check(nearForward.Compare(nearReverse)==0&&farForward.Compare(farReverse)==0,"complete five-part cost is independent of traversal accumulation order");
        Check(nearForward.Compare(farForward)<0&&nearReverse.Compare(farReverse)<0,"one virtual connector wins the true length tie in both directions");
        Check(nearForward.TotalLength.Metres==nearReverse.TotalLength.Metres&&farForward.TotalLength.Metres==farReverse.TotalLength.Metres,"floating-order regression outputs bit-identical forward/reverse totals");
        var infinitesimal=cost(new[]{make(1,CableEdgeKind.InternalEdge),make(double.Epsilon,CableEdgeKind.InternalEdge)});
        var shorter=cost(new[]{make(1,CableEdgeKind.InternalEdge)});
        Check(shorter.Compare(infinitesimal)<0,"a genuinely longer route is not treated as a tie by epsilon or rounded output");
        Check(shorter.TotalLength.Metres==infinitesimal.TotalLength.Metres,"cost distinguishes physically positive travel even when displayed doubles round equally");
    }
}
