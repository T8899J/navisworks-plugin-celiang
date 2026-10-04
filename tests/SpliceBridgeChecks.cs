using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static CableJointHint Splice(string id,Vec min,Vec max) { return new CableJointHint{Id=id,Name=id,Kind="SpliceConnector",Min=min,Max=max}; }

    static void SpliceBridgeCases()
    {
        // Horizontal adjustable splice at a 45 degree turn with a 0.1414 m gap between tray ends.
        var a=Line("splice-a",new Vec(),new Vec(2,0,0),.2);var b=Line("splice-b",new Vec(2.1,.1,0),new Vec(2.8,.8,0),.2);
        var hint=Splice("H-1",new Vec(1.95,-.1,-.075),new Vec(2.2,.2,.075));
        bool threw=false;try{new CableNetwork(new List<CablePiece>{a,b}).Find(new CableLocation{Piece=0,Edge=0,Station=0},new CableLocation{Piece=1,Edge=0,Station=b.Shape.InternalEdges[0].Length});}catch(CablePathNotFoundException){threw=true;}
        Check(threw,"without a splice hint the two tray ends stay disconnected");

        var n=new CableNetwork(new List<CablePiece>{a,b},jointHints:new[]{hint});
        Check(n.SpliceBridgeCount==1&&n.PhysicalComponentCount==1,"splice hint joins both trays into one physical component");
        var edge=n.GraphEdges.Single(e=>e.EdgeId.StartsWith("$splice-bridge:"));
        Check(edge.Kind==CableEdgeKind.ConnectionEdge&&!edge.RequiresReview&&edge.Join.SpliceConnectorId=="H-1","splice bridge is a confirmed connection edge that names its connector");
        var start=n.AtPort(0,0);var finish=n.AtPort(1,1);var route=n.Find(start,finish,false);
        Near(route.Length,2+Math.Sqrt(.02)+Math.Sqrt(.98),"splice bridge counts its real 3D gap in the total length");
        Check(route.VirtualConnectorCount==0&&route.SpliceBridgeCount==1&&!route.RequiresReview,"strict route crosses the splice without virtual connectors or review");
        Near(route.SpliceBridgeLength,Math.Sqrt(.02),"route reports the splice bridge length");
        var reverse=n.Find(finish,start,false);Check(reverse.TotalLength==route.TotalLength&&reverse.VerticalTravel==route.VerticalTravel,"splice route forward/reverse costs are exactly equal");

        n=new CableNetwork(new List<CablePiece>{a,b},options:new CableNetworkOptions{VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=true},jointHints:new[]{hint});
        Check(VirtualEdges(n).Length==0,"bridged splice ports do not also receive a virtual connector");
        Check(n.Find(start,finish).VirtualConnectorCount==0,"route prefers the confirmed splice bridge");

        // Vertical adjustable splice: horizontal tray turning up into a riser.
        var riser=Line("splice-riser",new Vec(2.1,0,.1),new Vec(2.1,0,2),.2);
        n=new CableNetwork(new List<CablePiece>{a,riser},jointHints:new[]{Splice("V-1",new Vec(1.95,-.1,-.05),new Vec(2.2,.1,.15))});
        Check(n.SpliceBridgeCount==1,"vertical splice bridges a horizontal end and a riser foot");
        Near(n.Find(n.AtPort(0,0),n.AtPort(1,1),false).Length,2+Math.Sqrt(.02)+1.9,"vertical splice route counts its real 3D gap");

        // A dense box still identifies the joint when exactly one port pair faces each other.
        // The third tray leaves the box pointing away, so it cannot pair with either end.
        var third=Line("splice-third",new Vec(2.15,-.05,0),new Vec(2.15,1,0),.2);
        n=new CableNetwork(new List<CablePiece>{a,b,third},jointHints:new[]{hint});
        Check(n.SpliceBridgeCount==1&&n.GraphEdges.Single(e=>e.EdgeId.StartsWith("$splice-bridge:")).Join.B.Piece==1,"a single facing pair inside a dense box is bridged");
        Check(!n.Ambiguities.Any(s=>s.Contains("H-1")),"the bridged dense box is not also reported as ambiguous");
        var sized=Splice("H-S",hint.Min,hint.Max);sized.NominalWidth=.15;sized.NominalHeight=.1;
        var small=Line("splice-small-crossing",new Vec(2.15,-.05,0),new Vec(2.15,-1,0),.05);
        n=new CableNetwork(new List<CablePiece>{a,b,small},jointHints:new[]{sized});
        Check(n.SpliceBridgeCount==1&&n.GraphEdges.Single(e=>e.EdgeId.StartsWith("$splice-bridge:")).Join.B.Piece==1,"a smaller tray passing through the box is ignored by the nominal section");
        // Two independent facing pairs in one box cannot be told apart.
        var wide=Splice("H-W",new Vec(1.95,-.1,-.075),new Vec(2.3,.7,.075));
        var cross=Line("splice-cross-a",new Vec(.9,.5,0),new Vec(2.15,.5,0),.2);
        var crossB=Line("splice-cross-b",new Vec(2.25,.5,0),new Vec(3,.5,0),.2);
        n=new CableNetwork(new List<CablePiece>{a,b,cross,crossB},jointHints:new[]{wide});
        Check(n.SpliceBridgeCount==0&&n.Ambiguities.Any(s=>s.Contains("H-W")),"two facing pairs inside one box stay ambiguous");

        var away=Line("splice-away",new Vec(2.1,.1,0),new Vec(1.4,.1,0),.2);
        var awayPorts=new CableNetwork(new List<CablePiece>{a,away},jointHints:new[]{Splice("H-2",new Vec(1.95,-.1,-.075),new Vec(2.2,.2,.075))});
        Check(awayPorts.SpliceBridgeCount==0,"a port pointing away from its partner is not joined by a splice");

        var far=new CableNetwork(new List<CablePiece>{a,b},jointHints:new[]{Splice("H-3",new Vec(5,5,5),new Vec(5.2,5.2,5.2))});
        Check(far.SpliceBridgeCount==0&&far.PhysicalComponentCount==2,"a splice box away from the tray ends bridges nothing");
    }
}
