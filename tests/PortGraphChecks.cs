using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

static partial class PortGraphChecks
{
    static int count;
    static void Check(bool pass,string name) { if(!pass)throw new InvalidOperationException("FAIL: "+name);count++;Console.WriteLine("PASS: "+name); }
    static void Near(double actual,double expected,string name,double tolerance=1e-7) { Check(Math.Abs(actual-expected)<=tolerance,name+" ("+actual.ToString("G12")+" / "+expected.ToString("G12")+")"); }
    static void Reject(Action action,string name) { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,name); }
    static Vec Unit(Vec p) { return p*(1/p.Norm); }
    static CablePiece Line(string name,Vec a,Vec b,double width=.4)
    {
        var d=Unit(b-a);var up=Math.Abs(d.Z)>.9?new Vec(0,1,0):new Vec(0,0,1);var w=Unit(up.Cross(d));up=d.Cross(w);
        return new CablePiece{Domain="Scope-"+name,WidthAxis=w,HeightAxis=up,Shape=new Part{Id=name,Name=name,System="Run-"+name,Kind="Straight",Centerline=new[]{a,b},Junctions=new PartJunction[0],
            Ports=new[]{new Port{Id="P0",Point=a,Outward=d*(-1),Width=width,Height=.1,WidthAxis=w,HeightAxis=up},new Port{Id="P1",Point=b,Outward=d,Width=width,Height=.1,WidthAxis=w,HeightAxis=up}},
            InternalEdges=new[]{new InternalEdge{Id="E0",From="P0",To="P1",Centerline=new[]{a,b}}}}};
    }
    static CablePiece Junction(int arms)
    {
        var ends=new[]{new Vec(-1,0,0),new Vec(1,0,0),new Vec(0,-2,0),new Vec(0,2,0)}.Take(arms).ToArray();
        var ports=ends.Select((p,i)=>new Port{Id="P"+i,Point=p,Outward=Unit(p),Width=i<2?.4:.2,Height=.1,WidthAxis=Unit(new Vec(0,0,1).Cross(p)),HeightAxis=new Vec(0,0,1)}).ToArray();
        return new CablePiece{Domain="Fitting",Shape=new Part{Id="fitting",Name="Three distinct arms",System="Run-J",Kind=arms==3?"Tee":"Cross",Ports=ports,Junctions=new[]{new PartJunction{Id="J",Point=new Vec()}},InternalEdges=ends.Select((p,i)=>new InternalEdge{Id="Arm"+i,From="P"+i,To="J",Centerline=new[]{p,new Vec()}}).ToArray()}};
    }
    static CableNetwork Net(params CablePiece[] p) { return new CableNetwork(p.ToList()); }
    static CablePiece FromMesh(MeshFixture fixture,int index)
    {
        var classification=FittingGeometry.Classify(fixture.Description,fixture.Name);
        var p=FittingGeometry.Build(fixture.Mesh,classification);p.Id="fixture:"+index;p.Name=fixture.Name;p.System=fixture.RunName;
        return new CablePiece{Shape=p,Domain="Fixture-Domain-"+index};
    }
    static void PureGraph()
    {
        var tee=Junction(3);var n=Net(tee);
        Check(n.GraphNodes.Count(x=>x.Kind==CableNodeKind.Port)==3&&n.GraphNodes.Count(x=>x.Kind==CableNodeKind.FittingInternalJunction)==1,"tee has three port nodes and one internal junction");
        Check(n.GraphEdges.Count==3&&n.GraphEdges.All(e=>e.Kind==CableEdgeKind.InternalEdge),"tee has three independent internal arms");
        var route=n.Find(n.AtPort(0,2),n.AtPort(0,1));Near(route.Length,3,"tee entry to exit excludes unused third arm");
        Check(route.InternalEdges.Count==2&&route.FittingContributions.Count==1,"tee contribution contains exactly traversed arms");
        Near(route.FittingContributions.Single().Length,3,"fitting contribution is traversed length");
        Near(n.Find(n.At(0,2,.5),n.At(0,1,.25)).Length,2.25,"start and finish inside different tee arms");
        Near(n.Find(n.AtPort(0,1),n.AtPort(0,2)).Length,3,"multiport path works in reverse");
        Near(n.Find(n.At(0,1,.25),n.At(0,1,.25)).Length,0,"identical partial-edge locations have zero length");
        Near(n.Project(0,new Vec(0,-1.5,.02)).Station,.5,"3D point projects onto the selected tee arm");
        Check(n.Project(0,new Vec(0,-1.5,.02)).Edge==2,"projection retains internal edge identity");
        Reject(()=>n.At(0,.5),"legacy single-edge station cannot silently address a tee");
        n=Net(Junction(4));Check(n.GraphNodes.Count(x=>x.Kind==CableNodeKind.Port)==4,"cross architecture supports four ports");Near(n.Find(n.AtPort(0,2),n.AtPort(0,3)).Length,4,"four-port fitting traverses exactly requested two arms");
        var a=Line("branch-name",new Vec(-2,0,0),new Vec(0,0,0),.2);var b=Line("other-name",new Vec(.001,0,0),new Vec(3.001,0,0),.6);n=Net(a,b);
        Check(n.Joins.Count==1,"different name run domain and size cannot sever touching ports");
        var connection=n.GraphEdges.Single(e=>e.Kind==CableEdgeKind.ConnectionEdge);Near(connection.Length,.001,"connection edge stores only actual 1mm model gap");
        Near(n.Find(n.Project(0,new Vec(-1,0,0)),n.Project(1,new Vec(2.001,0,0))).Length,3.001,"internal travel and small physical gap both counted once");
        b=Line("farther",new Vec(.01,0,0),new Vec(3.01,0,0));Check(Net(a,b).Joins.Count==0,"10mm gap is not silently connected");
        a=Line("main",new Vec(0,0,0),new Vec(10,0,0));b=Line("branch",new Vec(3,-2,0),new Vec(3,-.2,0),.1);n=Net(a,b);
        Check(n.GraphNodes.Any(node=>node.Kind==CableNodeKind.VirtualJunction),"branch-to-middle materializes a virtual junction");
        Check(n.GraphEdges.Where(e=>e.Kind==CableEdgeKind.ConnectionEdge).All(e=>e.Length<.002001),"side access is internal geometry rather than a long physical connection");
        var originalEdges=n.GraphEdges.Count;route=n.Find(n.Project(1,new Vec(3,-1.5,0)),n.Project(0,new Vec(8,0,0)));Near(route.Length,6.5,"station split counts branch and traversed main only");
        Check(route.RequiresReview&&route.ReviewConnections.Count>0,"unverified side opening is exposed as a review connection");
        Reject(()=>n.Find(n.AtPort(1,0),n.AtPort(0,1),false),"strict mode rejects inferred side opening");
        Check(n.GraphEdges.Count==originalEdges,"query-specific station splits do not mutate the base graph");
        Check(Net(a,Line("crossing",new Vec(3,-2,0),new Vec(3,2,0),.1)).Joins.Count==0,"middle crossing without terminating port remains disconnected");
        var stacked=Line("stacked",new Vec(3,-2,.15),new Vec(3,-.2,.15),.1);Check(Net(a,stacked).Joins.Count==0,"vertically separated branch is not connected");
        var fakeBend=Line("not-a-curve",new Vec(),new Vec(2,0,0));fakeBend.Shape.Kind="Elbow90";fakeBend.Shape.InternalEdges[0].Centerline=new[]{new Vec(),new Vec(1,0,0),new Vec(2,0,0)};
        Reject(()=>Net(fakeBend),"three collinear points cannot masquerade as an elbow centerline");
        var reservedPort=Line("reserved-port",new Vec(),new Vec(2,0,0));reservedPort.Shape.Ports[0].Id="$connection:0";reservedPort.Shape.InternalEdges[0].From="$connection:0";
        Reject(()=>Net(reservedPort),"port identifier cannot collide with reserved generated node names");
        var reservedJunction=Junction(3);reservedJunction.Shape.Junctions[0].Id="$virtual:0";foreach(var edge in reservedJunction.Shape.InternalEdges)edge.To="$virtual:0";
        Reject(()=>Net(reservedJunction),"fitting junction identifier cannot collide with reserved generated node names");
        n=Net(Line("first",new Vec(),new Vec(2,0,0)),Line("second",new Vec(0,3,0),new Vec(2,3,0)));
        Check(n.GraphNodes.Count==4&&n.GraphNodes.Count(node=>node.LocalId=="P0")==2,"same local port identifiers in distinct parts remain distinct graph nodes");
        Reject(()=>n.Find(n.AtPort(0,0),n.AtPort(1,0)),"same local port identifier does not create a false connection");
        var rollA=Line("roll-main",new Vec(),new Vec(2,0,0));var rollB=Line("roll-next",new Vec(2,0,0),new Vec(4,0,0));
        foreach(var port in rollB.Shape.Ports){port.WidthAxis=new Vec(0,0,1);port.HeightAxis=new Vec(0,-1,0);}
        n=Net(rollA,rollB);Check(n.Joins.Count==1&&n.Joins[0].RequiresReview&&n.Joins[0].ReviewReason.Contains("截面朝向"),"90-degree roll mismatch remains connected but requires explicit review");
        route=n.Find(n.Project(0,new Vec(1,0,0)),n.Project(1,new Vec(3,0,0)));Near(route.Length,2,"roll mismatch adds no invented transition distance");Check(route.ReviewConnections.Count==1,"rolled connection is exposed in route review details");
        Reject(()=>n.Find(n.AtPort(0,0),n.AtPort(1,1),false),"strict mode excludes an unverified rolled transition");
        var oppositeAxes=Line("signed-axes",new Vec(2,0,0),new Vec(4,0,0));foreach(var port in oppositeAxes.Shape.Ports){port.WidthAxis=port.WidthAxis*(-2);port.HeightAxis=port.HeightAxis*(-3);}
        n=Net(rollA,oppositeAxes);Check(n.Joins.Count==1&&!n.Joins[0].RequiresReview,"axis sign and vector scale conventions do not create false roll mismatches");
        var slightRoll=Line("two-degrees",new Vec(2,0,0),new Vec(4,0,0));double rollAngle=2*Math.PI/180;
        foreach(var port in slightRoll.Shape.Ports){port.WidthAxis=new Vec(0,Math.Cos(rollAngle),Math.Sin(rollAngle));port.HeightAxis=new Vec(0,-Math.Sin(rollAngle),Math.Cos(rollAngle));}
        n=Net(rollA,slightRoll);Check(n.Joins.Count==1&&!n.Joins[0].RequiresReview,"small cross-section angular tolerance avoids tessellation noise reviews");
    }
    static void ConnectivityRegressions()
    {
        var main=Line("regression-main",new Vec(),new Vec(10,0,0));
        Func<double,CablePiece> branch=y=>Line("regression-branch",new Vec(3,-2,0),new Vec(3,y,0),.1);
        Check(Net(main,branch(-.21)).Joins.Count==0,"10mm side-entry gap stays disconnected");
        Check(Net(main,branch(-.19)).Joins.Count==0,"10mm side-entry penetration is not treated as a terminating port");
        Check(Net(main,Line("parallel-neighbour",new Vec(1,-.2,0),new Vec(3,-.2,0),.1)).Joins.Count==0,"parallel proximity to a main side cannot create a branch connection");
        var duplicateMain=Line("duplicate-main",new Vec(),new Vec(10,0,0));var n=Net(main,duplicateMain,branch(-.2));
        Check(n.Joins.Count==0&&n.Ambiguities.Count>0,"overlapping main candidates refuse ambiguous side entry");
        var secondBranch=Line("second-branch",new Vec(7,2,0),new Vec(7,.2,0),.1);n=Net(main,branch(-.2),secondBranch);
        Check(Math.Abs(n.Find(n.AtPort(1,0),n.AtPort(2,0)).Length-8)<1e-8,"branch to main to second branch counts only the intervening route");
        var first=Line("first-socket",new Vec(),new Vec(2,0,0));var wrongDirection=Line("wrong-direction",new Vec(2,0,0),new Vec(3,0,0));wrongDirection.Shape.Ports[0].Outward=new Vec(0,1,0);
        Check(Net(first,wrongDirection).Joins.Count==0,"coincident socket with perpendicular outward direction is not connected");
        n=Net(first,Line("next-socket",new Vec(2,0,0),new Vec(3,0,0)),Line("overlapping-socket",new Vec(2,0,0),new Vec(3,0,0)));
        Check(n.Joins.Count==0&&n.Ambiguities.Count>0,"overlapping end sockets refuse ambiguous physical connection");
    }
    static void GeometryCases()
    {
        Check(FittingGeometry.Classify("Stainless Steel Straight")=="Straight","material name containing tee letters is not a Tee");
        Check(FittingGeometry.Classify("90Deg Riser 450 mm","Run-45501")=="Riser90","450 mm and RunName digits do not override explicit 90-degree fitting");
        Check(FittingGeometry.Classify("Elbow 450 mm","Run-45501")=="Elbow","unspecified fitting angle is not inferred from size or identifier");
        Check(FittingGeometry.Classify("Straight","Fixture-Main-Before-Elbow")=="Straight","explicit Description wins over an incidental fitting word in Name");
        Check(FittingGeometry.Classify("Stainless Steel Straight","Tee-Area-Main")=="Straight","description classification wins over misleading tee name and material letters");
        Check(FittingGeometry.Classify("","Fixture-Elbow90")=="Elbow90","name may classify only when description supplies no recognized type");
        var shortChannel=PortGraphFixtures.Transform(PortGraphFixtures.Straight(new Vec(),new Vec(.884,0,0),.64,.15),p=>p+new Vec(0,0,.075));
        var shortStraight=FittingGeometry.Build(shortChannel,"Straight");
        Check(shortStraight.Ports.All(p=>Math.Abs(p.Point.Z-.075)<1e-8),"U-channel ports use cross-section centre rather than surface centroid");
        Check(Math.Abs(shortStraight.InternalEdges.Single().Length-.884)<1e-8,"explicit straight classification supports a wide 884mm short segment");
        Check(!StraightMeasurement.Measure(shortChannel).IsStraightCandidate,"unclassified wide short segment remains geometrically ambiguous");
        foreach(var d in new[]{new Vec(2,0,0),new Vec(0,2,0),new Vec(0,0,2),Unit(new Vec(1,2,3))*2})
        {
            var p=FittingGeometry.Build(PortGraphFixtures.Straight(new Vec(),d),"sLoPe");Check(p.Ports.Length==2&&p.InternalEdges.Length==1,"arbitrary 3D straight has two ports and one internal edge");Near(p.InternalEdges[0].Length,2,"3D straight uses real centerline length");
        }
        foreach(var kind in new[]{"Elbow45","Elbow90","Riser45","Riser90"})
        {
            double angle=kind.EndsWith("45")?Math.PI/4:Math.PI/2;var mesh=PortGraphFixtures.Transform(PortGraphFixtures.Bend(angle,kind.StartsWith("Riser")),PortGraphFixtures.Rotate);
            var p=FittingGeometry.Build(mesh,kind);Check(p.Ports.Length==2&&p.InternalEdges.Length==1,kind+" reconstructed as two ports with one curved internal edge");
            Near(p.InternalEdges[0].Length,PortGraphFixtures.ArcLength(1,angle),kind+" follows tessellated 3D centerline",.00001);
            Check(p.InternalEdges[0].Length>(p.Ports[0].Point-p.Ports[1].Point).Norm+.015,kind+" never substitutes port-to-port chord");
        }
        var reducer=FittingGeometry.Build(PortGraphFixtures.Transform(PortGraphFixtures.Reducer(new Vec(),new Vec(0,2,0)),PortGraphFixtures.Rotate),"Reducer");
        Near(reducer.Ports.Min(p=>p.Width),.4,"reducer measures narrower port",.001);Near(reducer.Ports.Max(p=>p.Width),.6,"reducer measures wider port",.001);Near(reducer.InternalEdges.Single().Length,2,"reducer true centerline length");
        var tee=FittingGeometry.Build(PortGraphFixtures.Transform(PortGraphFixtures.Tee(),PortGraphFixtures.Rotate),"Tee");
        Check(tee.Ports.Length==3&&tee.Junctions.Length==1&&tee.InternalEdges.Length==3,"union U-channel tee mesh reconstructs three ports and three arms");
        Near(tee.InternalEdges.Sum(e=>e.Length),1.5,"tee stores individual arm geometry",.00001);
        Reject(()=>FittingGeometry.Build(PortGraphFixtures.Tee(),"Cross"),"unimplemented cross geometry is explicitly rejected");
        Reject(()=>FittingGeometry.Build(new List<Triangle>(),"Elbow90"),"empty fitting geometry is rejected");
        Reject(()=>FittingGeometry.Build(PortGraphFixtures.Bend(Math.PI/4),"Elbow90"),"explicit 90-degree label cannot accept a measured 45-degree bend");
        var brokenBend=PortGraphFixtures.Bend().Where(t=>{var p=(t.A+t.B+t.C)*(1.0/3)-new Vec(2,1,0);double a=Math.Atan2(p.Y,p.X);return a<-.9||a>-.65;}).ToList();
        Reject(()=>FittingGeometry.Build(brokenBend,"Elbow90"),"missing middle mesh strip prevents a continuous elbow path");
        var contaminatedBend=PortGraphFixtures.Bend();contaminatedBend.Add(new Triangle(new Vec(8,8,8),new Vec(8.1,8,8),new Vec(8,8.1,8)));
        Reject(()=>FittingGeometry.Build(contaminatedBend,"Elbow90"),"detached extra geometry cannot be hidden by a best-fitting bend subset");
        var kinkedReducer=PortGraphFixtures.Transform(PortGraphFixtures.Reducer(new Vec(),new Vec(0,2,0)),p=>Math.Abs(p.Y-1)<1e-8?p+new Vec(.03,0,0):p);
        Reject(()=>FittingGeometry.Build(kinkedReducer,"Reducer"),"nonlinear intermediate reducer section cannot pass linear taper reconstruction");
    }
    static void FoldedAndSleeveCases()
    {
        foreach(bool riser in new[]{false,true})
        {
            var mesh=PortGraphFixtures.Transform(PortGraphFixtures.Folded90(riser),PortGraphFixtures.Rotate);
            var part=FittingGeometry.Build(mesh,riser?"Riser90":"Elbow90");
            Near(part.Length,.4+Math.Sqrt(.08),"folded 90-degree fitting follows three real 3D legs",.00001);
            Check(part.InternalEdges.Single().Centerline.Length==4,"folded fitting preserves both intermediate corners");
            Check(part.InternalEdges.Single().RequiresReview,"faceted fitting reconstruction is explicitly reviewable");
            Check(part.Ports.All(p=>Math.Abs(p.Width-(riser?.05:.04))<1e-5&&Math.Abs(p.Height-(riser?.04:.05))<1e-5),"folded fitting port frame keeps width and height semantics");
        }
        var bevel=FittingGeometry.Build(PortGraphFixtures.Bevel45(),"Elbow45");
        double diagonal=Math.Sqrt(.5), bevelLength=.26+Math.Sqrt(Math.Pow(.04-.01*diagonal,2)+Math.Pow(.09*diagonal-.05,2))*.5;
        Near(bevel.Length,bevelLength,"unequal rail vertex counts preserve the actual bevel center polyline",.00001);
        Check(bevel.InternalEdges.Single().Centerline.Length==4,"one outer corner pairs with two bevel corners");
        Reject(()=>FittingGeometry.Build(PortGraphFixtures.Bevel45(),"Elbow90"),"folded fallback preserves the explicit angle check");
        var duplicateNoise=PortGraphFixtures.Folded90(true).Select((t,i)=>new Triangle(t.A+new Vec(i%2==0?2e-9:-2e-9,0,0),t.B,t.C)).ToList();
        Near(FittingGeometry.Build(duplicateNoise,"Riser90").Length,.4+Math.Sqrt(.08),"tiny export vertex noise does not disconnect a folded side outline",.00001);
        var detached=PortGraphFixtures.Folded90();detached.Add(new Triangle(new Vec(2,2,2),new Vec(2.1,2,2),new Vec(2,2.1,2)));
        Reject(()=>FittingGeometry.Build(detached,"Elbow90"),"folded reconstruction rejects detached geometry");

        var a=Line("socket-A",new Vec(),new Vec(1,0,0));var b=Line("socket-B",new Vec(.945,0,0),new Vec(2,0,0));var n=Net(a,b);
        Check(n.Joins.Count==1&&n.Joins[0].Kind=="sleeve-overlap","55mm inward overlap forms a reviewed sleeve connection");
        Near(n.Joins[0].OverlapLength,.055,"sleeve records actual measured overlap");
        Near(n.Joins[0].ConnectionLength,0,"overlap connector does not add a 55mm fake gap");
        var route=n.Find(n.AtPort(0,0),n.AtPort(1,1));Near(route.Length,2,"overlapping geometry counts once rather than summing component lengths");
        Near(route.InternalEdges.Sum(e=>e.Length),2,"station splits remove the duplicate internal travel");
        Check(route.RequiresReview&&route.ReviewConnections.Count==1,"sleeve joins remain visible for physical passability review");
        Reject(()=>n.Find(n.AtPort(0,0),n.AtPort(1,1),false),"strict mode excludes inferred sleeve connections");
        Near(n.Find(n.AtPort(1,1),n.AtPort(0,0)).Length,2,"sleeve reverse route has identical length");
        Check(Net(a,Line("empty-gap",new Vec(1.055,0,0),new Vec(2,0,0))).Joins.Count==0,"55mm empty gap cannot masquerade as overlap");
        Check(Net(a,Line("parallel-offset",new Vec(.945,.01,0),new Vec(2,.01,0))).Joins.Count==0,"parallel tray 10mm away cannot become a sleeve");
        Check(Net(a,Line("long-duplicate",new Vec(.4,0,0),new Vec(2,0,0))).Joins.Count==0,"long duplicate geometry does not become a short socket overlap");
        Check(Net(a,b,Line("ambiguous-B",new Vec(.945,0,0),new Vec(2,0,0))).Joins.Count==0,"duplicate sleeve candidates remain ambiguous");
    }
    static void FullMeshRoute(string artifactDirectory)
    {
        var fixtures=PortGraphFixtures.Chain();var pieces=fixtures.Select(FromMesh).ToList();var n=new CableNetwork(pieces);
        Check(pieces.Select(p=>p.Shape.Name).Distinct().Count()==7&&pieces.Select(p=>p.Shape.System).Distinct().Count()==7,"full mesh chain uses distinct names and RunNames");
        Check(Math.Abs(pieces[0].Shape.Ports[0].Width-pieces[6].Shape.Ports[0].Width)>.3,"full chain endpoints have different widths");
        Check(n.Joins.Count==6,"full mesh chain has six continuous physical component connections");
        Check(n.GraphEdges.Count(e=>e.Kind==CableEdgeKind.ConnectionEdge)==6,"full mesh chain distinguishes physical connections from internal travel");
        var start=n.Project(0,PortGraphFixtures.Start);var finish=n.Project(6,PortGraphFixtures.Finish);var route=n.Find(start,finish);
        Near(route.Length,PortGraphFixtures.ExpectedRouteLength,"Branch -> Tee -> Main -> Elbow90 -> Slope -> Reducer -> Main full route",.00002);
        Check(route.Pieces.Length==7,"full route reports seven visited components");
        var expected=new[]{1.5,1,1.5,PortGraphFixtures.ArcLength(1,Math.PI/2),2,1,1.5};
        for(int i=0;i<expected.Length;i++)Near(route.InternalEdges.Where(e=>e.Piece==i).Sum(e=>e.Length),expected[i],"traversed contribution: "+fixtures[i].Name,.00002);
        Check(route.InternalEdges.Count==8,"tee contributes two actual arms to eight traversed internal edges");
        Near(route.FittingContributions.Single(f=>f.Piece==1).Length,1,"tee contribution omits unused arm",.00001);
        Near(route.FittingContributions.Single(f=>f.Piece==3).Length,PortGraphFixtures.ArcLength(1,Math.PI/2),"elbow fitting contribution is curved",.00001);
        CostReverse(n,start,finish,route,"full 3D mesh route");
        Check(route.Steps.Where(s=>s.Kind==CableEdgeKind.ConnectionEdge).All(s=>s.Length<.002001),"full route physical connections remain near zero");
        if(artifactDirectory!=null)
        {
            Directory.CreateDirectory(artifactDirectory);File.WriteAllText(Path.Combine(artifactDirectory,"port-graph-checks.json"),JsonSerializer.Serialize(new{synthetic=true,checks=count,expectedLength=PortGraphFixtures.ExpectedRouteLength,actualLength=route.Length,route,start,finish,graphNodes=n.GraphNodes,graphEdges=n.GraphEdges,fixtures=fixtures.Select(f=>new{f.Name,f.RunName,f.Description,f.Size})},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        }
    }
    static void ReplayCaptured(string capturePath,string queryPath,string output,bool virtualConnectors=false)
    {
        using var capture=JsonDocument.Parse(File.ReadAllText(capturePath));
        using var query=JsonDocument.Parse(File.ReadAllText(queryPath));
        var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var pieces=new List<CablePiece>();var failures=new List<string>();
        foreach(var item in capture.RootElement.GetProperty("items").EnumerateArray())
        {
            string name=item.GetProperty("name").GetString();
            try
            {
                var mesh=JsonSerializer.Deserialize<List<Triangle>>(item.GetProperty("mesh").GetRawText(),options);
                var part=FittingGeometry.Build(mesh,FittingGeometry.Classify(item.GetProperty("description").GetString(),name));
                part.Id=item.GetProperty("id").GetString();part.Name=name;part.System=item.GetProperty("run").GetString();
                pieces.Add(new CablePiece{Shape=part});Console.WriteLine("RECONSTRUCTED: "+name+"; "+part.Kind+"; "+part.Length.ToString("F9")+" m");
            }
            catch(InvalidOperationException e){failures.Add(name+": "+e.Message);Console.WriteLine(failures.Last());}
        }
        var settings=virtualConnectors?new CableNetworkOptions{GapBridgeMaxDistance=.05,VirtualConnectorMaxDistance=.5}:null;
        var network=new CableNetwork(pieces,options:settings);CableRoute route=null;string error=null;
        Func<string,CableLocation> location=key=>{
            var saved=query.RootElement.GetProperty(key);
            var metadata=query.RootElement.GetProperty("recognized")[saved.GetProperty("Piece").GetInt32()];
            int i=pieces.FindIndex(p=>p.Shape.Id==metadata.GetProperty("ModelItemId").GetString());
            if(i<0)throw new InvalidOperationException("Captured endpoint was not reconstructed");
            return network.Project(i,JsonSerializer.Deserialize<Vec>(saved.GetProperty("Point").GetRawText(),options));
        };
        try{route=network.Find(location("start"),location("finish"));}catch(InvalidOperationException e){error=e.Message;}
        File.WriteAllText(output,JsonSerializer.Serialize(new{capturePath,queryPath,settings,parts=pieces.Select(p=>p.Shape),failures,result=route,error,joins=network.Joins,
            physicalComponents=network.PhysicalPieceComponents,virtualConnectorCandidates=network.VirtualConnectorCandidates,ambiguities=network.Ambiguities},options));
        Check(failures.Count==0,"all captured selected components reconstruct");
        Check(route!=null,"captured user start and finish have a continuous path");
        CostReverse(network,location("start"),location("finish"),route,"captured route");
        Near(route.Steps.Sum(s=>s.Length),route.Length,"captured route total equals traversed edges");
        if(virtualConnectors){Check(network.PhysicalComponentCount==1,"captured real geometry is one physical component before virtual search");Check(route.VirtualConnectorCount==0&&!network.GraphEdges.Any(e=>e.Kind==CableEdgeKind.VirtualConnectorEdge),"virtual mode creates no shortcut in the captured real component");}
        Console.WriteLine("CAPTURED RESULT: "+route.Length.ToString("F9")+" m; "+route.Pieces.Length+" parts; requires review: "+route.RequiresReview);
    }
    static void Main(string[] args)
    {
        try{Run(args);}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Run(string[] args)
    {
        if(args.Length==4&&(args[0]=="--replay-capture"||args[0]=="--replay-capture-virtual")){try{ReplayCaptured(args[1],args[2],args[3],args[0]=="--replay-capture-virtual");}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}return;}
        if(args.Length>0&&args[0]=="--fixture-only"){PortGraphFixtures.WriteArtifacts(args.Length>1?args[1]:"artifacts");Console.WriteLine("Synthetic IFC, mesh manifest and expected lengths written.");return;}
        string artifacts=args.Length>0?args[0]:null;PureGraph();ConnectivityRegressions();GeometryCases();FoldedAndSleeveCases();GapBridgeCases();VirtualConnectorCases(artifacts);ConnectivityDiagnosticCases();PathCostCases();FullMeshRoute(artifacts);if(artifacts!=null){PortGraphFixtures.WriteArtifacts(artifacts);PortGraphFixtures.WriteGapBridgeArtifacts(artifacts);PortGraphFixtures.WriteVirtualConnectorArtifacts(artifacts);PortGraphFixtures.WriteConnectivityArtifacts(artifacts);}Console.WriteLine("RESULT: "+count+" port graph checks passed.");
    }
}
