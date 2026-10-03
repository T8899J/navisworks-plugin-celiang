using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;
class RouteChecks
{
    static int count;
    static void Check(bool pass,string name){if(!pass)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
    static void Fails(Action f,string name){bool failed=false;try{f();}catch(InvalidOperationException){failed=true;}Check(failed,name);}
    static void Quad(List<Triangle> t,Vec a,Vec b,Vec c,Vec d){t.Add(new Triangle(a,b,c));t.Add(new Triangle(a,c,d));}
    static List<Triangle> Straight(double length,Vec offset=default(Vec))
    {
        var t=new List<Triangle>(); var a=new Vec(0,-.32,0)+offset;var b=new Vec(0,.32,0)+offset;
        var c=new Vec(0,.32,.15)+offset;var d=new Vec(0,-.32,.15)+offset;var l=new Vec(length,0,0);
        Quad(t,a,b,b+l,a+l);Quad(t,b,c,c+l,b+l);Quad(t,d,a,a+l,d+l);return t;
    }
    static List<Triangle> Bend()
    {
        var t=new List<Triangle>();
        for(int i=0;i<5;i++)
        {
            var a=new Vec(.5771*Math.Cos(i*Math.PI/10),.5771*Math.Sin(i*Math.PI/10),0);
            var b=new Vec(.7271*Math.Cos(i*Math.PI/10),.7271*Math.Sin(i*Math.PI/10),0);
            var c=new Vec(.7271*Math.Cos((i+1)*Math.PI/10),.7271*Math.Sin((i+1)*Math.PI/10),0);
            var d=new Vec(.5771*Math.Cos((i+1)*Math.PI/10),.5771*Math.Sin((i+1)*Math.PI/10),0);
            Vec w=new Vec(0,0,.64);Quad(t,a,b,c,d);Quad(t,a+w,b+w,c+w,d+w);Quad(t,b,b+w,c+w,c);
        }return t;
    }
    static Part P(List<Triangle> mesh){var p=RouteGeometry.Straight(mesh,.6,.15);p.System="test";return p;}
    public class Capture{public List<CapturedPart> items{get;set;}}
    public class CapturedPart{public string path{get;set;}public List<Triangle> triangles{get;set;}}
    static void Main(string[] args)
    {
        var p=P(Straight(2));Check(Math.Abs(p.Length-2)<1e-8,"straight geometry length");
        Check(Math.Abs(p.Ports[0].Point.Z-.075)<1e-8,"ports use section center, not surface centroid");
        Check(Math.Abs(P(Straight(.884)).Length-.884)<1e-8,"metadata-labelled short straight");
        Check(!StraightMeasurement.Measure(Straight(.884)).IsStraightCandidate,"default measurement still rejects ambiguous short piece");
        var bend=RouteGeometry.Riser90(Bend(),.6,.15);
        Check(Math.Abs(bend.Length-10*.6521*Math.Sin(Math.PI/20))<1e-8,"bend uses faceted centerline, not chord or nominal radius");
        Check(Math.Abs(bend.Ports[0].Outward.Dot(bend.Ports[1].Outward))<1e-8,"bend orthogonal ports");
        Func<Vec,Vec> rotate=v=>new Vec(v.X*.8-v.Y*.6,v.X*.36+v.Y*.48-v.Z*.8,v.X*.48+v.Y*.64+v.Z*.6)+new Vec(102,30,49);
        var rotated=Bend().Select(t=>new Triangle(rotate(t.A),rotate(t.B),rotate(t.C))).ToList();
        Check(Math.Abs(RouteGeometry.Riser90(rotated,.6,.15).Length-bend.Length)<1e-8,"rotated translated bend");
        Fails(()=>RouteGeometry.Riser90(Straight(1),.6,.15),"straight not misclassified as bend");
        Fails(()=>RouteGeometry.Riser90(Bend(),.6,.3),"wrong bend section rejected");
        var parts=new[]{P(Straight(2)),P(Straight(1,new Vec(2,0,0))),P(Straight(1,new Vec(3,0,0)))};
        var edges=RouteGeometry.Connect(parts,.002);Check(edges.Count==2,"three-part chain");
        var route=RouteGeometry.Find(parts,edges,0,2);Check(route.Items.SequenceEqual(new[]{0,1,2})&&Math.Abs(route.Length-4)<1e-8,"find connected route");
        Check(RouteGeometry.Find(parts,edges,2,0).Items.SequenceEqual(new[]{2,1,0}),"reverse route");
        Check(RouteGeometry.Connect(new[]{parts[0],P(Straight(1,new Vec(2.01,0,0)))},.002).Count==0,"10 mm break stays disconnected");
        var shifted=P(Straight(1,new Vec(2.001,0,0)));var smallGap=new[]{parts[0],shifted};
        Check(Math.Abs(RouteGeometry.Find(smallGap,RouteGeometry.Connect(smallGap,.002),0,1).Length-3.001)<1e-8,"joint gap included explicitly");
        shifted.System="different";Check(RouteGeometry.Connect(new[]{parts[0],shifted},.002).Count==0,"different systems excluded");
        shifted.System="test";shifted.Ports[0].Outward=new Vec(0,1,0);Check(RouteGeometry.Connect(new[]{parts[0],shifted},.002).Count==0,"crossing orientation excluded");
        shifted=P(Straight(1,new Vec(2,0,0)));shifted.Ports[0].Width=.8;Check(RouteGeometry.Connect(new[]{parts[0],shifted},.002).Count==0,"section mismatch excluded");
        Fails(()=>RouteGeometry.Connect(new[]{parts[0],parts[1],P(Straight(1,new Vec(2,0,0)))},.002),"overlapping connection candidates fail closed");
        Fails(()=>RouteGeometry.Find(parts,new List<Connection>(),0,2),"unreachable destination fails");
        if(args.Length>0)
        {
            var cap=JsonSerializer.Deserialize<Capture>(File.ReadAllText(args[0]),new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true});
            var actual=cap.items.Select((x,i)=>i==1?RouteGeometry.Riser90(x.triangles,.6,.15):RouteGeometry.Straight(x.triangles,.6,.15)).ToArray();
            for(int i=0;i<actual.Length;i++){actual[i].Id=cap.items[i].path;actual[i].System="captured-route";}
            edges=RouteGeometry.Connect(actual,.002);route=RouteGeometry.Find(actual,edges,0,2);
            Check(route.Items.SequenceEqual(new[]{0,1,2})&&edges.Count==2,"real captured mesh connects through selected riser");
            Console.WriteLine(JsonSerializer.Serialize(new {parts=actual,connections=edges,route=route},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        }
        Console.WriteLine("RESULT: "+count+" checks passed.");
    }
}
