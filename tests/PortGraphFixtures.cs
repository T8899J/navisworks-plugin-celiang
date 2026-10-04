using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;

public sealed class MeshFixture
{
    public string Name, RunName, Description, Size;
    public List<Triangle> Mesh;
    public double ExpectedWholeLength;
}

public static class PortGraphFixtures
{
    public const int ArcSteps = 32;
    public static double ArcLength(double radius, double angle, int steps = ArcSteps) { return steps * 2 * radius * Math.Sin(angle / (2 * steps)); }
    public static Vec Rotate(Vec p) { double a = Math.PI / 6; return new Vec(p.X + 8, p.Y * Math.Cos(a) - p.Z * Math.Sin(a) + 5, p.Y * Math.Sin(a) + p.Z * Math.Cos(a) + 3); }
    public static Vec RotateDirection(Vec p) { return Rotate(p) - Rotate(new Vec()); }
    public static List<Triangle> Transform(IEnumerable<Triangle> mesh, Func<Vec, Vec> f) { return mesh.Select(t => new Triangle(f(t.A), f(t.B), f(t.C))).ToList(); }
    static Vec Unit(Vec p) { return p * (1 / p.Norm); }
    static void Quad(List<Triangle> mesh, Vec a, Vec b, Vec c, Vec d) { mesh.Add(new Triangle(a,b,c)); mesh.Add(new Triangle(a,c,d)); }
    static Vec[] Section(Vec p, Vec widthAxis, Vec heightAxis, double width, double height, double thickness)
    {
        var x = new[] { -width/2, width/2, width/2, width/2-thickness, width/2-thickness, -width/2+thickness, -width/2+thickness, -width/2 };
        var y = new[] { -height/2, -height/2, height/2, height/2, -height/2+thickness, -height/2+thickness, height/2, height/2 };
        return Enumerable.Range(0,8).Select(i => p + widthAxis*x[i] + heightAxis*y[i]).ToArray();
    }
    static void Cap(List<Triangle> mesh, Vec[] p, bool reverse)
    {
        // The end is a U-shaped metal cross-section; its empty centre is never capped.
        int[][] faces = { new[]{0,1,4}, new[]{0,4,5}, new[]{1,2,3}, new[]{1,3,4}, new[]{0,5,6}, new[]{0,6,7} };
        foreach (var t in faces) mesh.Add(reverse ? new Triangle(p[t[2]],p[t[1]],p[t[0]]) : new Triangle(p[t[0]],p[t[1]],p[t[2]]));
    }
    public static List<Triangle> Sweep(IList<Vec> line, IList<Vec> widthAxes, IList<Vec> heightAxes, IList<double> widths, double height=.1, double thickness=.003)
    {
        var mesh = new List<Triangle>();
        var sections = Enumerable.Range(0,line.Count).Select(i=>Section(line[i],widthAxes[i],heightAxes[i],widths[i],height,thickness)).ToArray();
        for(int i=1;i<sections.Length;i++) for(int j=0;j<8;j++) Quad(mesh,sections[i-1][j],sections[i-1][(j+1)%8],sections[i][(j+1)%8],sections[i][j]);
        Cap(mesh,sections[0],true); Cap(mesh,sections.Last(),false); return mesh;
    }
    public static List<Triangle> Straight(Vec a, Vec b, double width=.4, double height=.1)
    {
        Vec direction=Unit(b-a), up=Math.Abs(direction.Z)>.9?new Vec(0,1,0):new Vec(0,0,1);
        Vec across=Unit(up.Cross(direction)); up=direction.Cross(across);
        return Sweep(new[]{a,b},new[]{across,across},new[]{up,up},new[]{width,width},height);
    }
    public static List<Triangle> Reducer(Vec a, Vec b, double widthA=.4, double widthB=.6, double height=.1)
    {
        Vec direction=Unit(b-a), up=Math.Abs(direction.Z)>.9?new Vec(0,1,0):new Vec(0,0,1);
        Vec across=Unit(up.Cross(direction)); up=direction.Cross(across);
        return Sweep(Enumerable.Range(0,5).Select(i=>a+(b-a)*(i/4.0)).ToArray(),Enumerable.Repeat(across,5).ToArray(),Enumerable.Repeat(up,5).ToArray(),Enumerable.Range(0,5).Select(i=>widthA+(widthB-widthA)*i/4.0).ToArray(),height);
    }
    public static List<Triangle> Bend(double angle=Math.PI/2, bool riser=false, double radius=1, int steps=ArcSteps)
    {
        var line=new List<Vec>();var widthAxes=new List<Vec>();var heightAxes=new List<Vec>();
        for(int i=0;i<=steps;i++)
        {
            double a=-Math.PI/2+angle*i/steps; var radial=new Vec(Math.Cos(a),Math.Sin(a),0);
            line.Add(new Vec(2,1,0)+radial*radius);
            widthAxes.Add(riser?new Vec(0,0,1):radial);
            heightAxes.Add(riser?radial:new Vec(0,0,1));
        }
        return Sweep(line,widthAxes,heightAxes,Enumerable.Repeat(.4,steps+1).ToArray());
    }
    static double Cross2(Vec a,Vec b,Vec c) { return (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X); }
    public static List<Triangle> FoldedChannel(Vec[] first,Vec[] second,double depth,bool riser)
    {
        // Independent forward model of a thin U-channel with two explicit polygonal rails.
        var contour=first.Concat(second.Reverse()).ToArray();
        double area=0;for(int i=0;i<contour.Length;i++)area+=contour[i].X*contour[(i+1)%contour.Length].Y-contour[i].Y*contour[(i+1)%contour.Length].X;
        if(area<0)Array.Reverse(contour);
        var mesh=new List<Triangle>();
        Func<Vec,double,Vec> p=(v,z)=>new Vec(v.X,v.Y,z);
        foreach(var t in Triangulate(contour))
        {
            mesh.Add(new Triangle(p(contour[t[0]],-depth/2),p(contour[t[1]],-depth/2),p(contour[t[2]],-depth/2)));
            if(riser)mesh.Add(new Triangle(p(contour[t[2]],depth/2),p(contour[t[1]],depth/2),p(contour[t[0]],depth/2)));
        }
        foreach(var rail in riser?new[]{first}:new[]{first,second})
            for(int i=1;i<rail.Length;i++)Quad(mesh,p(rail[i-1],-depth/2),p(rail[i],-depth/2),p(rail[i],depth/2),p(rail[i-1],depth/2));
        return mesh;
    }
    public static List<Triangle> Folded90(bool riser=false)
    {
        double m=.02*Math.Tan(Math.PI/8);
        return FoldedChannel(new[]{new Vec(-.2,-.02,0),new Vec(m,-.02,0),new Vec(.22,.2-m,0),new Vec(.22,.4,0)},
            new[]{new Vec(-.2,.02,0),new Vec(-m,.02,0),new Vec(.18,.2+m,0),new Vec(.18,.4,0)},.05,riser);
    }
    public static List<Triangle> Bevel45()
    {
        double c=Math.Sqrt(.5);
        return FoldedChannel(new[]{new Vec(-.15,0,0),new Vec(),new Vec(.15*c,.15*c,0)},
            new[]{new Vec(-.15,.05,0),new Vec(-.04,.05,0),new Vec(-.01*c,.09*c,0),new Vec(.10*c,.20*c,0)},.04,false);
    }
    static bool Inside(Vec p,Vec a,Vec b,Vec c) { return Cross2(a,b,p)>=-1e-12&&Cross2(b,c,p)>=-1e-12&&Cross2(c,a,p)>=-1e-12; }
    static List<int[]> Triangulate(Vec[] polygon)
    {
        var remaining=Enumerable.Range(0,polygon.Length).ToList();var result=new List<int[]>();
        while(remaining.Count>3)
        {
            bool clipped=false;
            for(int j=0;j<remaining.Count;j++)
            {
                int a=remaining[(j+remaining.Count-1)%remaining.Count],b=remaining[j],c=remaining[(j+1)%remaining.Count];
                if(Cross2(polygon[a],polygon[b],polygon[c])<=1e-12)continue;
                if(remaining.Any(k=>k!=a&&k!=b&&k!=c&&Inside(polygon[k],polygon[a],polygon[b],polygon[c])))continue;
                result.Add(new[]{a,b,c});remaining.RemoveAt(j);clipped=true;break;
            }
            if(!clipped)throw new InvalidOperationException("Invalid fixture polygon");
        }
        result.Add(remaining.ToArray()); return result;
    }
    static List<Triangle> Extrude(Vec[] polygon,double low,double high)
    {
        var mesh=new List<Triangle>();var lo=polygon.Select(p=>new Vec(p.X,p.Y,low)).ToArray();var hi=polygon.Select(p=>new Vec(p.X,p.Y,high)).ToArray();
        foreach(var t in Triangulate(polygon)){mesh.Add(new Triangle(lo[t[2]],lo[t[1]],lo[t[0]]));mesh.Add(new Triangle(hi[t[0]],hi[t[1]],hi[t[2]]));}
        for(int i=0;i<polygon.Length;i++)Quad(mesh,lo[i],lo[(i+1)%polygon.Length],hi[(i+1)%polygon.Length],hi[i]);
        return mesh;
    }
    public static List<Triangle> Tee(double mainWidth=.4,double branchWidth=.2,double height=.1,double thickness=.003)
    {
        double a=mainWidth/2,b=branchWidth/2;
        var contour=new[]{new Vec(-.5,-a,0),new Vec(-b,-a,0),new Vec(-b,-.5,0),new Vec(b,-.5,0),new Vec(b,-a,0),new Vec(.5,-a,0),new Vec(.5,a,0),new Vec(-.5,a,0)};
        var mesh=Extrude(contour,-height/2,-height/2+thickness);
        // All three mouths stay open; no hidden caps divide the junction into three objects.
        foreach(int i in new[]{0,1,3,4,6})
        {
            var p=contour[i];var q=contour[(i+1)%contour.Length];var tangent=Unit(q-p);var inside=new Vec(-tangent.Y,tangent.X,0)*thickness;
            mesh.AddRange(Extrude(new[]{p,q,q+inside,p+inside},-height/2+thickness,height/2));
        }
        return mesh;
    }
    public static List<MeshFixture> Chain()
    {
        var list=new List<MeshFixture>{
            new MeshFixture{Name="Fixture-Branch-Start",RunName="Branch-Run-A",Description="Straight",Size="200 x 100 mm",Mesh=Straight(new Vec(0,-2.5,0),new Vec(0,-.5,0),.2),ExpectedWholeLength=2},
            new MeshFixture{Name="Fixture-Tee",RunName="Junction-Run-B",Description="Tee",Size="400 x 100 / 200 x 100 mm",Mesh=Tee(),ExpectedWholeLength=1.5},
            new MeshFixture{Name="Fixture-Main-Before-Elbow",RunName="Main-Run-C",Description="Straight",Size="400 x 100 mm",Mesh=Straight(new Vec(.5,0,0),new Vec(2,0,0)),ExpectedWholeLength=1.5},
            new MeshFixture{Name="Fixture-Elbow-90",RunName="Bend-Run-D",Description="Elbow90",Size="400 x 100 mm",Mesh=Bend(),ExpectedWholeLength=ArcLength(1,Math.PI/2)},
            new MeshFixture{Name="Fixture-Slope",RunName="Slope-Run-E",Description="Slope",Size="400 x 100 mm",Mesh=Straight(new Vec(3,1,0),new Vec(3,3,0)),ExpectedWholeLength=2},
            new MeshFixture{Name="Fixture-Reducer",RunName="Transition-Run-F",Description="Reducer",Size="400 x 100 -> 600 x 100 mm",Mesh=Reducer(new Vec(3,3,0),new Vec(3,4,0)),ExpectedWholeLength=1},
            new MeshFixture{Name="Fixture-Main-Finish",RunName="Finish-Run-G",Description="Straight",Size="600 x 100 mm",Mesh=Straight(new Vec(3,4,0),new Vec(3,6,0),.6),ExpectedWholeLength=2}
        };
        foreach(var f in list)f.Mesh=Transform(f.Mesh,Rotate); return list;
    }
    public static Vec Start { get { return Rotate(new Vec(0,-2,0)); } }
    public static Vec Finish { get { return Rotate(new Vec(3,5.5,0)); } }
    public static double ExpectedRouteLength { get { return 8.5+ArcLength(1,Math.PI/2); } }
    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);var chain=Chain();
        var json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        File.WriteAllText(Path.Combine(directory,"port-graph-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",startName=chain[0].Name,finishName=chain.Last().Name,start=Start,finish=Finish,expectedRouteLength=ExpectedRouteLength,expectedContributions=new[]{1.5,1,1.5,ArcLength(1,Math.PI/2),2,1,1.5},items=chain},json));
        File.WriteAllText(Path.Combine(directory,"port-graph-fixture.ifc"),Ifc(chain),Encoding.ASCII);
    }
    public static void WriteGapBridgeArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);var origin=new Vec(8,5,3);var direction=Unit(new Vec(1,2,3));
        var parts=new List<MeshFixture>{
            new MeshFixture{Name="Fixture-Gap-Tray-A",RunName="Gap-Run-A",Description="Slope",Size="400 x 100 mm",Mesh=Straight(origin,origin+direction),ExpectedWholeLength=1},
            new MeshFixture{Name="Fixture-Gap-Tray-B",RunName="Gap-Run-B",Description="Slope",Size="400 x 100 mm",Mesh=Straight(origin+direction*1.03,origin+direction*2.03),ExpectedWholeLength=1}
        };
        File.WriteAllText(Path.Combine(directory,"gap-bridge-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",
            startName=parts[0].Name,finishName=parts[1].Name,start=origin,finish=origin+direction*2.03,
            expectedRouteLength=2.03,expectedContributions=new[]{1.0,1.0},expectedGapCount=1,expectedGapLength=.03,items=parts},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        File.WriteAllText(Path.Combine(directory,"gap-bridge-fixture.ifc"),Ifc(parts),Encoding.ASCII);
    }
    public static void WriteVirtualConnectorArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);var origin=new Vec(8,5,3);var direction=Unit(new Vec(1,2,3));var delta=new Vec(.25,-.1,.3);
        var portParts=new List<MeshFixture>{
            new MeshFixture{Name="Fixture-Virtual-Port-A",RunName="Virtual-Port-Run-A",Description="Slope",Size="200 x 100 mm",Mesh=Straight(origin-direction,origin,.2),ExpectedWholeLength=1},
            new MeshFixture{Name="Fixture-Virtual-Port-B",RunName="Virtual-Port-Run-B",Description="Slope",Size="400 x 100 mm",Mesh=Straight(origin+delta,origin+delta+direction),ExpectedWholeLength=1}
        };
        var json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        File.WriteAllText(Path.Combine(directory,"virtual-port-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",
            startName=portParts[0].Name,finishName=portParts[1].Name,start=origin-direction,finish=origin+delta+direction,
            expectedRouteLength=2+Math.Sqrt(.1625),expectedContributions=new[]{1.0,1.0},expectedVirtualCount=1,expectedVirtualLength=Math.Sqrt(.1625),expectedVirtualKind="PortToPort3D",items=portParts},json));
        File.WriteAllText(Path.Combine(directory,"virtual-port-fixture.ifc"),Ifc(portParts),Encoding.ASCII);

        origin=new Vec(15,25,4);var q=origin+direction*.7;delta=Unit(direction.Cross(new Vec(.6,-.3,.1)))*.35;
        var segmentParts=new List<MeshFixture>{
            new MeshFixture{Name="Fixture-Virtual-Branch",RunName="Virtual-Branch-Run",Description="Slope",Size="200 x 100 mm",Mesh=Straight(q-delta-Unit(delta)*2,q-delta,.2),ExpectedWholeLength=2},
            new MeshFixture{Name="Fixture-Virtual-Main",RunName="Virtual-Main-Run",Description="Slope",Size="400 x 100 mm",Mesh=Straight(origin-direction*3,origin+direction*5),ExpectedWholeLength=8}
        };
        File.WriteAllText(Path.Combine(directory,"virtual-segment-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",
            startName=segmentParts[0].Name,finishName=segmentParts[1].Name,start=q-delta-Unit(delta)*2,finish=origin+direction*5,
            expectedRouteLength=6.65,expectedContributions=new[]{2.0,4.3},expectedVirtualCount=1,expectedVirtualLength=.35,
            expectedVirtualKind="PortToSegment3D",expectedTargetStation=3.7,expectedTargetPoint=q,items=segmentParts},json));
        File.WriteAllText(Path.Combine(directory,"virtual-segment-fixture.ifc"),Ifc(segmentParts),Encoding.ASCII);
    }
    public static void WriteConnectivityArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);var json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var chain=Chain();var maintenance=new MeshFixture{Name="Maintenance Volume",RunName="Excluded-Run",Description="Straight",Size="400 x 100 mm",
            Mesh=Straight(new Vec(30,30,0),new Vec(32,30,0)),ExpectedWholeLength=2};
        File.WriteAllText(Path.Combine(directory,"visibility-fixture.ifc"),Ifc(chain.Concat(new[]{maintenance}).ToList()),Encoding.ASCII);
        File.WriteAllText(Path.Combine(directory,"visibility-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",startName=chain[0].Name,finishName=chain.Last().Name,
            start=Start,finish=Finish,expectedRouteLength=ExpectedRouteLength,expectedContributions=new[]{1.5,1,1.5,ArcLength(1,Math.PI/2),2,1,1.5},items=chain},json));
        var ambiguous=new List<MeshFixture>{
            new MeshFixture{Name="Fixture-TopN-Branch",RunName="TopN-Branch",Description="Straight",Size="200 x 100 mm",Mesh=Straight(new Vec(-2,0,0),new Vec(),.2),ExpectedWholeLength=2},
            new MeshFixture{Name="Fixture-TopN-Target",RunName="TopN-Target",Description="Straight",Size="600 x 100 mm",Mesh=Straight(new Vec(.3,0,.2),new Vec(2.3,0,.2),.6),ExpectedWholeLength=2},
            new MeshFixture{Name="Fixture-TopN-Alternative",RunName="TopN-Alternative",Description="Straight",Size="400 x 100 mm",Mesh=Straight(new Vec(.4,0,-.2),new Vec(2.4,0,-.2)),ExpectedWholeLength=2}
        };
        File.WriteAllText(Path.Combine(directory,"multiple-candidate-fixture.ifc"),Ifc(ambiguous),Encoding.ASCII);
        File.WriteAllText(Path.Combine(directory,"multiple-candidate-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",startName=ambiguous[0].Name,finishName=ambiguous[1].Name,
            start=new Vec(-2,0,0),finish=new Vec(2.3,0,.2),expectedRouteLength=4+Math.Sqrt(.13),expectedContributions=new[]{2.0,2.0,0.0},
            expectedVirtualCount=1,expectedVirtualLength=Math.Sqrt(.13),expectedVirtualKind="PortToPort3D",minimumSourceCandidates=2,items=ambiguous},json));
        foreach(bool raised in new[]{false,true})
        {
            double z=raised?.25:0;var parts=new List<MeshFixture>{
                new MeshFixture{Name="Fixture-Horizontal-Branch",RunName="Short-Branch",Description="Straight",Size="200 x 100 mm",Mesh=Straight(new Vec(3,-2,z),new Vec(3,-.3,z),.2),ExpectedWholeLength=1.7},
                new MeshFixture{Name="Fixture-Horizontal-Main",RunName="Other-Run-Main",Description="Straight",Size="400 x 100 mm",Mesh=Straight(new Vec(),new Vec(10,0,0)),ExpectedWholeLength=10}
            };
            double length=Math.Sqrt(.09+z*z);string name=raised?"raised-segment-fixture":"horizontal-segment-fixture";
            File.WriteAllText(Path.Combine(directory,name+".ifc"),Ifc(parts),Encoding.ASCII);
            File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",startName=parts[0].Name,finishName=parts[1].Name,
                start=new Vec(3,-2,z),finish=new Vec(8,0,0),expectedRouteLength=6.7+length,expectedContributions=new[]{1.7,5.0},
                expectedVirtualCount=1,expectedVirtualLength=length,expectedVirtualKind="PortToSegment3D",expectedTargetStation=3.0,expectedTargetPoint=new Vec(3,0,0),items=parts},json));
        }
        var failed=new List<MeshFixture>{
            new MeshFixture{Name="Fixture-Disconnected-Branch",RunName="Failure-Branch",Description="Straight",Size="200 x 100 mm",Mesh=Straight(new Vec(3,-2,.25),new Vec(3,-.8,.25),.2),ExpectedWholeLength=1.2},
            new MeshFixture{Name="Fixture-Disconnected-Main",RunName="Failure-Other-Main",Description="Straight",Size="400 x 100 mm",Mesh=Straight(new Vec(),new Vec(10,0,0)),ExpectedWholeLength=10}
        };
        File.WriteAllText(Path.Combine(directory,"disconnected-fixture.ifc"),Ifc(failed),Encoding.ASCII);
        File.WriteAllText(Path.Combine(directory,"disconnected-fixture.json"),JsonSerializer.Serialize(new{synthetic=true,units="metres",startName=failed[0].Name,finishName=failed[1].Name,
            start=new Vec(3,-2,.25),finish=new Vec(8,0,0),expectedFailure=true,expectedNearestDistance=Math.Sqrt(.64+.0625),items=failed},json));
    }
    static string Ifc(List<MeshFixture> fixtures)
    {
        var e=new List<string>();Func<string,string> add=body=>{e.Add("#"+(e.Count+1)+"="+body+";");return "#"+e.Count;};
        Func<double,string> number=d=>{var s=d.ToString("R",CultureInfo.InvariantCulture);return s.Contains(".")||s.Contains("E")?s:s+".";};
        Func<Vec,string> point=p=>add("IFCCARTESIANPOINT(("+number(p.X)+","+number(p.Y)+","+number(p.Z)+"))");
        int counter=0;Func<string> guid=()=>"0PG"+(++counter).ToString("D19",CultureInfo.InvariantCulture);
        string person=add("IFCPERSON($,'Fixture','Synthetic',$,$,$,$,$)"),org=add("IFCORGANIZATION($,'Local cable graph fixture',$,$,$)"),personOrg=add("IFCPERSONANDORGANIZATION("+person+","+org+",$)"),app=add("IFCAPPLICATION("+org+",'1','Port Graph Fixture','PGF')"),owner=add("IFCOWNERHISTORY("+personOrg+","+app+",$,.ADDED.,$,$,$,0)");
        string origin=point(new Vec()),z=add("IFCDIRECTION((0.,0.,1.))"),x=add("IFCDIRECTION((1.,0.,0.))"),axis=add("IFCAXIS2PLACEMENT3D("+origin+","+z+","+x+")"),placement=add("IFCLOCALPLACEMENT($,"+axis+")"),context=add("IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-6,"+axis+",$)"),unit=add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)"),units=add("IFCUNITASSIGNMENT(("+unit+"))");
        string project=add("IFCPROJECT('"+guid()+"',"+owner+",'Synthetic cable port graph',$,$,$,$,("+context+"),"+units+")"),building=add("IFCBUILDING('"+guid()+"',"+owner+",'SYNTHETIC - not a project model',$,$,"+placement+",$,$,.ELEMENT.,$,$,$)");
        add("IFCRELAGGREGATES('"+guid()+"',"+owner+",$,$,"+project+",("+building+"))");var products=new List<string>();
        foreach(var fixture in fixtures)
        {
            var vertices=new Dictionary<string,string>();Func<Vec,string> vertex=p=>{string key=number(p.X)+","+number(p.Y)+","+number(p.Z);if(!vertices.ContainsKey(key))vertices[key]=point(p);return vertices[key];};
            var faces=new List<string>();
            foreach(var t in fixture.Mesh)
            {
                string a=vertex(t.A),b=vertex(t.B),c=vertex(t.C),loop=add("IFCPOLYLOOP(("+a+","+b+","+c+"))"),bound=add("IFCFACEOUTERBOUND("+loop+",.T.)");faces.Add(add("IFCFACE(("+bound+"))"));
            }
            string faceSet=add("IFCCONNECTEDFACESET(("+string.Join(",",faces)+"))"),surface=add("IFCFACEBASEDSURFACEMODEL(("+faceSet+"))"),shape=add("IFCSHAPEREPRESENTATION("+context+",'Body','SurfaceModel',("+surface+"))"),definition=add("IFCPRODUCTDEFINITIONSHAPE($,$,("+shape+"))"),product=add("IFCFLOWSEGMENT('"+guid()+"',"+owner+",'"+fixture.Name+"','"+fixture.Description+"','Cable tray',"+placement+","+definition+",$)");products.Add(product);
            var properties=new[]{new[]{"Description",fixture.Description},new[]{"RunName",fixture.RunName},new[]{"Size",fixture.Size},new[]{"SyntheticFixture","TRUE"}}.Select(pair=>add("IFCPROPERTYSINGLEVALUE('"+pair[0]+"',$,IFCTEXT('"+pair[1]+"'),$)")).ToArray();
            string pset=add("IFCPROPERTYSET('"+guid()+"',"+owner+",'Cable Routing Fixture',$,("+string.Join(",",properties)+"))");add("IFCRELDEFINESBYPROPERTIES('"+guid()+"',"+owner+",$,$,("+product+"),"+pset+")");
        }
        add("IFCRELCONTAINEDINSPATIALSTRUCTURE('"+guid()+"',"+owner+",$,$,("+string.Join(",",products)+"),"+building+")");
        return "ISO-10303-21;\r\nHEADER;\r\nFILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');\r\nFILE_NAME('port-graph-fixture.ifc','2026-10-03T00:00:00',('Synthetic'),('Local'),'PortGraphFixture','PortGraphFixture','');\r\nFILE_SCHEMA(('IFC2X3'));\r\nENDSEC;\r\nDATA;\r\n"+string.Join("\r\n",e)+"\r\nENDSEC;\r\nEND-ISO-10303-21;\r\n";
    }
}
