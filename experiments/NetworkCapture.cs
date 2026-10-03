using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement;

[Plugin("TrayNetworkCapture","JPPM")]
public sealed class NetworkCapture:AddInPlugin
{
    static string Key(ModelItem x){return string.Join("/",((Array)ComApiBridge.ToInwOaPath(x).ArrayData).Cast<object>().Select(Convert.ToInt32));}
    static string Prop(ModelItem x,string name){foreach(var c in x.PropertyCategories)foreach(var p in c.Properties)if(p.DisplayName==name&&p.Value.IsDisplayString)return p.Value.ToDisplayString();return "";}
    public override int Execute(params string[] args)
    {
        var json=new JavaScriptSerializer{MaxJsonLength=200000000};var d=Autodesk.Navisworks.Api.Application.ActiveDocument;
        var clock=Stopwatch.StartNew();
        try
        {
            var candidates=d.Models.RootItems.SelectMany(r=>r.DescendantsAndSelf).Where(x=>x.DisplayName.Contains("TRAY-")&&!string.IsNullOrEmpty(Prop(x,"RunName"))).ToArray();
            var wanted=args.Length>1?new HashSet<string>(json.Deserialize<string[]>(File.ReadAllText(args[1]))):null;
            var output=new List<object>();
            foreach(var item in candidates)
            {
                string id=Key(item);if(wanted!=null&&!wanted.Contains(id))continue;
                if(clock.Elapsed.TotalSeconds>90)throw new InvalidOperationException("Capture limit exceeded; reduce the requested subset.");
                var b=item.BoundingBox();var nodes=item.DescendantsAndSelf.Where(x=>x.HasGeometry&&x.DisplayName=="Geometry").ToArray();
                object mesh=null;string error=null;
                if(wanted!=null)
                {
                    try{if(nodes.Length!=1)throw new InvalidOperationException("Geometry count="+nodes.Length);mesh=GeometryReader.Read(d,nodes[0]).Triangles;}
                    catch(Exception e){error=e.Message;}
                }
                output.Add(new{id=id,name=item.DisplayName,run=Prop(item,"RunName"),description=Prop(item,"Description"),size=Prop(item,"Size"),partNumber=Prop(item,"Part Number"),systemPath=Prop(item,"System Path"),
                    geometry=nodes.Select(Key).ToArray(),hidden=nodes.Select(x=>x.IsHidden).ToArray(),min=b.IsEmpty?null:new[]{b.Min.X,b.Min.Y,b.Min.Z},max=b.IsEmpty?null:new[]{b.Max.X,b.Max.Y,b.Max.Z},triangles=mesh,error=error});
            }
            File.WriteAllText(args[0],json.Serialize(new{file=d.FileName,units=d.Units.ToString(),selection=d.CurrentSelection.SelectedItems.Select(Key).ToArray(),count=output.Count,elapsedSeconds=clock.Elapsed.TotalSeconds,items=output}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(args[0],json.Serialize(new{error=e.ToString()}));return 1;}
    }
}
