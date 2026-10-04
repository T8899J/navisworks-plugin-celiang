using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static CableNetwork AdmissionNet(int topN,bool experimental,double angle,params CablePiece[] pieces)
    {
        return new CableNetwork(pieces.ToList(),options:new CableNetworkOptions{GapBridgeMaxDistance=.05,
            VirtualConnectorMaxDistance=.5,VirtualConnectorExperimentalTopN=experimental,VirtualConnectorTopN=topN,
            VirtualConnectorMaxAngle=angle,VirtualConnectorRejectParallelOffset=true});
    }

    static void CheckOneAutomaticConnectorPerPort(CableNetwork network,string name)
    {
        var sockets=VirtualEdges(network).SelectMany(e=>e.Join.VirtualConnector.Kind==VirtualConnectorKind.PortToPort3D
            ?new[]{e.Join.A.Piece+":"+e.Join.A.Port,e.Join.B.Piece+":"+e.Join.B.Port}
            :new[]{e.Join.A.Piece+":"+e.Join.A.Port});
        Check(sockets.GroupBy(s=>s).All(g=>g.Count()<=1),name+": each port has at most one incident automatic connector");
        foreach(var edge in VirtualEdges(network))
        {
            var d=edge.Join.VirtualConnector;var delta=d.TargetPoint-d.SourcePoint;
            Check(network.Pieces[d.SourcePiece].Shape.Ports[d.SourcePort].Outward.Dot(delta)>1e-7,name+": source is strictly forward");
            if(d.Kind==VirtualConnectorKind.PortToPort3D)
                Check(network.Pieces[d.TargetPiece].Shape.Ports[d.TargetPort].Outward.Dot(delta*(-1))>1e-7,name+": target is strictly forward");
            Check(d.Status=="Candidate"&&d.RequiresReview&&edge.RequiresReview&&edge.Join.RequiresReview,name+": only review candidates become graph edges");
        }
    }

    static void VirtualAdmissionCases(string artifacts)
    {
        NoVirtualTriangleShortcut(artifacts);
        var source=Line("admission-source",new Vec(-2,0,0),new Vec(),.2);
        var port=Line("admission-facing-port",new Vec(.3,0,.2),new Vec(2.3,0,.2),.2);
        var pieces=new List<CablePiece>{source,port};
        for(int i=1;i<=6;i++)pieces.Add(Line("near-segment-"+i,new Vec(i*.04,-2,.02),new Vec(i*.04,2,.02),.2));
        foreach(bool experimental in new[]{false,true})foreach(int topN in new[]{1,5})
        {
            var n=AdmissionNet(topN,experimental,0,pieces.ToArray());
            var diagnostics=n.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0&&d.SourcePort==1).ToArray();
            Check(diagnostics.Length==topN&&diagnostics.All(d=>d.Kind==VirtualConnectorKind.PortToSegment3D&&d.Status=="DiagnosticOnly"),
                "port priority: closer Top-N segment diagnostics never override a legal facing port");
            Check(diagnostics.Select(d=>d.CandidateRank).SequenceEqual(Enumerable.Range(1,topN)),"diagnostic Top-N keeps distance ranks including non-routable alternatives");
            var edge=VirtualEdges(n).Single();var chosen=edge.Join.VirtualConnector;
            Check(chosen.SourcePiece==0&&chosen.TargetPiece==1&&chosen.Kind==VirtualConnectorKind.PortToPort3D&&chosen.CandidateRank==7,
                "port priority: legal port outside diagnostic Top-N is still the one automatic connector");
            Check(chosen.TargetPortCandidateRank>0&&chosen.ParallelOffsetRisk,"selected port retains reverse ranking and staggered height risk");
            CheckOneAutomaticConnectorPerPort(n,"port priority independent of Top-N and compatibility flag");
        }

        var segments=pieces.Where((piece,index)=>index!=1).ToArray();
        var segmentNet=AdmissionNet(5,true,0,segments);
        var segmentChoices=segmentNet.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0&&d.SourcePort==1).ToArray();
        Check(segmentChoices.Length==5&&segmentChoices[0].Status=="Candidate"&&segmentChoices.Skip(1).All(d=>d.Status=="DiagnosticOnly"),
            "segment fallback: only the nearest of six legal projections is admitted while Top-5 remain visible");
        Check(VirtualEdges(segmentNet).Length==1&&VirtualEdges(segmentNet).Single().Join.VirtualConnector.TargetPiece==1,
            "segment fallback: routing uses one nearest segment and no farther projections");
        CheckOneAutomaticConnectorPerPort(segmentNet,"segment fallback");

        var broad=Line("wide-angle-stagger",new Vec(.01,.3,.2),new Vec(2.01,.3,.2),.2);
        var broadNet=AdmissionNet(5,true,60,source,broad);var broadEdge=VirtualEdges(broadNet).Single();
        Check(broadEdge.Join.VirtualConnector.SourceDirectionAngleDegrees>60&&broadEdge.Join.VirtualConnector.TargetDirectionAngleDegrees>60&&
            broadEdge.Join.VirtualConnector.ParallelOffsetRisk,"forward staggered end-to-end survives angles beyond the old 60 degree cone");
        CheckOneAutomaticConnectorPerPort(broadNet,"broad-angle forward stagger");
        ReversibleVirtualRoute(broadNet,broadNet.AtPort(0,0),broadNet.AtPort(1,1),4+Math.Sqrt(.1301),"forward staggered end-to-end");

        foreach(double forward in new[]{-.01,0,5e-8,2e-7})
        {
            var n=AdmissionNet(5,true,0,source,Line("epsilon-target",new Vec(forward,.3,.2),new Vec(forward+2,.3,.2),.2));
            var d=n.VirtualConnectorCandidates.Single(c=>c.SourcePiece==0&&c.SourcePort==1&&c.Kind==VirtualConnectorKind.PortToPort3D);
            if(forward<=1e-7)Check(d.Status=="BehindSourcePort"&&VirtualEdges(n).Length==0,"forward epsilon excludes a behind, tangent or numerically coincident target: "+forward);
            else Check(d.Status=="Candidate"&&VirtualEdges(n).Length==1,"positive forward displacement above epsilon is sufficient without a cone");
        }

        // A closer, backward port must not hide the next legal forward port.
        var behind=Line("backward-near-port",new Vec(-.1,.1,.1),new Vec(-2.1,.1,.1),.2);
        var behindNet=AdmissionNet(5,true,0,source,behind,port);
        var behindChoices=behindNet.VirtualConnectorCandidates.Where(d=>d.SourcePiece==0&&d.SourcePort==1).ToArray();
        Check(behindChoices[0].Status=="BehindSourcePort"&&behindChoices.Any(d=>d.TargetPiece==2&&d.Status=="Candidate"),
            "nearest legal selection ignores a closer backward candidate but preserves its diagnostic rank");
        CheckOneAutomaticConnectorPerPort(behindNet,"backward diagnostic before legal port");

        // Two sources want the same target. Only the target's nearest source can share its slot.
        var competing=Line("competing-source",new Vec(-2,.15,0),new Vec(0,.15,0),.2);
        var shared=Line("shared-target",new Vec(.2,0,.1),new Vec(2.2,0,.1),.2);
        var conflict=AdmissionNet(5,true,0,source,competing,shared);
        Check(VirtualEdges(conflict).Length==1&&VirtualEdges(conflict).Single().Join.VirtualConnector.SourcePiece==0,
            "competing ports cannot create multiple incoming automatic edges at the same target");
        Check(conflict.VirtualConnectorCandidates.Any(d=>d.SourcePiece==1&&d.TargetPiece==2&&d.Status=="DiagnosticOnly"),
            "non-mutual nearest port remains diagnostic instead of falling back to another edge");
        CheckOneAutomaticConnectorPerPort(conflict,"shared target conflict");

        // Even before the lower chain is fully recognized, both ends prefer each other over the main.
        var pair=AdmissionNet(5,true,0,Line("isolated-top",new Vec(0,0,.3),new Vec(0,0,2.3),.2),
            Line("isolated-lower",new Vec(0,0,-1.8),new Vec(0,0,.2),.2),
            Line("near-main",new Vec(-1,.25,.25),new Vec(1,.25,.25),.2));
        var projections=pair.VirtualConnectorCandidates.Where(d=>d.Kind==VirtualConnectorKind.PortToSegment3D&&d.TargetPiece==2).ToArray();
        Check(projections.Length==2&&projections.All(d=>d.ForwardOffset>1e-7&&d.Status=="DiagnosticOnly")&&VirtualEdges(pair).Length==1,
            "NoVirtualTriangleShortcut: both facing ports suppress their otherwise legal main projections");
        CheckOneAutomaticConnectorPerPort(pair,"isolated facing pair suppresses both triangle sides");

        var json=JsonSerializer.Serialize(segmentChoices,new JsonSerializerOptions{IncludeFields=true});
        using(var document=JsonDocument.Parse(json))
        {
            var rows=document.RootElement.EnumerateArray().ToArray();
            Check(rows.Length==5&&rows.All(row=>new[]{"CandidateRank","Distance3D","SourceDirectionAngleDegrees","TargetDirectionAngleDegrees","ParallelOffsetRisk","Status"}.All(field=>row.TryGetProperty(field,out _))),
                "diagnostic JSON retains all Top-5 ranking, distance, direction, risk and status fields");
            Check(rows.Count(row=>row.GetProperty("Status").GetString()=="DiagnosticOnly")==4,"diagnostic JSON marks every unselected forward projection DiagnosticOnly");
        }

        var partial=AdmissionNet(5,true,0,source,port,Line("unreachable-finish",new Vec(10,10,10),new Vec(12,10,10),.2));
        int edgeCount=partial.GraphEdges.Count,joinCount=partial.Joins.Count;
        var failureView=partial.DiagnoseConnectivity(partial.AtPort(0,0),partial.AtPort(2,1));
        Check(failureView.Candidates.Single(d=>d.SourcePiece==0&&d.SourcePort==1&&d.TargetPiece==1&&d.TargetPort==0).Status=="Candidate",
            "failure UI/JSON identifies an existing automatic graph connector as Candidate");
        Check(failureView.Candidates.Where(d=>d.TargetPiece==2).All(d=>d.Status!="Candidate"),"failure UI/JSON never labels unreachable diagnostic alternatives as graph candidates");
        Check(partial.GraphEdges.Count==edgeCount&&partial.Joins.Count==joinCount&&failureView.DiagnosticOnly,
            "reporting actual admission statuses never mutates the routing graph");
    }

    static void NoVirtualTriangleShortcut(string artifacts)
    {
        var top=Line("Top vertical tray",new Vec(0,0,.3),new Vec(0,0,2.3),.2);
        var lower=Line("Lower vertical tray",new Vec(0,0,-1.8),new Vec(0,0,.2),.2);
        var main=Line("Main",new Vec(-1,.25,.25),new Vec(1,.25,.25),.2);
        var bend=Line("Lower bend",lower.Shape.Ports[0].Point,main.Shape.Ports[0].Point,.2);
        bend.Shape.Kind="Elbow90";
        bend.Shape.Ports[0].Outward=new Vec(0,0,1);bend.Shape.Ports[1].Outward=new Vec(1,0,0);
        bend.Shape.InternalEdges[0].Centerline=new[]{bend.Shape.Ports[0].Point,new Vec(0,0,-2.2),
            new Vec(-1.4,.25,-2.2),new Vec(-1.4,.25,.25),bend.Shape.Ports[1].Point};
        var n=ProductionVirtualNet(top,lower,bend,main);
        Check(n.PhysicalComponentCount==2&&n.PhysicalPieceComponents[1]==n.PhysicalPieceComponents[3],"NoVirtualTriangleShortcut: lower, bend and main retain their real connected chain");
        var edges=VirtualEdges(n);
        Check(edges.Length==1&&edges[0].Join.VirtualConnector.Kind==VirtualConnectorKind.PortToPort3D,
            "NoVirtualTriangleShortcut: routing graph contains only the facing TopPort to LowerPort connector");
        var selected=edges[0].Join.VirtualConnector;
        Check(selected.SourcePiece==0&&selected.SourcePort==0&&selected.TargetPiece==1&&selected.TargetPort==1,
            "NoVirtualTriangleShortcut: nearest facing tray ends are connected");
        Check(!edges.Any(e=>e.Join.VirtualConnector.Kind==VirtualConnectorKind.PortToSegment3D),
            "NoVirtualTriangleShortcut: neither top nor lower adds a shortcut to MainSegment");
        var mainCandidate=n.VirtualConnectorCandidates.Single(d=>d.SourcePiece==0&&d.SourcePort==0&&d.TargetPiece==3&&d.Kind==VirtualConnectorKind.PortToSegment3D);
        Check(mainCandidate.Distance3D<.5&&mainCandidate.ForwardOffset>0&&mainCandidate.Status=="DiagnosticOnly",
            "NoVirtualTriangleShortcut: the legal nearby main projection is retained only in diagnostics");
        var route=n.Find(n.AtPort(0,1),n.AtPort(3,1));
        double expected=top.Shape.Length+.1+lower.Shape.Length+bend.Shape.Length+main.Shape.Length;
        Near(route.Length,expected,"NoVirtualTriangleShortcut: TotalLength-first traverses the lower tray and real bend/main chain");
        Check(route.Pieces.Contains(1)&&route.Pieces.Contains(2)&&route.VirtualConnectorCount==1&&route.RequiresReview,
            "NoVirtualTriangleShortcut: route includes lower and bend with exactly one review connector");
        CostReverse(n,n.AtPort(0,1),n.AtPort(3,1),route,"NoVirtualTriangleShortcut");
        if(artifacts!=null)
        {
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts,"no-virtual-triangle-shortcut.json"),JsonSerializer.Serialize(new{
                synthetic=true,expectedLength=expected,candidates=n.VirtualConnectorCandidates,graphEdges=n.GraphEdges,result=route},
                new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        }
    }
}
