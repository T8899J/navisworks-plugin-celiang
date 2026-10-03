using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using JiePinPai.TrayMeasurement.Core;
using TrayRouteExperiment;
class NetworkAnalysis
{
    public class Capture{public string file;public List<Item> items;}
    public class Item{public string id,name,run,description,size,error;public List<Triangle> triangles;}
    static void Main(string[] args)
    {
        var json=new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true,WriteIndented=true};
        var capture=JsonSerializer.Deserialize<Capture>(File.ReadAllText(args[0]),json);
        var recognized=new List<Part>();var rejects=new List<object>();
        foreach(var i in capture.items)
        {
            if(i.triangles==null||!i.description.Contains("Straight",StringComparison.OrdinalIgnoreCase))continue;
            var m=StraightMeasurement.Measure(i.triangles,true);
            if(m.IsStraightCandidate)
            {
                recognized.Add(new Part{Id=i.id,Name=i.name,System=i.run,Kind="straight",Centerline=new[]{m.PortStart,m.PortEnd},Ports=new[]{new Port{Point=m.PortStart,Outward=m.Axis*(-1),Width=m.WidthMetres,Height=m.HeightMetres},new Port{Point=m.PortEnd,Outward=m.Axis,Width=m.WidthMetres,Height=m.HeightMetres}}});
            }
            else rejects.Add(new{i.name,i.size,m.Reason,m.SpanMetres,m.WidthMetres,m.HeightMetres,m.SectionVariation});
        }
        var junctions=new List<object>();
        for(int a=0;a<recognized.Count;a++)for(int b=0;b<recognized.Count;b++)
        {
            var p=recognized[a];var q=recognized[b];if(a==b||p.System==q.System)continue;
            var axis=q.Ports[1].Point-q.Ports[0].Point;var direction=axis*(1/axis.Norm);
            foreach(var port in p.Ports)
            {
                double t=(port.Point-q.Ports[0].Point).Dot(direction);
                if(t<.1||t>q.Length-.1||Math.Abs(direction.Dot(port.Outward))>.1)continue;
                var target=q.Ports[0].Point+direction*t;var delta=target-port.Point;
                double advance=delta.Dot(port.Outward);var offset=delta-port.Outward*advance;
                if(advance<-.005||advance>Math.Max(q.Ports[0].Width,q.Ports[0].Height)/2+.03||offset.Norm>.08)continue;
                junctions.Add(new{branch=p.Name,main=q.Name,branchId=p.Id,mainId=q.Id,branchLength=p.Length,mainLength=q.Length,branchWidth=port.Width,mainWidth=q.Ports[0].Width,advance=advance,offset=offset.Norm,station=t,point=port.Point,target=target});
            }
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{file=capture.file,recognized=recognized, rejected=rejects,junctions=junctions},json));
        Console.WriteLine($"Straight parts {recognized.Count}, rejected {rejects.Count}, possible junctions {junctions.Count}");
        Console.WriteLine(JsonSerializer.Serialize(junctions.Take(35),json));
    }
}
