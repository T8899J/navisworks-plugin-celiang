using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;
class CableNetworkChecks
{
    static int count;
    static void Check(bool pass,string name){if(!pass)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
    static void Near(double a,double b,string name){Check(Math.Abs(a-b)<1e-8,name);}
    static void Fails(Action f,string name){bool failed=false;try{f();}catch(InvalidOperationException){failed=true;}Check(failed,name);}
    static CablePiece Line(string name,Vec a,Vec b,double width,Vec widthAxis,double height=.1)
    {
        var direction=(b-a)*(1/(b-a).Norm);
        return new CablePiece{Domain="Electrical",WidthAxis=widthAxis,HeightAxis=direction.Cross(widthAxis),Shape=new Part{Id=name,Name=name,System=name,Kind="straight",Centerline=new[]{a,b},Ports=new[]{new Port{Point=a,Outward=direction*(-1),Width=width,Height=height},new Port{Point=b,Outward=direction,Width=width,Height=height}}}};
    }
    static CablePiece MainTray(string id="main"){return Line(id,new Vec(0,0,0),new Vec(10,0,0),.4,new Vec(0,1,0));}
    static CablePiece Branch(double y=-.2,double z=0){return Line("different branch",new Vec(3,-2,z),new Vec(3,y,z),.1,new Vec(1,0,0));}
    static CableNetwork Net(params CablePiece[] p){return new CableNetwork(p.ToList());}
    public sealed class Capture{public string file{get;set;}public List<Item> items{get;set;}}
    public sealed class Item{public string id{get;set;}public string name{get;set;}public string run{get;set;}public string description{get;set;}public string size{get;set;}public List<Triangle> triangles{get;set;}}
    static void Main(string[] args)
    {
        var n=Net(MainTray(),Branch());Check(n.Joins.Count==1&&n.Joins[0].Kind=="branch-to-middle","different names and widths connect at main interior");
        var route=n.Find(n.At(1,0),n.At(0,9));Near(route.Length,8,"branch plus connection plus traversed main only");
        Near(route.Steps.Where(x=>x.Piece==0).Sum(x=>x.Length),6,"unused main segment not counted");
        Check(route.RequiresReview,"side entry remains a candidate until verified");
        Fails(()=>n.Find(n.At(1,0),n.At(0,9),false),"strict mode never silently accepts inferred side opening");
        Near(n.Find(n.At(0,9),n.At(1,0)).Length,8,"reverse direction same length");
        Near(n.Find(n.At(0,2),n.At(0,7)).Length,5,"partial length on same main");
        Near(n.Find(n.At(0,2),n.At(0,2)).Length,0,"same projected point zero");
        Near(n.Project(0,new Vec(4,.2,.05)).Station,4,"3D picked point projects to centerline");
        Check(Net(MainTray(),Branch(-.21)).Joins.Count==0,"10 mm side gap not connected");
        Check(Net(MainTray(),Branch(-.19)).Joins.Count==0,"penetrating overlap not treated as termination");
        Check(Net(MainTray(),Branch(-.2,.08)).Joins.Count==0,"stacked trays stay disconnected");
        Check(Net(MainTray(),Line("crossing",new Vec(3,-2,0),new Vec(3,2,0),.1,new Vec(1,0,0))).Joins.Count==0,"middle crossings never connect");
        Check(Net(MainTray(),Line("parallel",new Vec(1,-.2,0),new Vec(3,-.2,0),.1,new Vec(0,1,0))).Joins.Count==0,"parallel proximity never connects");
        var foreign=Branch();foreign.Domain="Instrumentation";Check(Net(MainTray(),foreign).Joins.Count==0,"different model disciplines excluded");
        var ambiguous=Net(MainTray(),MainTray("duplicate"),Branch());Check(ambiguous.Joins.Count==0&&ambiguous.Ambiguities.Count>0,"overlapping main candidates rejected");
        var next=Line("renamed next",new Vec(10,0,0),new Vec(12,0,0),.4,new Vec(0,1,0));n=Net(MainTray(),next);
        Check(n.Joins.Count==1&&!n.Joins[0].RequiresReview,"different run end-to-end join");Near(n.Find(n.At(0,8),n.At(1,1)).Length,3,"cross-run continuous length");
        n=Net(MainTray(),Branch(),Line("second branch",new Vec(7,2,0),new Vec(7,.2,0),.1,new Vec(1,0,0)));
        Near(n.Find(n.At(1,0),n.At(2,0)).Length,8,"branch to main to another branch");
        if(args.Length>0)
        {
            if(args.Length!=2 && args.Length!=4)
                throw new ArgumentException("Usage: CableNetworkChecks <capture.json> <result.json> [branch-name main-name]");
            var json=new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true,WriteIndented=true};
            var capture=JsonSerializer.Deserialize<Capture>(File.ReadAllText(args[0]),json);var pieces=new List<CablePiece>();var rejected=new List<string>();
            foreach(var item in capture.items)
            {
                try
                {
                    if(item.triangles==null)continue;var parts=item.size.Replace("mm","").Split('x');double width=double.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture)/1000,height=double.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture)/1000;
                    CablePiece piece;
                    if(item.description.Contains("Straight",StringComparison.OrdinalIgnoreCase))piece=CableNetwork.Straight(item.triangles,width,height);
                    else if(item.description.Contains("90Deg")&&item.description.Contains("Riser"))piece=new CablePiece{Shape=RouteGeometry.Riser90(item.triangles,width,height)};
                    else continue;
                    piece.Shape.Id=item.id;piece.Shape.Name=item.name;piece.Shape.System=item.run;piece.Domain="Electrical";pieces.Add(piece);
                }
                catch(Exception e){rejected.Add(item.name+": "+e.Message);}
            }
            n=new CableNetwork(pieces);Console.WriteLine($"Real pieces={pieces.Count} joins={n.Joins.Count} sides={n.Joins.Count(j=>j.Kind=="branch-to-middle")} rejected={rejected.Count} ambiguous={n.Ambiguities.Count}");
            var side=n.Joins.First(j=>j.Kind=="branch-to-middle" && (args.Length==2 ||
                (pieces[j.A.Piece].Shape.Name==args[2] && pieces[j.B.Piece].Shape.Name==args[3])));
            var start=n.At(side.A.Piece,side.A.Station==0?pieces[side.A.Piece].Shape.Length:0);var finish=n.At(side.B.Piece,pieces[side.B.Piece].Shape.Length);
            route=n.Find(start,finish);Check(route.Pieces.Length==2&&route.RequiresReview,"real captured branch to a different main");
            Near(route.Steps.Where(s=>s.Piece==side.B.Piece).Sum(s=>s.Length),pieces[side.B.Piece].Shape.Length-side.B.Station,"real main counted only after junction");
            File.WriteAllText(args[1],JsonSerializer.Serialize(new{model=capture.file,start,finish,route,pieces=route.Pieces.Select(i=>new{index=i,piece=pieces[i]}),join=side,joinCount=n.Joins.Count,sideCount=n.Joins.Count(j=>j.Kind=="branch-to-middle"),rejected,ambiguities=n.Ambiguities},json));
            Console.WriteLine($"REAL ROUTE: {route.Length:F9} m; side gap {side.SurfaceGap*1000:F3} mm");
        }
        Console.WriteLine("RESULT: "+count+" checks passed.");
    }
}
